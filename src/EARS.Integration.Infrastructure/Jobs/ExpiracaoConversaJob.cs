using EARS.Integration.Application.UseCases;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EARS.Integration.Infrastructure.Jobs;

/// <summary>
/// Expira conversas abandonadas e libera a etiqueta de agendamento em curso.
/// </summary>
/// <remarks>
/// Desligar este job não degrada o serviço aos poucos: ele acumula pacientes
/// presos sem menu, um por desistência, até alguém notar. Ver
/// <see cref="ExpirarConversasUseCase"/>.
/// </remarks>
[DisallowConcurrentExecution]
public sealed class ExpiracaoConversaJob(
    ExpirarConversasUseCase useCase,
    ILogger<ExpiracaoConversaJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await useCase.ExecutarAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao expirar conversas abandonadas.");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
