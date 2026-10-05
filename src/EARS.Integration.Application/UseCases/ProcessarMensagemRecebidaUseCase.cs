using System.Globalization;
using System.Text.Json;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.Options;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using EARS.Integration.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Application.UseCases;

/// <summary>
/// Opção de profissional guardada entre turnos da conversa, em JSON.
/// Existe porque o paciente responde com o número da opção, não com o
/// codigoProfissional — e o código é o que o listar-horarios precisa.
/// </summary>
public sealed record OpcaoProfissional(string Codigo, string Nome);

/// <summary>
/// Módulo 1: máquina de estados do agendamento pela conversa.
/// Ponto de entrada de toda mensagem recebida via webhook do SleekFlow.
/// </summary>
/// <remarks>
/// Três pontos onde o formato da API da Amplimed decide o desenho do diálogo:
///
/// <list type="number">
/// <item>Não existe busca de paciente por telefone, só por CPF, nome ou
/// codReferencia. Por isso o CPF é pedido na conversa — não é burocracia.</item>
/// <item>O <c>cadastrar-agendamento</c> faz upsert do paciente procurando por
/// idUnico e, se não achar, por CPF + data de nascimento. Quando o CPF não existe
/// na base, a data de nascimento passa a ser necessária; sem ela, o cadastro
/// sairia duplicado.</item>
/// <item>Marcar exige o <c>idHorario</c> devolvido por <c>listar-horarios</c>: um
/// token opaco, não data e hora. Guardá-lo é o que permite revalidar o slot antes
/// de gravar.</item>
/// </list>
/// </remarks>
public sealed class ProcessarMensagemRecebidaUseCase(
    IConversaRepository conversas,
    IPacienteRepository pacientes,
    IAgendamentoRepository agendamentos,
    IAmplimedPacientesClient pacientesClient,
    IAmplimedAgendamentoClient agendamentoClient,
    ISleekFlowClient sleekFlow,
    IMensagemLogRepository logs,
    IUnitOfWork uow,
    IRelogio relogio,
    IOptions<AutomacaoOptions> options,
    ILogger<ProcessarMensagemRecebidaUseCase> logger)
{
    private readonly AutomacaoOptions _opcoes = options.Value;

    public async Task ExecutarAsync(MensagemRecebida mensagem, CancellationToken ct = default)
    {
        await logs.AdicionarAsync(new MensagemLog(
            mensagem.Telefone, TipoMensagem.Recebida, null,
            mensagem.BotaoPayload ?? mensagem.Texto ?? string.Empty,
            null, sucesso: true), ct);

        var conversa = await conversas.ObterAtivaPorTelefoneAsync(mensagem.Telefone, ct);

        if (conversa is null || conversa.Expirada(_opcoes.ExpiracaoConversa))
        {
            conversa = new Conversa(mensagem.Telefone, mensagem.ContatoId);
            await conversas.AdicionarAsync(conversa, ct);
        }

        if (PedeAtendimentoHumano(mensagem))
        {
            conversa.TransferirParaHumano();
            await sleekFlow.TransferirParaAtendimentoAsync(
                mensagem.Telefone, "Paciente solicitou atendimento humano.", ct);
            await uow.SalvarAsync(ct);
            return;
        }

        try
        {
            await AvancarAsync(conversa, mensagem, ct);
        }
        catch (Exception ex)
        {
            // Escape humano: qualquer falha inesperada vira atendimento, com contexto.
            logger.LogError(ex, "Falha ao processar a conversa {ConversaId}.", conversa.Id);
            conversa.TransferirParaHumano();
            await sleekFlow.TransferirParaAtendimentoAsync(
                mensagem.Telefone, "Não foi possível concluir o agendamento automaticamente.", ct);
        }

        await uow.SalvarAsync(ct);
    }

    private async Task AvancarAsync(Conversa conversa, MensagemRecebida mensagem, CancellationToken ct)
    {
        var texto = (mensagem.Texto ?? string.Empty).Trim();

        switch (conversa.Estado)
        {
            case EstadoConversa.Inicio:
                await Responder(mensagem.Telefone,
                    "Olá! Para marcar sua consulta, me diga seu nome completo, por favor.", ct);
                conversa.TransicionarPara(EstadoConversa.AguardandoIdentificacao);
                break;

            case EstadoConversa.AguardandoIdentificacao:
                if (texto.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
                {
                    await Responder(mensagem.Telefone, "Preciso do nome completo, por favor.", ct);
                    break;
                }
                conversa.RegistrarNome(texto);
                await Responder(mensagem.Telefone, "Obrigado. Agora me informe seu CPF.", ct);
                conversa.TransicionarPara(EstadoConversa.AguardandoCpf);
                break;

            case EstadoConversa.AguardandoCpf:
                if (!Cpf.TryCreate(texto, out var cpf) || cpf is null)
                {
                    await Responder(mensagem.Telefone, "Esse CPF não parece válido. Pode conferir?", ct);
                    break;
                }

                conversa.RegistrarCpf(cpf.Value);

                if (await ResolverPacienteAsync(conversa, mensagem, cpf, ct))
                {
                    await OfertarProfissionaisAsync(conversa, mensagem.Telefone, ct);
                    conversa.TransicionarPara(EstadoConversa.AguardandoProfissional);
                }
                else
                {
                    await Responder(mensagem.Telefone,
                        "Não encontrei seu cadastro. Qual é a sua data de nascimento? (dd/mm/aaaa)", ct);
                    conversa.TransicionarPara(EstadoConversa.AguardandoDataNascimento);
                }
                break;

            case EstadoConversa.AguardandoDataNascimento:
                if (!TentarLerData(texto, out var nascimento))
                {
                    await Responder(mensagem.Telefone,
                        "Não consegui ler a data. Pode escrever assim: 05/02/1990?", ct);
                    break;
                }

                conversa.RegistrarDataNascimento(nascimento);
                await CriarPacienteLocalAsync(conversa, mensagem, nascimento, ct);
                await OfertarProfissionaisAsync(conversa, mensagem.Telefone, ct);
                conversa.TransicionarPara(EstadoConversa.AguardandoProfissional);
                break;

            case EstadoConversa.AguardandoProfissional:
                var escolhido = EscolherProfissional(conversa, mensagem);
                if (escolhido is null)
                {
                    await Responder(mensagem.Telefone,
                        "Não entendi. Responda com o número do profissional, por favor.", ct);
                    break;
                }

                conversa.RegistrarProfissional(escolhido.Codigo);
                await OfertarHorariosAsync(conversa, mensagem.Telefone, escolhido.Codigo, ct);
                conversa.TransicionarPara(EstadoConversa.AguardandoEscolhaHorario);
                break;

            case EstadoConversa.AguardandoEscolhaHorario:
                await ConcluirAgendamentoAsync(conversa, mensagem, ct);
                break;

            default:
                conversa.TransferirParaHumano();
                await sleekFlow.TransferirParaAtendimentoAsync(mensagem.Telefone, "Conversa em estado final.", ct);
                break;
        }
    }

    /// <summary>
    /// Resolve o paciente por CPF: espelho local primeiro, depois a API de
    /// Pacientes. Devolve <c>false</c> quando o CPF não existe em nenhum dos dois
    /// e a data de nascimento passa a ser necessária.
    /// </summary>
    private async Task<bool> ResolverPacienteAsync(
        Conversa conversa, MensagemRecebida mensagem, Cpf cpf, CancellationToken ct)
    {
        var local = await pacientes.ObterPorCpfAsync(cpf.Value, ct);
        if (local is not null)
        {
            if (Telefone.TryCreate(mensagem.Telefone, out var atual) && atual is not null
                && local.Telefone != atual.Value)
            {
                // O paciente escreveu de outro número; o canal passa a ser este.
                local.AtualizarContato(local.Nome, atual);
            }

            conversa.VincularPaciente(local.Id);
            return true;
        }

        var remoto = await pacientesClient.BuscarPorCpfAsync(cpf.Value, ct);
        if (remoto is null) return false;

        var paciente = await CriarPacienteLocalAsync(
            conversa, mensagem, remoto.DataNascimento, ct, nomeAmplimed: remoto.Nome);

        // Cadastro antigo da clínica sem codReferencia: amarra o nosso agora, para
        // que o upsert do cadastrar-agendamento encontre o paciente por idUnico.
        if (string.IsNullOrWhiteSpace(remoto.CodReferencia))
        {
            if (await pacientesClient.VincularCodReferenciaAsync(cpf.Value, paciente.CodReferencia, ct))
                paciente.MarcarCodReferenciaVinculado();
        }

        return true;
    }

    private async Task<Paciente> CriarPacienteLocalAsync(
        Conversa conversa,
        MensagemRecebida mensagem,
        DateOnly? dataNascimento,
        CancellationToken ct,
        string? nomeAmplimed = null)
    {
        // Não há POST /pattient aqui de propósito: o cadastrar-agendamento faz o
        // upsert do paciente na Amplimed, procurando por idUnico e depois por
        // CPF + data de nascimento. Criar antes seria uma chamada a mais e um
        // caminho a mais para gerar cadastro duplicado.
        var paciente = new Paciente(
            nomeAmplimed ?? conversa.NomeInformado ?? "Paciente",
            Cpf.Create(conversa.CpfInformado!),
            Telefone.Create(mensagem.Telefone),
            dataNascimento ?? conversa.DataNascimentoInformada);

        await pacientes.AdicionarAsync(paciente, ct);
        await uow.SalvarAsync(ct);

        conversa.VincularPaciente(paciente.Id);
        return paciente;
    }

    private async Task OfertarProfissionaisAsync(Conversa conversa, string telefone, CancellationToken ct)
    {
        var hoje = relogio.HojeLocal;

        var profissionais = await agendamentoClient.ListarProfissionaisAsync(
            new FiltroProfissionais(hoje, hoje.AddDays(_opcoes.DiasHorizonteOferta)), ct);

        var opcoes = profissionais
            .Select(p => new OpcaoProfissional(p.CodigoProfissional, p.Nome))
            .ToList();

        if (opcoes.Count == 0)
        {
            conversa.TransferirParaHumano();
            await sleekFlow.TransferirParaAtendimentoAsync(
                telefone, "Nenhum profissional com agenda aberta para integração.", ct);
            return;
        }

        conversa.RegistrarProfissionaisOfertados(JsonSerializer.Serialize(opcoes));

        var linhas = opcoes.Select((p, i) => $"{i + 1}. {p.Nome}");
        await Responder(telefone, "Com qual profissional você quer marcar?\n" + string.Join('\n', linhas), ct);
    }

    private OpcaoProfissional? EscolherProfissional(Conversa conversa, MensagemRecebida mensagem)
    {
        var opcoes = Desserializar<OpcaoProfissional>(conversa.ProfissionaisOfertadosJson);
        var indice = InterpretarEscolha(mensagem, opcoes.Count);

        // O botão pode trazer o código do profissional direto, em vez do número.
        if (indice is null && !string.IsNullOrWhiteSpace(mensagem.BotaoPayload))
        {
            var porCodigo = opcoes.FirstOrDefault(p => p.Codigo == mensagem.BotaoPayload!.Trim());
            if (porCodigo is not null) return porCodigo;
        }

        return indice is null ? null : opcoes[indice.Value];
    }

    private async Task OfertarHorariosAsync(
        Conversa conversa, string telefone, string codigoProfissional, CancellationToken ct)
    {
        var hoje = relogio.HojeLocal;

        var horarios = await agendamentoClient.ListarHorariosDisponiveisAsync(
            codigoProfissional, hoje, hoje.AddDays(_opcoes.DiasHorizonteOferta), ct);

        var oferta = horarios
            .Where(h => h.Inicio > relogio.AgoraLocal)
            .Take(_opcoes.MaxHorariosOfertados)
            .ToList();

        if (oferta.Count == 0)
        {
            conversa.TransferirParaHumano();
            await sleekFlow.TransferirParaAtendimentoAsync(
                telefone, "Não há horários livres para o profissional escolhido.", ct);
            return;
        }

        conversa.RegistrarHorariosOfertados(JsonSerializer.Serialize(oferta));

        var linhas = oferta.Select((h, i) => $"{i + 1}. {h.Inicio:dd/MM} às {h.Inicio:HH:mm}");
        await Responder(telefone, "Estes horários estão livres:\n" + string.Join('\n', linhas), ct);
    }

    private async Task ConcluirAgendamentoAsync(
        Conversa conversa, MensagemRecebida mensagem, CancellationToken ct)
    {
        var oferta = Desserializar<HorarioDisponivel>(conversa.HorariosOfertadosJson);
        var escolha = InterpretarEscolha(mensagem, oferta.Count);

        if (escolha is null)
        {
            await Responder(mensagem.Telefone, "Não entendi a escolha. Responda com o número da opção.", ct);
            return;
        }

        var horario = oferta[escolha.Value];

        var paciente = conversa.PacienteId is null || conversa.CpfInformado is null
            ? null
            : await pacientes.ObterPorCpfAsync(conversa.CpfInformado, ct);

        if (paciente is null)
            throw new InvalidOperationException("Paciente não resolvido na conversa.");

        var profissionalNome = Desserializar<OpcaoProfissional>(conversa.ProfissionaisOfertadosJson)
            .FirstOrDefault(p => p.Codigo == horario.CodigoProfissional)?.Nome
            ?? horario.CodigoProfissional;

        // Revalida pelo idHorario — o token do slot — e não por data e hora:
        // entre a oferta e a resposta o horário pode ter sido ocupado por outro canal.
        var diaDoHorario = DateOnly.FromDateTime(horario.Inicio.Date);
        var aindaLivres = await agendamentoClient.ListarHorariosDisponiveisAsync(
            horario.CodigoProfissional, diaDoHorario, diaDoHorario, ct);

        if (aindaLivres.All(h => h.IdHorario != horario.IdHorario))
        {
            await Responder(mensagem.Telefone,
                "Esse horário acabou de ser ocupado. Vou buscar outras opções.", ct);
            await OfertarHorariosAsync(conversa, mensagem.Telefone, horario.CodigoProfissional, ct);
            return;
        }

        var criado = await agendamentoClient.CadastrarAsync(
            new CadastrarAgendamentoRequest(
                IdUnico: paciente.CodReferencia,
                IdHorario: horario.IdHorario,
                Cpf: paciente.Cpf,
                Nome: paciente.Nome,
                DataNascimento: paciente.DataNascimento,
                Celular: paciente.Telefone,
                Observacoes: "Agendado pela automação de WhatsApp."),
            ct);

        await agendamentos.AdicionarAsync(Agendamento.DaIntegracao(
            criado.IdIntegracao,
            horario.IdHorario,
            paciente.Id,
            horario.CodigoProfissional,
            profissionalNome,
            horario.Inicio), ct);

        await Responder(mensagem.Telefone,
            $"Consulta marcada com {profissionalNome} em {horario.Inicio:dd/MM} às {horario.Inicio:HH:mm}.", ct);

        conversa.TransicionarPara(EstadoConversa.Concluida);
    }

    private static List<T> Desserializar<T>(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<T>>(json) ?? [];

    private static int? InterpretarEscolha(MensagemRecebida mensagem, int total)
    {
        if (total == 0) return null;

        var bruto = mensagem.BotaoPayload ?? mensagem.Texto ?? string.Empty;
        if (!int.TryParse(bruto.Trim(), out var n)) return null;
        return n >= 1 && n <= total ? n - 1 : null;
    }

    private static bool TentarLerData(string entrada, out DateOnly data)
    {
        string[] formatos = ["dd/MM/yyyy", "d/M/yyyy", "ddMMyyyy", "yyyy-MM-dd"];

        return DateOnly.TryParseExact(
            entrada.Trim(), formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out data);
    }

    private static bool PedeAtendimentoHumano(MensagemRecebida mensagem)
    {
        var texto = (mensagem.Texto ?? string.Empty).ToLowerInvariant();
        return texto.Contains("atendente") || texto.Contains("humano") || texto.Contains("recepção");
    }

    private async Task Responder(string telefone, string texto, CancellationToken ct)
    {
        var resultado = await sleekFlow.EnviarTextoAsync(telefone, texto, ct);
        await logs.AdicionarAsync(new MensagemLog(
            telefone, TipoMensagem.EnviadaTexto, null, texto, null, resultado.Sucesso, resultado.Erro), ct);
    }
}
