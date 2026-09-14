using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace EARS.Integration.Infrastructure.Resilience;

/// <summary>
/// Retry com backoff exponencial, timeout e circuit breaker por client,
/// conforme a arquitetura aprovada na proposta.
/// </summary>
public static class PoliticasHttp
{
    public static IAsyncPolicy<HttpResponseMessage> Retry(ILogger logger, string nomeCliente) =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: tentativa => TimeSpan.FromSeconds(Math.Pow(2, tentativa)),
                onRetry: (resultado, espera, tentativa, _) =>
                    logger.LogWarning(
                        "{Cliente}: tentativa {Tentativa} após {Espera}s. Status: {Status}",
                        nomeCliente, tentativa, espera.TotalSeconds,
                        resultado.Result?.StatusCode.ToString() ?? resultado.Exception?.GetType().Name));

    public static IAsyncPolicy<HttpResponseMessage> CircuitBreaker(ILogger logger, string nomeCliente) =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (_, duracao) =>
                    logger.LogError("{Cliente}: circuito aberto por {Duracao}s.", nomeCliente, duracao.TotalSeconds),
                onReset: () => logger.LogInformation("{Cliente}: circuito fechado.", nomeCliente));

    public static IAsyncPolicy<HttpResponseMessage> Timeout() =>
        Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(15));
}
