using System.Net;
using System.Net.Http.Json;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using Microsoft.Extensions.Logging;

namespace EARS.Integration.Infrastructure.Clients.Amplimed;

public sealed class AmplimedAgendamentoClient(
    HttpClient http,
    ILogger<AmplimedAgendamentoClient> logger) : IAmplimedAgendamentoClient
{
    public async Task<IReadOnlyList<HorarioDisponivel>> ListarHorariosDisponiveisAsync(
        string profissionalId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var url = $"agendamento/horarios?profissional={Uri.EscapeDataString(profissionalId)}" +
                  $"&inicio={de:yyyy-MM-dd}&fim={ate:yyyy-MM-dd}";

        var resposta = await http.GetAsync(url, ct);
        resposta.EnsureSuccessStatusCode();

        return await resposta.Content.ReadFromJsonAsync<List<HorarioDisponivel>>(ct)
               ?? new List<HorarioDisponivel>();
    }

    public async Task<AgendamentoAmplimed> CriarAsync(CriarAgendamentoRequest request, CancellationToken ct = default)
    {
        var resposta = await http.PostAsJsonAsync("agendamento", request, ct);
        resposta.EnsureSuccessStatusCode();

        return await resposta.Content.ReadFromJsonAsync<AgendamentoAmplimed>(ct)
               ?? throw new InvalidOperationException("Amplimed não retornou o agendamento criado.");
    }

    public async Task CancelarAsync(string agendamentoId, string? motivo, CancellationToken ct = default)
    {
        var url = $"agendamento/{Uri.EscapeDataString(agendamentoId)}";
        if (!string.IsNullOrWhiteSpace(motivo))
            url += $"?motivo={Uri.EscapeDataString(motivo)}";

        var resposta = await http.DeleteAsync(url, ct);
        resposta.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Cláusula 3ª do contrato. Endpoint não descrito na documentação pública:
    /// 404 ou 501 indicam cenário B, e o chamador recorre ao espelho local.
    /// </summary>
    public async Task<IReadOnlyList<AgendamentoAmplimed>?> ListarPorPeriodoAsync(
        DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var url = $"agendamento?inicio={de:yyyy-MM-dd}&fim={ate:yyyy-MM-dd}";
        var resposta = await http.GetAsync(url, ct);

        if (resposta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NotImplemented
            or HttpStatusCode.MethodNotAllowed)
        {
            logger.LogWarning(
                "Leitura de agenda por período indisponível ({Status}); cenário B em vigor.",
                resposta.StatusCode);
            return null;
        }

        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<List<AgendamentoAmplimed>>(ct);
    }

    /// <summary>Cláusula 3ª do contrato. Retorna false quando o endpoint não existe.</summary>
    public async Task<bool> AtualizarStatusAsync(
        string agendamentoId, string status, CancellationToken ct = default)
    {
        var url = $"agendamento/{Uri.EscapeDataString(agendamentoId)}/status";
        var resposta = await http.PatchAsJsonAsync(url, new { status }, ct);

        if (resposta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NotImplemented
            or HttpStatusCode.MethodNotAllowed)
        {
            logger.LogWarning(
                "Atualização de status indisponível ({Status}); registrado apenas no espelho local.",
                resposta.StatusCode);
            return false;
        }

        resposta.EnsureSuccessStatusCode();
        return true;
    }
}
