using System.Net.Http.Headers;
using System.Net.Http.Json;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Infrastructure.Clients.Amplimed;

/// <summary>
/// API de Dados: troca client_id/client_secret por um token Bearer com validade,
/// mantido em cache até expirar.
/// </summary>
public sealed class AmplimedDadosClient(
    HttpClient http,
    IOptions<AmplimedOptions> options) : IAmplimedDadosClient
{
    private readonly DadosApiOptions _opcoes = options.Value.Dados;
    private readonly SemaphoreSlim _semaforo = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiraEm = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<ProfissionalAmplimed>> ListarProfissionaisAsync(CancellationToken ct = default)
    {
        await AutenticarAsync(ct);
        var resposta = await http.GetAsync("data-api/list-users", ct);
        resposta.EnsureSuccessStatusCode();

        return await resposta.Content.ReadFromJsonAsync<List<ProfissionalAmplimed>>(ct)
               ?? new List<ProfissionalAmplimed>();
    }

    public async Task<IReadOnlyList<PacienteAmplimed>> ListarPacientesAsync(
        int pagina, int tamanhoPagina = 100, CancellationToken ct = default)
    {
        await AutenticarAsync(ct);

        var tamanho = Math.Min(tamanhoPagina, _opcoes.TamanhoPagina);
        var resposta = await http.GetAsync($"data-api/list-patients?page={pagina}&limit={tamanho}", ct);
        resposta.EnsureSuccessStatusCode();

        return await resposta.Content.ReadFromJsonAsync<List<PacienteAmplimed>>(ct)
               ?? new List<PacienteAmplimed>();
    }

    private async Task AutenticarAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expiraEm.AddMinutes(-1))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return;
        }

        await _semaforo.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiraEm.AddMinutes(-1))
                return;

            var resposta = await http.PostAsJsonAsync("auth/token", new
            {
                client_id = _opcoes.ClientId,
                client_secret = _opcoes.ClientSecret
            }, ct);

            resposta.EnsureSuccessStatusCode();

            var token = await resposta.Content.ReadFromJsonAsync<TokenResponse>(ct)
                        ?? throw new InvalidOperationException("Amplimed não retornou token de acesso.");

            _token = token.AccessToken;
            _expiraEm = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }
        finally
        {
            _semaforo.Release();
        }
    }

    private sealed record TokenResponse(string AccessToken, int ExpiresIn);
}
