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
    IAmplimedAgendamentoClient agendamentoClient,
    ISleekFlowClient sleekFlow,
    IMensagemLogRepository logs,
    IOutboxRepository outbox,
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
            return false; // não é resposta de confirmação; segue para o fluxo de agendamento

        var resposta = Interpretar(mensagem);

        switch (resposta)
        {
            case RespostaConfirmacao.Confirmar:
                agendamento.Confirmar();
                await PropagarStatusAsync(agendamento, StatusAmplimed.Confirmado, ct);
                await sleekFlow.EnviarTextoAsync(mensagem.Telefone,
                    "Consulta confirmada. Até breve!", ct);
                break;

            case RespostaConfirmacao.Cancelar:
                agendamento.Cancelar();
                await LiberarOuCancelarAsync(agendamento, "Cancelado pelo paciente via WhatsApp", ct);
                await sleekFlow.EnviarTextoAsync(mensagem.Telefone,
                    "Consulta cancelada. Se quiser remarcar, é só escrever por aqui.", ct);
                await sleekFlow.TransferirParaAtendimentoAsync(mensagem.Telefone,
                    $"CANCELAMENTO — {agendamento.ProfissionalNome} em {agendamento.InicioEm:dd/MM HH:mm}. Horário liberado.", ct);
                break;

            case RespostaConfirmacao.Remarcar:
                agendamento.MarcarRemarcado();
                await LiberarOuCancelarAsync(agendamento, "Remarcação solicitada pelo paciente via WhatsApp", ct);
                // O fluxo do Módulo 1 assume a partir daqui, já com o profissional conhecido.
                break;

            default:
                logger.LogInformation(
                    "Resposta não compreendida para o agendamento {AgendamentoId}; escalando para atendimento humano.",
                    agendamento.Id);
                await sleekFlow.TransferirParaAtendimentoAsync(mensagem.Telefone,
                    $"Resposta não compreendida sobre a consulta de {agendamento.InicioEm:dd/MM HH:mm}: \"{mensagem.Texto}\"", ct);
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
        var statusEquivalente = agendamento.Status == StatusAgendamento.Remarcado
            ? StatusAmplimed.Reagendado
            : StatusAmplimed.Cancelado;

        if (!agendamento.PodeCancelarNaAmplimed())
        {
            await PropagarStatusAsync(agendamento, statusEquivalente, ct);
            return;
        }

        if (await agendamentoClient.CancelarAsync(agendamento.IdIntegracao!, motivo, ct))
            return;

        logger.LogWarning(
            "cancelar-agendamento não concluiu para {IdIntegracao}; tentando alteração de status.",
            agendamento.IdIntegracao);

        await PropagarStatusAsync(agendamento, statusEquivalente, ct);
    }

    private Task EnfileirarAsync(string tipo, object payload, CancellationToken ct) =>
        outbox.AdicionarAsync(new OutboxMessage(tipo, JsonSerializer.Serialize(payload)), ct);

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
