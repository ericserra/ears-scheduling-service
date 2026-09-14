using System.Net.Http.Json;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using Microsoft.Extensions.Logging;

namespace EARS.Integration.Infrastructure.Clients.SleekFlow;

public sealed class SleekFlowClient(
    HttpClient http,
    ILogger<SleekFlowClient> logger) : ISleekFlowClient
{
    public async Task<ResultadoEnvio> EnviarTextoAsync(
        string telefone, string texto, CancellationToken ct = default)
    {
        try
        {
            var resposta = await http.PostAsJsonAsync("message/send", new
            {
                channel = "whatsapp",
                to = telefone,
                messageType = "text",
                messageContent = texto
            }, ct);

            if (!resposta.IsSuccessStatusCode)
            {
                var erro = await resposta.Content.ReadAsStringAsync(ct);
                logger.LogError("SleekFlow recusou o envio de texto: {Status} {Erro}", resposta.StatusCode, erro);
                return ResultadoEnvio.Falha($"{(int)resposta.StatusCode}: {erro}");
            }

            var corpo = await resposta.Content.ReadFromJsonAsync<EnvioResponse>(ct);
            return ResultadoEnvio.Ok(corpo?.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao enviar texto pelo SleekFlow.");
            return ResultadoEnvio.Falha(ex.Message);
        }
    }

    /// <summary>
    /// Template aprovado pela Meta — único envio permitido fora da janela de 24h.
    /// Até 3 botões de resposta rápida por template.
    /// </summary>
    public async Task<ResultadoEnvio> EnviarTemplateAsync(
        EnvioTemplateRequest request, CancellationToken ct = default)
    {
        try
        {
            var resposta = await http.PostAsJsonAsync("message/send", new
            {
                channel = "whatsapp",
                to = request.Telefone,
                messageType = "template",
                templateName = request.TemplateNome,
                templateParams = request.Variaveis.Select(v => new { name = v.Nome, value = v.Valor })
            }, ct);

            if (!resposta.IsSuccessStatusCode)
            {
                var erro = await resposta.Content.ReadAsStringAsync(ct);
                logger.LogError("SleekFlow recusou o template {Template}: {Status} {Erro}",
                    request.TemplateNome, resposta.StatusCode, erro);
                return ResultadoEnvio.Falha($"{(int)resposta.StatusCode}: {erro}");
            }

            var corpo = await resposta.Content.ReadFromJsonAsync<EnvioResponse>(ct);
            return ResultadoEnvio.Ok(corpo?.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao enviar o template {Template}.", request.TemplateNome);
            return ResultadoEnvio.Falha(ex.Message);
        }
    }

    public async Task TransferirParaAtendimentoAsync(
        string telefone, string contexto, CancellationToken ct = default)
    {
        var resposta = await http.PostAsJsonAsync("conversation/assign", new
        {
            channel = "whatsapp",
            to = telefone,
            note = contexto
        }, ct);

        if (!resposta.IsSuccessStatusCode)
            logger.LogError("Falha ao transferir a conversa de {Telefone} para atendimento humano.", telefone);
    }

    private sealed record EnvioResponse(string? Id);
}
