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
/// Módulo 1: máquina de estados do agendamento pela conversa.
/// Ponto de entrada de toda mensagem recebida via webhook do SleekFlow.
/// </summary>
public sealed class ProcessarMensagemRecebidaUseCase(
    IConversaRepository conversas,
    IPacienteRepository pacientes,
    IAgendamentoRepository agendamentos,
    IAmplimedPacientesClient pacientesClient,
    IAmplimedAgendamentoClient agendamentoClient,
    IAmplimedDadosClient dadosClient,
    ISleekFlowClient sleekFlow,
    IMensagemLogRepository logs,
    IUnitOfWork uow,
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
                await ResolverPacienteAsync(conversa, mensagem, cpf, ct);
                await OfertarProfissionaisAsync(mensagem.Telefone, ct);
                conversa.TransicionarPara(EstadoConversa.AguardandoProfissional);
                break;

            case EstadoConversa.AguardandoProfissional:
                var profissionalId = mensagem.BotaoPayload ?? texto;
                conversa.RegistrarProfissional(profissionalId);
                await OfertarHorariosAsync(conversa, mensagem.Telefone, profissionalId, ct);
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
    /// Deduplicação: busca por CPF e telefone em conjunto. Só cria cadastro
    /// quando não há correspondência alguma.
    /// </summary>
    private async Task ResolverPacienteAsync(Conversa conversa, MensagemRecebida mensagem, Cpf cpf, CancellationToken ct)
    {
        var local = await pacientes.ObterPorCpfAsync(cpf.Value, ct);
        if (local is not null)
        {
            conversa.VincularPaciente(local.Id);
            return;
        }

        var remoto = await pacientesClient.BuscarPorCpfETelefoneAsync(cpf.Value, mensagem.Telefone, ct)
                     ?? await pacientesClient.BuscarPorCpfAsync(cpf.Value, ct);

        remoto ??= await pacientesClient.CriarAsync(
            new CriarPacienteRequest(
                conversa.NomeInformado ?? "Paciente",
                cpf.Value,
                mensagem.Telefone,
                DataNascimento: null,
                Observacoes: "Cadastro criado pela automação de WhatsApp."),
            ct);

        var paciente = new Paciente(
            remoto.Id,
            remoto.Nome,
            cpf,
            Telefone.Create(mensagem.Telefone));

        await pacientes.AdicionarAsync(paciente, ct);
        await uow.SalvarAsync(ct);
        conversa.VincularPaciente(paciente.Id);
    }

    private async Task OfertarProfissionaisAsync(string telefone, CancellationToken ct)
    {
        var profissionais = await dadosClient.ListarProfissionaisAsync(ct);
        var linhas = profissionais.Select((p, i) => $"{i + 1}. {p.Nome}");
        await Responder(telefone, "Com qual profissional você quer marcar?\n" + string.Join('\n', linhas), ct);
    }

    private async Task OfertarHorariosAsync(Conversa conversa, string telefone, string profissionalId, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var horarios = await agendamentoClient.ListarHorariosDisponiveisAsync(
            profissionalId, hoje, hoje.AddDays(30), ct);

        var oferta = horarios.Take(_opcoes.MaxHorariosOfertados).ToList();

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

    private async Task ConcluirAgendamentoAsync(Conversa conversa, MensagemRecebida mensagem, CancellationToken ct)
    {
        var oferta = JsonSerializer.Deserialize<List<HorarioDisponivel>>(conversa.HorariosOfertadosJson ?? "[]")
                     ?? new List<HorarioDisponivel>();

        var escolha = InterpretarEscolha(mensagem, oferta.Count);
        if (escolha is null)
        {
            await Responder(mensagem.Telefone, "Não entendi a escolha. Responda com o número da opção.", ct);
            return;
        }

        var horario = oferta[escolha.Value];
        var paciente = conversa.PacienteId is null
            ? null
            : await pacientes.ObterPorAmplimedIdAsync(conversa.CpfInformado!, ct);

        var pacienteAmplimedId = paciente?.AmplimedId
                                 ?? throw new InvalidOperationException("Paciente não resolvido na conversa.");

        // Revalida o horário imediatamente antes de gravar, evitando corrida.
        var aindaLivre = await agendamentoClient.ListarHorariosDisponiveisAsync(
            horario.ProfissionalId,
            DateOnly.FromDateTime(horario.Inicio.Date),
            DateOnly.FromDateTime(horario.Inicio.Date), ct);

        if (aindaLivre.All(h => h.Inicio != horario.Inicio))
        {
            await Responder(mensagem.Telefone,
                "Esse horário acabou de ser ocupado. Vou buscar outras opções.", ct);
            await OfertarHorariosAsync(conversa, mensagem.Telefone, horario.ProfissionalId, ct);
            return;
        }

        var criado = await agendamentoClient.CriarAsync(
            new CriarAgendamentoRequest(
                pacienteAmplimedId,
                horario.ProfissionalId,
                horario.Inicio,
                "Agendado pela automação de WhatsApp."),
            ct);

        await agendamentos.AdicionarAsync(new Agendamento(
            criado.Id,
            paciente.Id,
            horario.ProfissionalId,
            horario.ProfissionalNome,
            horario.Inicio,
            OrigemAgendamento.WhatsApp), ct);

        await Responder(mensagem.Telefone,
            $"Consulta marcada com {horario.ProfissionalNome} em {horario.Inicio:dd/MM} às {horario.Inicio:HH:mm}.", ct);

        conversa.TransicionarPara(EstadoConversa.Concluida);
    }

    private static int? InterpretarEscolha(MensagemRecebida mensagem, int total)
    {
        var bruto = mensagem.BotaoPayload ?? mensagem.Texto ?? string.Empty;
        if (!int.TryParse(bruto.Trim(), out var n)) return null;
        return n >= 1 && n <= total ? n - 1 : null;
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
