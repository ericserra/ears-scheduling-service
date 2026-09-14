using System.Text.Json;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.UseCases;
using EARS.Integration.Infrastructure.Clients;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Api.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/webhooks").WithTags("Webhooks");

        grupo.MapPost("/sleekflow", async (
            HttpRequest request,
            ProcessarRespostaConfirmacaoUseCase confirmacao,
            ProcessarMensagemRecebidaUseCase agendamento,
            IOptions<SleekFlowOptions> options,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Webhook.SleekFlow");

            // Validação do segredo compartilhado antes de qualquer processamento.
            var segredoEsperado = options.Value.WebhookSecret;
            if (!string.IsNullOrEmpty(segredoEsperado))
            {
                var recebido = request.Headers["X-SleekFlow-Signature"].ToString();
                if (!string.Equals(recebido, segredoEsperado, StringComparison.Ordinal))
                {
                    logger.LogWarning("Webhook rejeitado: assinatura inválida.");
                    return Results.Unauthorized();
                }
            }

            using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            var mensagem = Normalizar(doc.RootElement);

            if (mensagem is null)
            {
                logger.LogInformation("Webhook ignorado: payload sem mensagem de paciente.");
                return Results.Ok();
            }

            // A resposta ao template de véspera tem precedência; se não for o caso,
            // a mensagem segue para a máquina de estados do agendamento.
            var tratada = await confirmacao.ExecutarAsync(mensagem, ct);
            if (!tratada)
                await agendamento.ExecutarAsync(mensagem, ct);

            return Results.Ok();
        })
        .WithName("ReceberWebhookSleekFlow")
        .WithSummary("Recebe mensagens e respostas de botão encaminhadas pelo SleekFlow.");
    }

    /// <summary>
    /// Normaliza o payload do SleekFlow para o DTO interno.
    /// Ajustar os nomes dos campos conforme o contrato real do webhook na homologação.
    /// </summary>
    private static MensagemRecebida? Normalizar(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("message", out var msg)) return null;

        var telefone = msg.TryGetProperty("from", out var f) ? f.GetString() : null;
        if (string.IsNullOrWhiteSpace(telefone)) return null;

        var texto = msg.TryGetProperty("text", out var t) ? t.GetString() : null;
        var botao = msg.TryGetProperty("buttonPayload", out var b) ? b.GetString() : null;
        var contato = raiz.TryGetProperty("contact", out var c) && c.TryGetProperty("id", out var cid)
            ? cid.GetString()
            : null;

        return new MensagemRecebida(telefone, contato, texto, botao, DateTimeOffset.UtcNow);
    }
}
