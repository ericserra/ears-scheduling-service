using EARS.Integration.Application.UseCases;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EARS.Integration.Infrastructure.Jobs;

/// <summary>Módulo 2, passo 6: varredura das consultas de D+1 no horário configurado.</summary>
[DisallowConcurrentExecution]
public sealed class ConfirmacaoD1Job(
    EnviarConfirmacaoD1UseCase useCase,
    ILogger<ConfirmacaoD1Job> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Iniciando a varredura de confirmação D-1.");
        try
        {
            await useCase.ExecutarAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha na varredura de confirmação D-1.");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
