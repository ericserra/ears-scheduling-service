using System.Net;
using System.Net.Http.Json;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using Microsoft.Extensions.Logging;

namespace EARS.Integration.Infrastructure.Clients.Amplimed;

public sealed class AmplimedPacientesClient(
    HttpClient http,
    ILogger<AmplimedPacientesClient> logger) : IAmplimedPacientesClient
{
    public async Task<PacienteAmplimed?> BuscarPorCpfETelefoneAsync(
        string cpf, string telefone, CancellationToken ct = default)
    {
        // Busca combinada: telefone isolado não desambigua pacientes que o compartilham.
        var url = $"pacientes?cpf={Uri.EscapeDataString(cpf)}&telefone={Uri.EscapeDataString(telefone)}";
        return await BuscarPrimeiroAsync(url, ct);
    }

    public Task<PacienteAmplimed?> BuscarPorCpfAsync(string cpf, CancellationToken ct = default) =>
        BuscarPrimeiroAsync($"pacientes?cpf={Uri.EscapeDataString(cpf)}", ct);

    public async Task<PacienteAmplimed> CriarAsync(CriarPacienteRequest request, CancellationToken ct = default)
    {
        var resposta = await http.PostAsJsonAsync("pacientes", request, ct);
        resposta.EnsureSuccessStatusCode();

        var criado = await resposta.Content.ReadFromJsonAsync<PacienteAmplimed>(ct);
        return criado ?? throw new InvalidOperationException("Amplimed não retornou o paciente criado.");
    }

    private async Task<PacienteAmplimed?> BuscarPrimeiroAsync(string url, CancellationToken ct)
    {
        var resposta = await http.GetAsync(url, ct);

        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        resposta.EnsureSuccessStatusCode();

        var lista = await resposta.Content.ReadFromJsonAsync<List<PacienteAmplimed>>(ct);

        if (lista is null || lista.Count == 0) return null;

        if (lista.Count > 1)
            logger.LogWarning("Busca retornou {Total} pacientes; usando o primeiro. URL: {Url}", lista.Count, url);

        return lista[0];
    }
}
