using System.Globalization;
using System.Text.Json;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.Options;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Application.UseCases;

/// <summary>
/// Módulo 2, passos 10 e 11: interpreta a resposta do paciente ao template de
/// véspera, atualiza o agendamento e devolve a mensagem adequada.
/// Nunca assume confirmação por inferência frágil — na dúvida, escala para humano.
/// </summary>
/// <remarks>
/// Escrever de volta na Amplimed usa dois endpoints distintos, e qual deles vale
/// depende da origem do agendamento (ver ADR 0003):
///
/// <list type="bullet">
/// <item><c>alterar-status-agendamento</c> exige o par (usuclin, idAgendamento)
/// da listagem da agenda. É o caminho do "Confirmado", e serve a qualquer
/// agendamento, inclusive os marcados no sistema da clínica.</item>
/// <item><c>cancelar-agendamento</c> aceita apenas o UUID da integração e só
/// funciona para o que foi marcado por aqui. A vantagem é que libera o horário
/// de volta para a escala; por isso é preferido no cancelamento e na
/// remarcação, com a alteração de status como alternativa.</item>
/// </list>
/// </remarks>
public sealed class ProcessarRespostaConfirmacaoUseCase(
    IAgendamentoRepository agendamentos,
    IConversaRepository conversas,
    IRelogio relogio,
    IAmplimedAgendamentoClient agendamentoClient,
    ISleekFlowClient sleekFlow,
    IMensagemLogRepository logs,
    IOutboxRepository outbox,
    CancelarAgendamentoUseCase cancelamento,
    IOptions<RegrasClinicaOptions> clinica,
    IUnitOfWork uow,
    ILogger<ProcessarRespostaConfirmacaoUseCase> logger)
{
    private readonly RegrasClinicaOptions _regras = clinica.Value;

    public const string TipoOutboxStatus = "amplimed.alterar-status-agendamento";
    public const string TipoOutboxCancelamento = "amplimed.cancelar-agendamento";

    public async Task<bool> ExecutarAsync(MensagemRecebida mensagem, CancellationToken ct = default)
    {
        var agendamento = await agendamentos
            .ObterAtivoMaisRecentePorTelefoneAsync(mensagem.Telefone, ct);

        if (agendamento is null || agendamento.ConfirmacaoEnviadaEm is null)
        {
            // Ninguém mandou o template, mas o paciente escreveu "confirmar" (por
            // exemplo, respondendo a um lembrete que a clínica mandou por fora).
            // Só o confirmar explícito vale: "sim" e "1" são ambíguos fora do template.
            if (!ConfirmacaoExplicita(mensagem)) return false;

            // Em meio a um agendamento pelo bot, "confirmar" é a resposta ao
            // resumo do horário, e quem trata isso é o diálogo.
            if (await conversas.ObterAtivaPorTelefoneAsync(mensagem.Telefone, ct) is not null)
                return false;

            var ativos = await agendamentos.ListarAtivosFuturosPorTelefoneAsync(
                mensagem.Telefone, relogio.AgoraUtc, 2, ct);

            if (ativos.Count != 1)
            {
                await EncaminharConfirmacaoSemAlvoAsync(mensagem, ativos.Count, ct);
                return true;
            }

            agendamento = ativos[0];
        }

        RespostaConfirmacao resposta;

        if (agendamento.ConfirmacaoEnviadaEm is not null)
        {
            // Resposta ao nosso template: o botão não age no primeiro toque. Primeiro
            // vem "Você deseja ...?", e só o Sim executa; o Não volta à pergunta inicial.
            var decidida = await DecidirComSegundaConfirmacaoAsync(agendamento, mensagem, ct);
            if (decidida is null) return true;

            resposta = decidida.Value;
        }
        else
        {
            resposta = Interpretar(mensagem);
        }

        switch (resposta)
        {
            case RespostaConfirmacao.Confirmar:
                agendamento.Confirmar();
                await PropagarStatusAsync(agendamento, StatusAmplimed.Confirmado, ct);
                await sleekFlow.EnviarTextoAsync(mensagem.Telefone,
                    "Consulta confirmada. Até breve!", ct);
                await LiberarEtiquetaAsync(agendamento, mensagem.Telefone, ct);
                break;

            case RespostaConfirmacao.Cancelar:
                // Quem marca o status é o CancelarAgendamentoUseCase. Marcar
                // aqui antes faria ele ver o agendamento já cancelado e sair
                // sem avisar a Amplimed.
                await LiberarOuCancelarAsync(agendamento, "Cancelado pelo paciente via WhatsApp", ct);
                await sleekFlow.EnviarTextoAsync(mensagem.Telefone,
                    "Consulta cancelada. Se quiser remarcar, é só escrever por aqui.", ct);
                await sleekFlow.TransferirParaAtendimentoAsync(mensagem.Telefone,
                    $"CANCELAMENTO — {agendamento.ProfissionalNome} em {Hora(agendamento)}. Horário liberado.", ct);
                // A transferência já troca as etiquetas: tira a de agendamento em curso.
                agendamento.MarcarEtiquetaConfirmacaoRemovida();
                break;

            case RespostaConfirmacao.Remarcar:
                // Quem pede para remarcar ainda não escolheu outro horário. Liberar
                // o atual agora deixaria o paciente sem consulta e sem resposta,
                // porque nada aqui conduz a escolha do novo. Fica reservado até a
                // recepção remarcar; o status local já evita o lembrete da manhã.
                agendamento.MarcarRemarcado();
                await sleekFlow.EnviarTextoAsync(mensagem.Telefone,
                    "Certo! Vou encaminhar para a nossa equipe, que combina o novo horário com você por aqui.", ct);
                await sleekFlow.TransferirParaAtendimentoAsync(mensagem.Telefone,
                    $"REMARCAÇÃO — {agendamento.ProfissionalNome} em {Hora(agendamento)}. " +
                    "O horário original segue reservado na agenda até a recepção remarcar.", ct);
                agendamento.MarcarEtiquetaConfirmacaoRemovida();
                break;

            default:
                logger.LogInformation(
                    "Resposta não compreendida para o agendamento {AgendamentoId}; escalando para atendimento humano.",
                    agendamento.Id);
                await sleekFlow.TransferirParaAtendimentoAsync(mensagem.Telefone,
                    $"Resposta não compreendida sobre a consulta de {Hora(agendamento)}: \"{mensagem.Texto}\"", ct);
                agendamento.MarcarEtiquetaConfirmacaoRemovida();
                break;
        }

        await RegistrarRecebidaAsync(agendamento, mensagem, ct);

        await uow.SalvarAsync(ct);
        return true;
    }

    private Task RegistrarRecebidaAsync(Agendamento agendamento, MensagemRecebida mensagem, CancellationToken ct) =>
        logs.AdicionarAsync(new MensagemLog(
            mensagem.Telefone, TipoMensagem.Recebida, null,
            mensagem.BotaoPayload ?? mensagem.Texto ?? string.Empty,
            agendamento.Id, sucesso: true), ct);

    // -----------------------------------------------------------------------
    // Segunda confirmação (failsafe contra clique errado)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Quanto tempo a pergunta "Você deseja ...?" espera pelo Sim. Depois disso, um
    /// "sim" solto não executa nada: o paciente é perguntado de novo.
    /// </summary>
    private static readonly TimeSpan ValidadeDaPergunta = TimeSpan.FromHours(6);

    /// <summary>
    /// Respostas seguidas que não entendemos antes de passar para a recepção. Abaixo
    /// disso o paciente continua no fluxo; sem limite, quem não consegue responder
    /// ficaria preso pedindo desculpa ao bot.
    /// </summary>
    public const int MaximoDeRespostasNaoEntendidas = 3;

    private const string DesculpaNaoEntendi = "Me desculpe, mas não entendi.";

    public const string BotaoSim = "acao_sim";
    public const string BotaoNao = "acao_nao";

    /// <summary>
    /// Devolve a ação a executar, ou <c>null</c> quando só houve pergunta: nada
    /// a fazer agora e a resposta ao paciente já foi enviada e registrada.
    /// </summary>
    private async Task<RespostaConfirmacao?> DecidirComSegundaConfirmacaoAsync(
        Agendamento agendamento, MensagemRecebida mensagem, CancellationToken ct)
    {
        var agora = relogio.AgoraUtc;
        var pendente = agendamento.AcaoPendenteVigente(agora, ValidadeDaPergunta);
        var simNao = InterpretarSimNao(mensagem);

        if (pendente is not null && simNao is true)
        {
            agendamento.LimparAcaoPendente();
            agendamento.ZerarRespostasNaoEntendidas();
            return AcaoParaResposta(pendente);
        }

        if (simNao is false || (pendente is null && EhBotaoDaSegundaConfirmacao(mensagem)))
        {
            // Não, ou o Sim de uma pergunta que já venceu: volta ao começo.
            agendamento.LimparAcaoPendente();
            agendamento.ZerarRespostasNaoEntendidas();
            await PerguntarOQueFazerAsync(agendamento, mensagem.Telefone, "Sem problemas!", ct);
            await RegistrarRecebidaAsync(agendamento, mensagem, ct);
            await uow.SalvarAsync(ct);
            return null;
        }

        var resposta = Interpretar(mensagem);

        if (resposta is RespostaConfirmacao.Confirmar or RespostaConfirmacao.Cancelar or RespostaConfirmacao.Remarcar)
        {
            agendamento.ZerarRespostasNaoEntendidas();
            agendamento.AguardarConfirmacaoDaAcao(NomeDaAcao(resposta), agora);
            await PerguntarSeTemCertezaAsync(agendamento, resposta, mensagem.Telefone, null, ct);
            await RegistrarRecebidaAsync(agendamento, mensagem, ct);
            await uow.SalvarAsync(ct);
            return null;
        }

        // Não entendemos. Urgência e pedido de atendente vão direto para a recepção; o
        // resto fica no fluxo: pede desculpa e repete a pergunta em que o paciente está.
        var querHumano = _regras.MencionaUrgencia(mensagem.Texto) || _regras.PedeAtendente(mensagem.Texto);

        if (querHumano
            || agendamento.RegistrarRespostaNaoEntendida() >= MaximoDeRespostasNaoEntendidas)
        {
            agendamento.LimparAcaoPendente();
            return resposta;
        }

        if (pendente is not null)
        {
            await PerguntarSeTemCertezaAsync(
                agendamento, AcaoParaResposta(pendente), mensagem.Telefone, DesculpaNaoEntendi, ct);
        }
        else
        {
            await PerguntarOQueFazerAsync(agendamento, mensagem.Telefone, DesculpaNaoEntendi, ct);
        }

        await RegistrarRecebidaAsync(agendamento, mensagem, ct);
        await uow.SalvarAsync(ct);
        return null;
    }

    private async Task PerguntarSeTemCertezaAsync(
        Agendamento agendamento, RespostaConfirmacao acao, string telefone, string? abertura, CancellationToken ct)
    {
        var verbo = acao switch
        {
            RespostaConfirmacao.Confirmar => "confirmar",
            RespostaConfirmacao.Cancelar => "cancelar",
            _ => "reagendar"
        };

        await EnviarComBotoesOuTextoAsync(
            telefone,
            $"{(abertura is null ? "" : abertura + " ")}Você deseja {verbo} a sua consulta de {Quando(agendamento)}?",
            [new BotaoResposta(BotaoSim, "Sim"), new BotaoResposta(BotaoNao, "Não")],
            "Responda SIM ou NÃO.", ct);
    }

    private Task PerguntarOQueFazerAsync(
        Agendamento agendamento, string telefone, string abertura, CancellationToken ct) =>
        EnviarComBotoesOuTextoAsync(
            telefone,
            $"{abertura} O que você prefere fazer com a consulta de {Quando(agendamento)}?",
            [
                new BotaoResposta("confirmar", "Confirmar"),
                new BotaoResposta("cancelar", "Cancelar"),
                new BotaoResposta("remarcar", "Reagendar")
            ],
            "Responda confirmar, cancelar ou reagendar.", ct);

    /// <summary>
    /// Botões só valem dentro da janela de 24h, que o clique do paciente acabou de
    /// abrir. Se mesmo assim falharem, a pergunta vai como texto: sem resposta
    /// nenhuma o paciente ficaria sem saber que nada foi feito.
    /// </summary>
    private async Task EnviarComBotoesOuTextoAsync(
        string telefone, string texto, IReadOnlyList<BotaoResposta> botoes, string complemento, CancellationToken ct)
    {
        var envio = await sleekFlow.EnviarBotoesAsync(new EnvioBotoesRequest(telefone, texto, botoes), ct);
        if (envio is { Sucesso: true }) return;

        logger.LogWarning(
            "Botões da segunda confirmação não saíram para {Telefone} ({Erro}); enviando como texto.",
            telefone, envio?.Erro ?? "sem resposta");

        await sleekFlow.EnviarTextoAsync(telefone, $"{texto} {complemento}", ct);
    }

    private string Quando(Agendamento agendamento) =>
        relogio.ParaLocal(agendamento.InicioEm).ToString("dd/MM 'às' HH:mm", CultureInfo.InvariantCulture);

    private string Hora(Agendamento agendamento) =>
        relogio.ParaLocal(agendamento.InicioEm).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    private static bool EhBotaoDaSegundaConfirmacao(MensagemRecebida mensagem) =>
        !string.IsNullOrWhiteSpace(mensagem.BotaoPayload)
        && mensagem.BotaoPayload.Trim().ToLowerInvariant() is BotaoSim or BotaoNao;

    private static string NomeDaAcao(RespostaConfirmacao resposta) => resposta switch
    {
        RespostaConfirmacao.Confirmar => "confirmar",
        RespostaConfirmacao.Cancelar => "cancelar",
        _ => "remarcar"
    };

    private static RespostaConfirmacao AcaoParaResposta(string acao) => acao switch
    {
        "confirmar" => RespostaConfirmacao.Confirmar,
        "cancelar" => RespostaConfirmacao.Cancelar,
        "remarcar" => RespostaConfirmacao.Remarcar,
        _ => RespostaConfirmacao.NaoCompreendida
    };

    /// <summary>
    /// Sim ou não à segunda pergunta. Aceita o id do botão e o texto (o título do
    /// botão chega como texto em alguns canais). <c>null</c> = nem um nem outro.
    /// </summary>
    internal static bool? InterpretarSimNao(MensagemRecebida mensagem)
    {
        var bruto = !string.IsNullOrWhiteSpace(mensagem.BotaoPayload) ? mensagem.BotaoPayload : mensagem.Texto;
        if (string.IsNullOrWhiteSpace(bruto)) return null;

        var texto = RegrasClinicaOptions.Normalizar(bruto).Trim('.', '!', ' ');

        return texto switch
        {
            "acao_sim" or "sim" or "s" => true,
            "acao_nao" or "nao" or "n" => false,
            _ => null
        };
    }

    /// <summary>
    /// Confirmação que o sistema não consegue aplicar sozinho: nenhuma consulta
    /// futura neste número (cadastro sem CPF ou celular na Amplimed, por exemplo)
    /// ou mais de uma. Quem decide é a recepção, e o paciente não fica sem resposta.
    /// </summary>
    private async Task EncaminharConfirmacaoSemAlvoAsync(
        MensagemRecebida mensagem, int consultasEncontradas, CancellationToken ct)
    {
        var nota = consultasEncontradas == 0
            ? "CONFIRMAÇÃO — o paciente respondeu \"confirmar\", mas não há consulta futura vinculada a este número " +
              "no sistema (possível cadastro sem CPF ou celular na Amplimed). Confirmar manualmente na agenda."
            : "CONFIRMAÇÃO — o paciente respondeu \"confirmar\" e há mais de uma consulta futura neste número. " +
              "Confirmar manualmente, indicando qual.";

        logger.LogInformation(
            "Confirmação de {Telefone} sem alvo único ({Consultas} consulta(s)); encaminhada à recepção.",
            mensagem.Telefone, consultasEncontradas);

        await sleekFlow.EnviarTextoAsync(mensagem.Telefone,
            "Recebi sua confirmação! Nossa equipe vai conferir sua consulta e confirmar com você por aqui.", ct);
        await sleekFlow.TransferirParaAtendimentoAsync(mensagem.Telefone, nota, ct);

        await logs.AdicionarAsync(new MensagemLog(
            mensagem.Telefone, TipoMensagem.Recebida, null,
            mensagem.BotaoPayload ?? mensagem.Texto ?? string.Empty,
            null, sucesso: true), ct);

        await uow.SalvarAsync(ct);
    }

    /// <summary>
    /// Leva o status à agenda da clínica. Sem o par (usuclin, idAgendamento) —
    /// caso de agendamento criado pelo WhatsApp que ainda não apareceu em
    /// nenhum polling — o pedido vai para a outbox, para ser aplicado quando o
    /// par existir, em vez de sumir.
    /// </summary>
    private async Task PropagarStatusAsync(
        Agendamento agendamento, StatusAmplimed status, CancellationToken ct)
    {
        if (!agendamento.PodeAlterarStatusNaAmplimed())
        {
            logger.LogWarning(
                "Agendamento {AgendamentoId} ainda sem par (usuclin, idAgendamento); " +
                "status {Status} enfileirado na outbox.",
                agendamento.Id, status);

            await EnfileirarAsync(TipoOutboxStatus, new
            {
                agendamentoId = agendamento.Id,
                idIntegracao = agendamento.IdIntegracao,
                statusCode = (int)status
            }, ct);

            return;
        }

        var ok = await agendamentoClient.AlterarStatusAsync(
            agendamento.Usuclin!, agendamento.IdAgendamentoAmplimed!.Value, status, ct);

        if (!ok)
        {
            await EnfileirarAsync(TipoOutboxStatus, new
            {
                agendamentoId = agendamento.Id,
                usuclin = agendamento.Usuclin,
                idAgendamento = agendamento.IdAgendamentoAmplimed,
                statusCode = (int)status
            }, ct);
        }
    }

    /// <summary>
    /// Cancelamento e remarcação: prefere <c>cancelar-agendamento</c>, que
    /// devolve o horário à escala, e cai em alteração de status quando o
    /// agendamento não nasceu nesta integração.
    /// </summary>
    private async Task LiberarOuCancelarAsync(
        Agendamento agendamento, string motivo, CancellationToken ct)
    {
        // Remarcação não é cancelamento: o status equivalente na Amplimed é
        // Reagendado, e o horário sai da agenda do mesmo jeito.
        if (agendamento.Status == StatusAgendamento.Remarcado)
        {
            if (!agendamento.PodeCancelarNaAmplimed()
                || !await agendamentoClient.CancelarAsync(agendamento.IdIntegracao!, motivo, ct))
            {
                await PropagarStatusAsync(agendamento, StatusAmplimed.Reagendado, ct);
            }

            return;
        }

        // Cancelamento de verdade: um caminho só, compartilhado com o endpoint
        // da clínica e com o nó HTTP do Flow Builder.
        await cancelamento.ExecutarAsync(
            agendamento,
            new PedidoCancelamento(motivo, OrigemCancelamento.Paciente, AvisarPaciente: false),
            ct);
    }

    /// <summary>
    /// Consulta confirmada encerra a conversa: o paciente volta a ter o menu.
    /// Cancelamento, remarcação e resposta não compreendida passam pela
    /// transferência para a recepção, que já troca as etiquetas.
    /// </summary>
    private async Task LiberarEtiquetaAsync(Agendamento agendamento, string telefone, CancellationToken ct)
    {
        if (!agendamento.EtiquetaConfirmacaoAplicada) return;

        if (await sleekFlow.LimparAgendamentoEmCursoAsync(telefone, ct))
        {
            agendamento.MarcarEtiquetaConfirmacaoRemovida();
            return;
        }

        // Fica marcada de propósito: o job de expiração tenta de novo.
        logger.LogError(
            "Não foi possível remover a etiqueta de agendamento em curso de {Telefone} após a confirmação.",
            telefone);
    }

    private Task EnfileirarAsync(string tipo, object payload, CancellationToken ct) =>
        outbox.AdicionarAsync(new OutboxMessage(tipo, JsonSerializer.Serialize(payload)), ct);

    private static bool ConfirmacaoExplicita(MensagemRecebida mensagem)
    {
        if (!string.IsNullOrWhiteSpace(mensagem.BotaoPayload))
            return mensagem.BotaoPayload.Trim().Equals("confirmar", StringComparison.OrdinalIgnoreCase);

        var texto = (mensagem.Texto ?? string.Empty).Trim().ToLowerInvariant();
        return texto is "confirmar" or "confirmo" or "confirmado";
    }

    /// <summary>
    /// O botão é a fonte confiável. Texto livre só é aceito quando inequívoco;
    /// qualquer ambiguidade vira NaoCompreendida e vai para atendimento humano.
    /// </summary>
    private static RespostaConfirmacao Interpretar(MensagemRecebida mensagem)
    {
        if (!string.IsNullOrWhiteSpace(mensagem.BotaoPayload))
        {
            return mensagem.BotaoPayload.Trim().ToLowerInvariant() switch
            {
                "confirmar" => RespostaConfirmacao.Confirmar,
                "remarcar" or "reagendar" => RespostaConfirmacao.Remarcar,
                "cancelar" => RespostaConfirmacao.Cancelar,
                _ => RespostaConfirmacao.NaoCompreendida
            };
        }

        var texto = (mensagem.Texto ?? string.Empty).Trim().ToLowerInvariant();

        return texto switch
        {
            "1" or "sim" or "confirmo" or "confirmar" => RespostaConfirmacao.Confirmar,
            "2" or "remarcar" or "reagendar" => RespostaConfirmacao.Remarcar,
            "3" or "cancelar" => RespostaConfirmacao.Cancelar,
            _ => RespostaConfirmacao.NaoCompreendida
        };
    }
}
