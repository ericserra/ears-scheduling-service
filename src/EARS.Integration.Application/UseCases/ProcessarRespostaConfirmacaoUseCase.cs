using System.Text.Json;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using Microsoft.Extensions.Logging;

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
    IUnitOfWork uow,
    ILogger<ProcessarRespostaConfirmacaoUseCase> logger)
{
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

        var resposta = Interpretar(mensagem);

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
                    $"CANCELAMENTO — {agendamento.ProfissionalNome} em {agendamento.InicioEm:dd/MM HH:mm}. Horário liberado.", ct);
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
                    $"REMARCAÇÃO — {agendamento.ProfissionalNome} em {agendamento.InicioEm:dd/MM HH:mm}. " +
                    "O horário original segue reservado na agenda até a recepção remarcar.", ct);
                agendamento.MarcarEtiquetaConfirmacaoRemovida();
                break;

            default:
                logger.LogInformation(
                    "Resposta não compreendida para o agendamento {AgendamentoId}; escalando para atendimento humano.",
                    agendamento.Id);
                await sleekFlow.TransferirParaAtendimentoAsync(mensagem.Telefone,
                    $"Resposta não compreendida sobre a consulta de {agendamento.InicioEm:dd/MM HH:mm}: \"{mensagem.Texto}\"", ct);
                agendamento.MarcarEtiquetaConfirmacaoRemovida();
                break;
        }

        await logs.AdicionarAsync(new MensagemLog(
            mensagem.Telefone, TipoMensagem.Recebida, null,
            mensagem.BotaoPayload ?? mensagem.Texto ?? string.Empty,
            agendamento.Id, sucesso: true), ct);

        await uow.SalvarAsync(ct);
        return true;
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
                "remarcar" => RespostaConfirmacao.Remarcar,
                "cancelar" => RespostaConfirmacao.Cancelar,
                _ => RespostaConfirmacao.NaoCompreendida
            };
        }

        var texto = (mensagem.Texto ?? string.Empty).Trim().ToLowerInvariant();

        return texto switch
        {
            "1" or "sim" or "confirmo" or "confirmar" => RespostaConfirmacao.Confirmar,
            "2" or "remarcar" => RespostaConfirmacao.Remarcar,
            "3" or "cancelar" => RespostaConfirmacao.Cancelar,
            _ => RespostaConfirmacao.NaoCompreendida
        };
    }
}
