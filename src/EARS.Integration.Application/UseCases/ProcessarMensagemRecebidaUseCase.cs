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

/// <summary>Opção de horário guardada entre turnos, já com o nome de quem atende.</summary>
public sealed record OpcaoHorario(
    string IdHorario,
    string CodigoProfissional,
    string ProfissionalNome,
    DateTimeOffset Inicio);

/// <summary>
/// Máquina de estados do atendimento conduzido pelo motor.
/// </summary>
/// <remarks>
/// Este caso de uso <b>não</b> é a porta de entrada do WhatsApp. O menu é do
/// Flow Builder; o motor só age depois que o fluxo entrega a conversa (ramos 1
/// e 3 do menu) e enquanto houver conversa ativa. Mensagem de quem não está em
/// atendimento é devolvida sem tratamento, para o fluxo seguir com ela.
///
/// A ordem da coleta é a que a clínica definiu: para quem e com que idade,
/// convênio, dia e turno, nome e data de nascimento. Profissional não é
/// perguntado — é preferência opcional, e os horários já saem nomeados.
/// O CPF só aparece quando nome e data de nascimento não desempatam.
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
    IOptions<AutomacaoOptions> automacao,
    IOptions<RegrasClinicaOptions> clinica,
    ILogger<ProcessarMensagemRecebidaUseCase> logger) : IDialogoAtendimento
{
    private readonly AutomacaoOptions _opcoes = automacao.Value;
    private readonly RegrasClinicaOptions _regras = clinica.Value;

    /// <summary>
    /// Devolve <c>false</c> quando a mensagem não é do motor — sem conversa
    /// ativa, quem responde é o Flow Builder.
    /// </summary>
    public async Task<bool> ExecutarAsync(MensagemRecebida mensagem, CancellationToken ct = default)
    {
        var conversa = await conversas.ObterAtivaPorTelefoneAsync(mensagem.Telefone, ct);
        if (conversa is null) return false;

        await logs.AdicionarAsync(new MensagemLog(
            mensagem.Telefone, TipoMensagem.Recebida, null,
            mensagem.BotaoPayload ?? mensagem.Texto ?? string.Empty,
            null, sucesso: true), ct);

        if (conversa.Expirada(_opcoes.ExpiracaoConversa))
        {
            logger.LogInformation(
                "Conversa de {Telefone} expirada antes desta mensagem; devolvendo ao menu.",
                mensagem.Telefone);

            await EncerrarAsync(conversa, EstadoConversa.Expirada, ct);
            await uow.SalvarAsync(ct);
            return false;
        }

        // Urgência e pedido de atendente têm precedência sobre qualquer estado.
        if (_regras.MencionaUrgencia(mensagem.Texto))
        {
            logger.LogWarning("Possível urgência relatada por {Telefone}; transferindo imediatamente.", mensagem.Telefone);
            await TransferirAsync(conversa,
                $"URGÊNCIA relatada pelo paciente: \"{mensagem.Texto}\"", ct, avisarPaciente: true);
            await uow.SalvarAsync(ct);
            return true;
        }

        if (_regras.PedeAtendente(mensagem.Texto))
        {
            await TransferirAsync(conversa, "Paciente pediu atendimento humano.", ct, avisarPaciente: true);
            await uow.SalvarAsync(ct);
            return true;
        }

        try
        {
            await AvancarAsync(conversa, mensagem, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao processar a conversa {ConversaId}.", conversa.Id);
            await TransferirAsync(conversa,
                "Não foi possível concluir o agendamento automaticamente.", ct, avisarPaciente: true);
        }

        await uow.SalvarAsync(ct);
        return true;
    }

    // -----------------------------------------------------------------------
    // Abertura
    // -----------------------------------------------------------------------

    public async Task AbrirAsync(Conversa conversa, CancellationToken ct = default)
    {
        if (conversa.Motivo == MotivoAtendimento.AgendarConsulta)
        {
            await Responder(conversa,
                "Vamos marcar sua consulta. A consulta é para você ou para outra pessoa?", ct);
            conversa.TransicionarPara(EstadoConversa.AguardandoParaQuem);
            return;
        }

        await Responder(conversa, "Certo. É sobre uma consulta ou um exame?", ct);
        conversa.TransicionarPara(EstadoConversa.AguardandoTipoRemarcacao);
    }

    // -----------------------------------------------------------------------
    // Máquina de estados
    // -----------------------------------------------------------------------

    private async Task AvancarAsync(Conversa conversa, MensagemRecebida mensagem, CancellationToken ct)
    {
        var texto = (mensagem.Texto ?? mensagem.BotaoPayload ?? string.Empty).Trim();

        switch (conversa.Estado)
        {
            case EstadoConversa.Inicio:
                await AbrirAsync(conversa, ct);
                break;

            case EstadoConversa.AguardandoParaQuem:
                conversa.RegistrarParaQuem(ParaOProprioContato(texto));
                await Responder(conversa,
                    conversa.PacienteEhOContato == true
                        ? "Qual a sua idade?"
                        : "Qual a idade do paciente?", ct);
                conversa.TransicionarPara(EstadoConversa.AguardandoIdade);
                break;

            case EstadoConversa.AguardandoIdade:
                if (!TentarLerIdade(texto, out var idade))
                {
                    await Responder(conversa, "Pode me dizer a idade em números? Por exemplo: 34.", ct);
                    break;
                }
                conversa.RegistrarIdade(idade);
                await Responder(conversa,
                    "Você tem convênio? Se tiver, me diga qual. Se preferir particular, é só dizer.", ct);
                conversa.TransicionarPara(EstadoConversa.AguardandoConvenio);
                break;

            case EstadoConversa.AguardandoConvenio:
                await TratarConvenioAsync(conversa, texto, ct);
                break;

            case EstadoConversa.AguardandoDiaTurno:
                conversa.RegistrarPreferencia(texto);
                await Responder(conversa,
                    conversa.PacienteEhOContato == true
                        ? "Qual o seu nome completo?"
                        : "Qual o nome completo do paciente?", ct);
                conversa.TransicionarPara(EstadoConversa.AguardandoNomePaciente);
                break;

            case EstadoConversa.AguardandoNomePaciente:
                if (texto.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
                {
                    await Responder(conversa, "Preciso do nome completo, por favor.", ct);
                    break;
                }
                conversa.RegistrarNome(texto);
                await Responder(conversa, "E a data de nascimento? (dd/mm/aaaa)", ct);
                conversa.TransicionarPara(EstadoConversa.AguardandoDataNascimento);
                break;

            case EstadoConversa.AguardandoDataNascimento:
                if (!TentarLerData(texto, out var nascimento))
                {
                    await Responder(conversa, "Não consegui ler a data. Pode escrever assim: 05/02/1990?", ct);
                    break;
                }
                conversa.RegistrarDataNascimento(nascimento);
                await ResolverPacienteEOfertarAsync(conversa, ct);
                break;

            case EstadoConversa.AguardandoCpf:
                if (!Cpf.TryCreate(texto, out var cpf) || cpf is null)
                {
                    await Responder(conversa, "Esse CPF não parece válido. Pode conferir?", ct);
                    break;
                }
                conversa.RegistrarCpf(cpf.Value);
                await ResolverPorCpfEOfertarAsync(conversa, cpf, ct);
                break;

            case EstadoConversa.AguardandoEscolhaHorario:
                await ConcluirAgendamentoAsync(conversa, mensagem, ct);
                break;

            case EstadoConversa.AguardandoTipoRemarcacao:
                await TratarTipoRemarcacaoAsync(conversa, texto, ct);
                break;

            case EstadoConversa.AguardandoConfirmacaoCancelamento:
                await TratarConfirmacaoCancelamentoAsync(conversa, texto, ct);
                break;

            default:
                await TransferirAsync(conversa, "Conversa em estado final.", ct, avisarPaciente: true);
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Convênio
    // -----------------------------------------------------------------------

    private async Task TratarConvenioAsync(Conversa conversa, string texto, CancellationToken ct)
    {
        // Teleconsulta: o bot não confirma nem nega, a equipe avalia.
        if (_regras.MencionaTeleconsulta(texto))
            await Responder(conversa, _regras.RespostaTeleconsulta, ct);

        // IPE e Unimed Unifácil: triagem humana. Sem ofertar horário e sem
        // citar médicas — a regra é da clínica e vale antes de qualquer oferta.
        if (_regras.ExigeTriagemHumana(texto))
        {
            conversa.RegistrarConvenio(texto);
            await TransferirAsync(conversa,
                $"Convênio {texto}: triagem pela equipe, conforme a regra da clínica.", ct, avisarPaciente: true);
            return;
        }

        var particular = EhParticular(texto);
        var convenio = particular ? "Particular" : texto;

        if (!particular && !_regras.ConvenioAceito(texto))
        {
            await Responder(conversa, _regras.RespostaConvenioForaDaLista, ct);
            convenio = "Particular";
        }

        conversa.RegistrarConvenio(convenio);

        await Responder(conversa,
            "Que dia e turno ficam melhores para você? Por exemplo: terça de manhã.", ct);
        conversa.TransicionarPara(EstadoConversa.AguardandoDiaTurno);
    }

    // -----------------------------------------------------------------------
    // Identificação do paciente
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolve o paciente por nome e data de nascimento, que é a ordem da
    /// clínica. O CPF só entra quando a busca volta ambígua — pedi-lo sempre
    /// acrescentaria um passo a quem já é cadastrado.
    /// </summary>
    private async Task ResolverPacienteEOfertarAsync(Conversa conversa, CancellationToken ct)
    {
        var nome = conversa.NomeInformado!;
        var nascimento = conversa.DataNascimentoInformada!.Value;

        var locais = await pacientes.ListarPorNomeEDataNascimentoAsync(nome, nascimento, ct);

        if (locais.Count == 1)
        {
            conversa.VincularPaciente(locais[0].Id);
            await OfertarHorariosAsync(conversa, ct);
            return;
        }

        if (locais.Count > 1)
        {
            await PedirCpfAsync(conversa, ct);
            return;
        }

        var remotos = (await pacientesClient.ListarAsync(new FiltroPacientes(Nome: nome, Pagina: 1), ct))
            .Where(p => p.DataNascimento == nascimento)
            .ToList();

        if (remotos.Count > 1)
        {
            logger.LogInformation(
                "{Total} cadastros na Amplimed com o mesmo nome e data de nascimento; pedindo CPF.", remotos.Count);
            await PedirCpfAsync(conversa, ct);
            return;
        }

        var remoto = remotos.FirstOrDefault();
        var paciente = await CriarPacienteLocalAsync(conversa, nascimento, remoto, ct);

        if (remoto is not null && string.IsNullOrWhiteSpace(remoto.CodReferencia)
            && !string.IsNullOrWhiteSpace(paciente.Cpf))
        {
            if (await pacientesClient.VincularCodReferenciaAsync(paciente.Cpf, paciente.CodReferencia, ct))
                paciente.MarcarCodReferenciaVinculado();
        }

        await OfertarHorariosAsync(conversa, ct);
    }

    private async Task PedirCpfAsync(Conversa conversa, CancellationToken ct)
    {
        await Responder(conversa,
            "Encontrei mais de um cadastro com esse nome. Pode me informar o CPF do paciente?", ct);
        conversa.TransicionarPara(EstadoConversa.AguardandoCpf);
    }

    private async Task ResolverPorCpfEOfertarAsync(Conversa conversa, Cpf cpf, CancellationToken ct)
    {
        var local = await pacientes.ObterPorCpfAsync(cpf.Value, ct);

        if (local is not null)
        {
            conversa.VincularPaciente(local.Id);
            await OfertarHorariosAsync(conversa, ct);
            return;
        }

        var remoto = await pacientesClient.BuscarPorCpfAsync(cpf.Value, ct);
        var paciente = await CriarPacienteLocalAsync(
            conversa, conversa.DataNascimentoInformada ?? remoto?.DataNascimento, remoto, ct, cpf);

        if (remoto is not null && string.IsNullOrWhiteSpace(remoto.CodReferencia))
        {
            if (await pacientesClient.VincularCodReferenciaAsync(cpf.Value, paciente.CodReferencia, ct))
                paciente.MarcarCodReferenciaVinculado();
        }

        await OfertarHorariosAsync(conversa, ct);
    }

    /// <summary>
    /// Cria o espelho local. Não há POST /pattient aqui: o
    /// cadastrar-agendamento já faz o upsert do paciente na Amplimed, e
    /// cadastrar antes seria uma chamada a mais e outro caminho para gerar
    /// duplicata.
    /// </summary>
    private async Task<Paciente> CriarPacienteLocalAsync(
        Conversa conversa,
        DateOnly? nascimento,
        PacienteAmplimed? remoto,
        CancellationToken ct,
        Cpf? cpf = null)
    {
        var cpfFinal = cpf;

        if (cpfFinal is null && !string.IsNullOrWhiteSpace(remoto?.Cpf)
            && Cpf.TryCreate(remoto.Cpf, out var doRemoto))
        {
            cpfFinal = doRemoto;
        }

        var paciente = new Paciente(
            remoto?.Nome ?? conversa.NomeInformado ?? "Paciente",
            Telefone.Create(conversa.Telefone),
            nascimento ?? remoto?.DataNascimento,
            cpfFinal);

        await pacientes.AdicionarAsync(paciente, ct);
        await uow.SalvarAsync(ct);

        conversa.VincularPaciente(paciente.Id);
        return paciente;
    }

    // -----------------------------------------------------------------------
    // Oferta de horários
    // -----------------------------------------------------------------------

    /// <summary>
    /// Monta a oferta já filtrada pela grade etária da clínica: para criança,
    /// só os profissionais que atendem criança. A preferência de dia e turno
    /// ordena, mas não elimina — ficar sem opção nenhuma é pior do que
    /// oferecer um horário fora da preferência.
    /// </summary>
    private async Task OfertarHorariosAsync(Conversa conversa, CancellationToken ct)
    {
        var hoje = relogio.HojeLocal;
        var ate = hoje.AddDays(_opcoes.DiasHorizonteOferta);

        var profissionais = await agendamentoClient.ListarProfissionaisAsync(
            new FiltroProfissionais(hoje, ate), ct);

        var elegiveis = profissionais
            .Where(p => _regras.AtendeIdade(p.Nome, conversa.IdadePaciente))
            .ToList();

        if (elegiveis.Count == 0)
        {
            await TransferirAsync(conversa,
                $"Sem profissional elegível para paciente de {conversa.IdadePaciente} anos.",
                ct, avisarPaciente: true);
            return;
        }

        var candidatos = new List<OpcaoHorario>();

        foreach (var profissional in elegiveis)
        {
            ct.ThrowIfCancellationRequested();

            var horarios = await agendamentoClient.ListarHorariosDisponiveisAsync(
                profissional.CodigoProfissional, hoje, ate, ct);

            candidatos.AddRange(horarios
                .Where(h => h.Inicio > relogio.AgoraLocal)
                .Select(h => new OpcaoHorario(
                    h.IdHorario, h.CodigoProfissional, profissional.Nome, h.Inicio)));
        }

        if (candidatos.Count == 0)
        {
            await TransferirAsync(conversa, "Nenhum horário livre no período consultado.",
                ct, avisarPaciente: true);
            return;
        }

        var oferta = candidatos
            .OrderByDescending(h => CasaComAPreferencia(h.Inicio, conversa.PreferenciaDiaTurno))
            .ThenBy(h => h.Inicio)
            .Take(_opcoes.MaxHorariosOfertados)
            .ToList();

        conversa.RegistrarHorariosOfertados(JsonSerializer.Serialize(oferta));

        var linhas = oferta.Select((h, i) =>
            $"{i + 1}. {h.ProfissionalNome} — {h.Inicio:dd/MM} às {h.Inicio:HH:mm}");

        await Responder(conversa,
            "Estes horários estão livres:\n" + string.Join('\n', linhas) +
            "\n\nResponda com o número da opção.", ct);

        conversa.TransicionarPara(EstadoConversa.AguardandoEscolhaHorario);
    }

    /// <summary>
    /// Casamento grosseiro da preferência: turno e dia da semana escritos em
    /// texto livre. Serve para ordenar, não para decidir.
    /// </summary>
    private static int CasaComAPreferencia(DateTimeOffset inicio, string? preferencia)
    {
        if (string.IsNullOrWhiteSpace(preferencia)) return 0;

        var alvo = RegrasClinicaOptions.Normalizar(preferencia);
        var pontos = 0;

        var manha = inicio.Hour < 12;
        if (manha && alvo.Contains("manha", StringComparison.Ordinal)) pontos++;
        if (!manha && (alvo.Contains("tarde", StringComparison.Ordinal) || alvo.Contains("noite", StringComparison.Ordinal))) pontos++;

        string[] dias = ["domingo", "segunda", "terca", "quarta", "quinta", "sexta", "sabado"];
        if (alvo.Contains(dias[(int)inicio.DayOfWeek], StringComparison.Ordinal)) pontos++;

        return pontos;
    }

    // -----------------------------------------------------------------------
    // Conclusão
    // -----------------------------------------------------------------------

    private async Task ConcluirAgendamentoAsync(
        Conversa conversa, MensagemRecebida mensagem, CancellationToken ct)
    {
        var oferta = Desserializar<OpcaoHorario>(conversa.HorariosOfertadosJson);
        var escolha = InterpretarEscolha(mensagem, oferta.Count);

        if (escolha is null)
        {
            await Responder(conversa, "Não entendi a escolha. Responda com o número da opção.", ct);
            return;
        }

        var horario = oferta[escolha.Value];

        var paciente = conversa.PacienteId is { } id ? await pacientes.ObterPorIdAsync(id, ct) : null;
        if (paciente is null)
            throw new InvalidOperationException("Paciente não resolvido na conversa.");

        // Revalida pelo idHorario — o token do slot —, não por data e hora:
        // entre a oferta e a resposta o horário pode ter sido ocupado.
        var dia = DateOnly.FromDateTime(horario.Inicio.Date);
        var aindaLivres = await agendamentoClient.ListarHorariosDisponiveisAsync(
            horario.CodigoProfissional, dia, dia, ct);

        if (aindaLivres.All(h => h.IdHorario != horario.IdHorario))
        {
            await Responder(conversa, "Esse horário acabou de ser ocupado. Vou buscar outras opções.", ct);
            await OfertarHorariosAsync(conversa, ct);
            return;
        }

        var observacoes = MontarObservacoes(conversa);

        var criado = await agendamentoClient.CadastrarAsync(
            new CadastrarAgendamentoRequest(
                IdUnico: paciente.CodReferencia,
                IdHorario: horario.IdHorario,
                Cpf: paciente.Cpf,
                Nome: paciente.Nome,
                DataNascimento: paciente.DataNascimento,
                Celular: paciente.Telefone,
                Observacoes: observacoes),
            ct);

        await agendamentos.AdicionarAsync(Agendamento.DaIntegracao(
            criado.IdIntegracao,
            horario.IdHorario,
            paciente.Id,
            horario.CodigoProfissional,
            horario.ProfissionalNome,
            horario.Inicio), ct);

        await Responder(conversa,
            $"Consulta marcada com {horario.ProfissionalNome} em {horario.Inicio:dd/MM} às {horario.Inicio:HH:mm}.", ct);

        await EncerrarAsync(conversa, EstadoConversa.Concluida, ct);
    }

    private static string MontarObservacoes(Conversa conversa)
    {
        var partes = new List<string> { "Agendado pela automação de WhatsApp." };

        if (!string.IsNullOrWhiteSpace(conversa.Convenio)) partes.Add($"Convênio informado: {conversa.Convenio}.");
        if (conversa.IdadePaciente is { } idade) partes.Add($"Idade informada: {idade}.");
        if (conversa.PacienteEhOContato == false) partes.Add("Agendado por terceiro (contato não é o paciente).");

        return string.Join(' ', partes);
    }

    // -----------------------------------------------------------------------
    // Remarcação e cancelamento (ramo 3)
    // -----------------------------------------------------------------------

    private async Task TratarTipoRemarcacaoAsync(Conversa conversa, string texto, CancellationToken ct)
    {
        var alvo = RegrasClinicaOptions.Normalizar(texto);

        // Exame continua com a Recepção: o motor só trata consulta.
        if (alvo.Contains("exame", StringComparison.Ordinal))
        {
            await TransferirAsync(conversa, "Remarcação ou cancelamento de exame.", ct, avisarPaciente: true);
            return;
        }

        if (!alvo.Contains("consulta", StringComparison.Ordinal))
        {
            await Responder(conversa, "É sobre uma consulta ou um exame?", ct);
            return;
        }

        var agendamento = await agendamentos.ObterAtivoMaisRecentePorTelefoneAsync(conversa.Telefone, ct);

        if (agendamento is null)
        {
            await TransferirAsync(conversa,
                "Paciente pediu remarcação ou cancelamento, mas não há consulta ativa vinculada a este número.",
                ct, avisarPaciente: true);
            return;
        }

        conversa.RegistrarAgendamentoAlvo(agendamento.Id);

        var descricao = $"{agendamento.ProfissionalNome} em {agendamento.InicioEm:dd/MM} às {agendamento.InicioEm:HH:mm}";

        if (conversa.Motivo == MotivoAtendimento.Cancelar)
        {
            await Responder(conversa,
                $"Encontrei sua consulta com {descricao}. Confirma o cancelamento?", ct);
            conversa.TransicionarPara(EstadoConversa.AguardandoConfirmacaoCancelamento);
            return;
        }

        await Responder(conversa, $"Encontrei sua consulta com {descricao}. Vou buscar outros horários.", ct);

        if (agendamento.PacienteId != Guid.Empty) conversa.VincularPaciente(agendamento.PacienteId);
        if (agendamento.Paciente is not null)
        {
            conversa.RegistrarNome(agendamento.Paciente.Nome);
            if (agendamento.Paciente.DataNascimento is { } dn) conversa.RegistrarDataNascimento(dn);
        }

        await LiberarAgendamentoAsync(agendamento, "Remarcação solicitada pelo paciente via WhatsApp", ct);
        await OfertarHorariosAsync(conversa, ct);
    }

    private async Task TratarConfirmacaoCancelamentoAsync(Conversa conversa, string texto, CancellationToken ct)
    {
        if (!EhSim(texto))
        {
            await Responder(conversa, "Tudo bem, sua consulta continua marcada.", ct);
            await EncerrarAsync(conversa, EstadoConversa.Concluida, ct);
            return;
        }

        var agendamento = conversa.AgendamentoAlvoId is { } id
            ? await agendamentos.ObterPorIdAsync(id, ct)
            : null;

        if (agendamento is null)
        {
            await TransferirAsync(conversa, "Agendamento alvo do cancelamento não encontrado.", ct, avisarPaciente: true);
            return;
        }

        agendamento.Cancelar();
        await LiberarAgendamentoAsync(agendamento, "Cancelado pelo paciente via WhatsApp", ct);

        // A clínica não cobra taxa. A resposta não menciona multa nem para
        // negar: dizer "sem multa" planta a ideia de que poderia haver.
        await Responder(conversa, _regras.RespostaCancelamento, ct);
        await EncerrarAsync(conversa, EstadoConversa.Concluida, ct);
    }

    /// <summary>
    /// Devolve o horário à escala. Agendamento criado pela integração sai pelo
    /// cancelar-agendamento; o que veio da agenda da clínica só aceita
    /// alteração de status (ADR 0003).
    /// </summary>
    private async Task LiberarAgendamentoAsync(Agendamento agendamento, string motivo, CancellationToken ct)
    {
        if (agendamento.PodeCancelarNaAmplimed())
        {
            if (await agendamentoClient.CancelarAsync(agendamento.IdIntegracao!, motivo, ct)) return;
            logger.LogWarning("cancelar-agendamento não concluiu para {Id}; tentando status.", agendamento.IdIntegracao);
        }

        if (agendamento.PodeAlterarStatusNaAmplimed())
        {
            await agendamentoClient.AlterarStatusAsync(
                agendamento.Usuclin!, agendamento.IdAgendamentoAmplimed!.Value, StatusAmplimed.Cancelado, ct);
            return;
        }

        logger.LogWarning(
            "Agendamento {Id} sem identificador utilizável na Amplimed; cancelado apenas no espelho local.",
            agendamento.Id);
    }

    // -----------------------------------------------------------------------
    // Saídas
    // -----------------------------------------------------------------------

    /// <summary>
    /// Toda saída passa por aqui, e toda saída tira a etiqueta. É o ponto
    /// único que impede o paciente de ficar sem menu.
    /// </summary>
    private async Task EncerrarAsync(Conversa conversa, EstadoConversa estado, CancellationToken ct)
    {
        conversa.TransicionarPara(estado);

        if (!conversa.EtiquetaAplicada) return;

        if (await sleekFlow.LimparAgendamentoEmCursoAsync(conversa.Telefone, ct))
        {
            conversa.MarcarEtiquetaRemovida();
            return;
        }

        logger.LogError(
            "Não foi possível remover a etiqueta de agendamento em curso de {Telefone}. " +
            "O paciente ficará sem o menu até que ela caia.",
            conversa.Telefone);
    }

    private async Task TransferirAsync(
        Conversa conversa, string contexto, CancellationToken ct, bool avisarPaciente)
    {
        if (avisarPaciente)
            await Responder(conversa, _regras.FraseTransferencia, ct);

        // A transferência já troca as etiquetas: aplica a de encaminhamento e
        // tira a de agendamento em curso.
        await sleekFlow.TransferirParaAtendimentoAsync(conversa.Telefone, contexto, ct);

        conversa.MarcarEtiquetaRemovida();
        conversa.TransicionarPara(EstadoConversa.TransferidaParaHumano);
    }

    // -----------------------------------------------------------------------
    // Utilitários
    // -----------------------------------------------------------------------

    private async Task Responder(Conversa conversa, string texto, CancellationToken ct)
    {
        var resultado = await sleekFlow.EnviarTextoAsync(conversa.Telefone, texto, ct);

        await logs.AdicionarAsync(new MensagemLog(
            conversa.Telefone, TipoMensagem.EnviadaTexto, null, texto, null,
            resultado.Sucesso, resultado.Erro), ct);
    }

    private static List<T> Desserializar<T>(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<T>>(json) ?? [];

    private static int? InterpretarEscolha(MensagemRecebida mensagem, int total)
    {
        if (total == 0) return null;

        var bruto = mensagem.BotaoPayload ?? mensagem.Texto ?? string.Empty;
        if (!int.TryParse(bruto.Trim(), out var n)) return null;
        return n >= 1 && n <= total ? n - 1 : null;
    }

    private static bool ParaOProprioContato(string texto)
    {
        var alvo = RegrasClinicaOptions.Normalizar(texto);

        // "para outra pessoa", "para meu filho" etc. pesam mais que "mim".
        if (alvo.Contains("outra", StringComparison.Ordinal)
            || alvo.Contains("filh", StringComparison.Ordinal)
            || alvo.Contains("minha mae", StringComparison.Ordinal)
            || alvo.Contains("meu pai", StringComparison.Ordinal)
            || alvo.Contains("terceiro", StringComparison.Ordinal))
        {
            return false;
        }

        return alvo.Contains("mim", StringComparison.Ordinal)
               || alvo.Contains("eu", StringComparison.Ordinal)
               || alvo.Contains("minha", StringComparison.Ordinal)
               || alvo.Contains("meu", StringComparison.Ordinal)
               || alvo.Contains("propri", StringComparison.Ordinal);
    }

    private static bool EhParticular(string texto)
    {
        var alvo = RegrasClinicaOptions.Normalizar(texto);
        return alvo.Contains("particular", StringComparison.Ordinal)
               || alvo.Contains("sem convenio", StringComparison.Ordinal)
               || alvo.Contains("nao tenho", StringComparison.Ordinal)
               || alvo is "nao" or "n";
    }

    private static bool EhSim(string texto)
    {
        var alvo = RegrasClinicaOptions.Normalizar(texto);
        return alvo is "sim" or "s" or "confirmo" or "confirmar" or "isso" or "pode" or "ok";
    }

    private static bool TentarLerIdade(string entrada, out int idade)
    {
        var digitos = new string(entrada.Where(char.IsDigit).ToArray());
        return int.TryParse(digitos, out idade) && idade is >= 0 and <= 120;
    }

    private static bool TentarLerData(string entrada, out DateOnly data)
    {
        string[] formatos = ["dd/MM/yyyy", "d/M/yyyy", "ddMMyyyy", "yyyy-MM-dd"];

        return DateOnly.TryParseExact(
            entrada.Trim(), formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out data);
    }
}
