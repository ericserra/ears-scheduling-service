using System.Text.Json;
using System.Text.Json.Serialization;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.UseCases;
using EARS.Integration.Domain.Enums;
using EARS.Integration.Infrastructure.Clients;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Api.Endpoints;

/// <summary>
/// Superfície que o SleekFlow consome.
/// </summary>
/// <remarks>
/// São duas portas, com papéis distintos (ver ADR 0004):
///
/// <list type="bullet">
/// <item><c>POST /webhooks/sleekflow/atendimento</c> — chamada pelo nó HTTP do
/// Flow Builder nos ramos 1 e 3 do menu. É o único jeito de o motor assumir a
/// conversa.</item>
/// <item><c>POST /webhooks/sleekflow</c> — mensagens recebidas. O motor só
/// trata as de quem já está em atendimento; o resto devolve 204 e segue com o
/// fluxo, porque o menu é do Flow Builder, não daqui.</item>
/// </list>
/// </remarks>
public static class WebhookEndpoints
{
    /// <summary>Corpo esperado do nó HTTP do Flow Builder.</summary>
    public sealed record IniciarAtendimentoCorpo(
        [property: JsonPropertyName("telefone")] string? Telefone,
        [property: JsonPropertyName("motivo")] string? Motivo,
        [property: JsonPropertyName("contatoId")] string? ContatoId,
        [property: JsonPropertyName("conversaId")] string? ConversaId,
        [property: JsonPropertyName("nome")] string? Nome);

    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/webhooks").WithTags("Webhooks");

        grupo.MapPost("/sleekflow/atendimento", async (
            IniciarAtendimentoCorpo corpo,
            HttpRequest request,
            IniciarAtendimentoUseCase useCase,
            IOptions<SleekFlowOptions> options,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Webhook.Atendimento");

            if (!AssinaturaValida(request, options.Value, logger))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(corpo.Telefone))
                return Results.BadRequest(new { erro = "telefone é obrigatório." });

            if (!TentarLerMotivo(corpo.Motivo, out var motivo))
            {
                return Results.BadRequest(new
                {
                    erro = "motivo inválido.",
                    aceitos = new[] { "agendar-consulta", "remarcar", "cancelar" },
                });
            }

            var resultado = await useCase.ExecutarAsync(
                new IniciarAtendimentoRequest(
                    corpo.Telefone!, motivo, corpo.ContatoId, corpo.ConversaId, corpo.Nome),
                ct);

            // 503 para o Flow Builder cair no ramo de erro e mandar à Recepção,
            // em vez de achar que o motor assumiu a conversa.
            return resultado.Aceito
                ? Results.Ok(resultado)
                : Results.Json(resultado, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .WithName("IniciarAtendimento")
        .WithSummary("Entrega a conversa ao motor, a partir dos ramos 1 e 3 do menu.");

        grupo.MapPost("/sleekflow", async (
            HttpRequest request,
            ProcessarRespostaConfirmacaoUseCase confirmacao,
            ProcessarMensagemRecebidaUseCase atendimento,
            IOptions<SleekFlowOptions> options,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Webhook.SleekFlow");

            if (!AssinaturaValida(request, options.Value, logger))
                return Results.Unauthorized();

            using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            var mensagem = Normalizar(doc.RootElement);

            if (mensagem is null)
            {
                logger.LogInformation("Webhook ignorado: payload sem mensagem de paciente.");
                return Results.NoContent();
            }

            // A resposta ao template de véspera tem precedência.
            if (await confirmacao.ExecutarAsync(mensagem, ct))
                return Results.Ok(new { tratadoPor = "confirmacao" });

            // Sem atendimento em curso, a mensagem é do menu: o Flow Builder
            // resolve. Devolver 204 em vez de 200 deixa isso explícito nos
            // registros do SleekFlow.
            if (await atendimento.ExecutarAsync(mensagem, ct))
                return Results.Ok(new { tratadoPor = "atendimento" });

            return Results.NoContent();
        })
        .WithName("ReceberWebhookSleekFlow")
        .WithSummary("Recebe mensagens e respostas de botão encaminhadas pelo SleekFlow.");
    }

    /// <summary>
    /// Segredo compartilhado, comparado em tempo constante para não vazar o
    /// prefixo correto pelo tempo de resposta. Segredo vazio desliga a
    /// verificação, o que só faz sentido em desenvolvimento.
    /// </summary>
    private static bool AssinaturaValida(HttpRequest request, SleekFlowOptions opcoes, ILogger logger)
    {
        if (string.IsNullOrEmpty(opcoes.WebhookSecret)) return true;

        var recebido = request.Headers["X-SleekFlow-Signature"].ToString();

        var iguais = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(recebido),
            System.Text.Encoding.UTF8.GetBytes(opcoes.WebhookSecret));

        if (!iguais) logger.LogWarning("Webhook rejeitado: assinatura inválida.");

        return iguais;
    }

    private static bool TentarLerMotivo(string? bruto, out MotivoAtendimento motivo)
    {
        motivo = MotivoAtendimento.AgendarConsulta;
        if (string.IsNullOrWhiteSpace(bruto)) return false;

        switch (bruto.Trim().ToLowerInvariant().Replace("_", "-"))
        {
            case "agendar-consulta" or "agendar" or "consulta":
                motivo = MotivoAtendimento.AgendarConsulta;
                return true;
            case "remarcar" or "remarcacao" or "reagendar":
                motivo = MotivoAtendimento.Remarcar;
                return true;
            case "cancelar" or "cancelamento":
                motivo = MotivoAtendimento.Cancelar;
                return true;
            default:
                return false;
        }
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
