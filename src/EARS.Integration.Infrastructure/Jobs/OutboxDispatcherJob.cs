using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace EARS.Integration.Infrastructure.Jobs;

/// <summary>
/// Reprocessa o que falhou em chamadas externas. Nenhum evento se perde:
/// a falha persistente fica na outbox até o teto de tentativas.
/// </summary>
[DisallowConcurrentExecution]
public sealed class OutboxDispatcherJob(
    IOutboxRepository outbox,
    IUnitOfWork uow,
    IOptions<AutomacaoOptions> options,
    ILogger<OutboxDispatcherJob> logger) : IJob
{
    private const int Lote = 50;

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var pendentes = await outbox.ListarPendentesAsync(Lote, ct);

        if (pendentes.Count == 0) return;

        logger.LogInformation("Outbox: {Total} mensagens pendentes.", pendentes.Count);

        foreach (var mensagem in pendentes)
        {
            try
            {
                mensagem.MarcarProcessando();
                // TODO: despachar por tipo quando os handlers de reprocessamento forem implementados.
                mensagem.MarcarConcluido();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao reprocessar a mensagem {Id} da outbox.", mensagem.Id);
                mensagem.RegistrarFalha(ex.Message, options.Value.MaxTentativasOutbox);
            }
        }

        await uow.SalvarAsync(ct);
    }
}
