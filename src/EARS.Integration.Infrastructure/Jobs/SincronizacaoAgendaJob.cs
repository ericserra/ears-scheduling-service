using EARS.Integration.Application.UseCases;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EARS.Integration.Infrastructure.Jobs;

/// <summary>
/// Polling da agenda da clínica. Existe porque a Amplimed não oferece webhook
/// nesta integração: sem consulta periódica, agendamento marcado no sistema da
/// clínica nunca chegaria ao espelho local, e o par (usuclin, idAgendamento)
/// necessário para confirmar status nunca seria descoberto.
/// </summary>
[DisallowConcurrentExecution]
public sealed class SincronizacaoAgendaJob(
    SincronizarAgendaUseCase useCase,
    ILogger<SincronizacaoAgendaJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await useCase.ExecutarAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha no polling da agenda da Amplimed.");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
