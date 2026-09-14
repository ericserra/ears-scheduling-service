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
public sealed class ProcessarRespostaConfirmacaoUseCase(
    IAgendamentoRepository agendamentos,
    IAmplimedAgendamentoClient agendamentoClient,
    ISleekFlowClient sleekFlow,
    IMensagemLogRepository logs,
    IUnitOfWork uow,
    ILogger<ProcessarRespostaConfirmacaoUseCase> logger)
{
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
                await agendamentoClient.AtualizarStatusAsync(agendamento.AmplimedId, "confirmado", ct);
                await sleekFlow.EnviarTextoAsync(mensagem.Telefone,
                    "Consulta confirmada. Até breve!", ct);
                break;

            case RespostaConfirmacao.Cancelar:
                agendamento.Cancelar();
                await agendamentoClient.CancelarAsync(agendamento.AmplimedId, "cancelado pelo paciente", ct);
                await sleekFlow.EnviarTextoAsync(mensagem.Telefone,
                    "Consulta cancelada. Se quiser remarcar, é só escrever por aqui.", ct);
                await sleekFlow.TransferirParaAtendimentoAsync(mensagem.Telefone,
                    $"CANCELAMENTO — {agendamento.ProfissionalNome} em {agendamento.InicioEm:dd/MM HH:mm}. Horário liberado.", ct);
                break;

            case RespostaConfirmacao.Remarcar:
                agendamento.MarcarRemarcado();
                await agendamentoClient.CancelarAsync(agendamento.AmplimedId, "remarcação solicitada", ct);
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
