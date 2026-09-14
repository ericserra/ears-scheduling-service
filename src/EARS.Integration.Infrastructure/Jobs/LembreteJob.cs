using EARS.Integration.Application.UseCases;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EARS.Integration.Infrastructure.Jobs;

/// <summary>Lembrete único da manhã seguinte para quem não respondeu.</summary>
[DisallowConcurrentExecution]
public sealed class LembreteJob(
    EnviarLembreteUseCase useCase,
    ILogger<LembreteJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Iniciando o envio do lembrete único.");
        try
        {
            await useCase.ExecutarAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha no envio do lembrete.");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
