using System.Net.Http.Json;
using System.Text.Json;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Infrastructure.Clients.SleekFlow;

/// <summary>
/// Client da SleekFlow Platform API.
/// </summary>
/// <remarks>
/// Rotas usadas, todas sob <c>https://api.sleekflow.io</c>:
/// <list type="bullet">
/// <item><c>POST /api/message/send/json</c> — texto, botões e template</item>
/// <item><c>POST /api/contact/addOrUpdate</c> — etiquetas, via addLabels/removeLabels</item>
/// <item><c>POST /api/whatsapp/reply-window-status</c> — janela de 24h</item>
/// <item><c>POST /api/conversation/{id}/assignee</c> — atribuição a um staff</item>
/// </list>
///
/// O corpo do <c>addOrUpdate</c> é um <em>array</em> de contatos, e o telefone
/// vai em formato internacional sem o "+", que é exatamente como o value
/// object Telefone guarda.
/// </remarks>
public sealed partial class SleekFlowClient(
    HttpClient http,
    IOptions<SleekFlowOptions> options,
    ILogger<SleekFlowClient> logger) : ISleekFlowClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string RotaEnvio = "api/message/send/json";
    private const string RotaContato = "api/contact/addOrUpdate";
    private const string RotaJanela = "api/whatsapp/reply-window-status";

    private readonly SleekFlowOptions _opcoes = options.Value;

    private bool CanalCloudApi => _opcoes.Canal.Equals("whatsappcloudapi", StringComparison.OrdinalIgnoreCase);
    private bool Canal360 => _opcoes.Canal.Equals("whatsapp360dialog", StringComparison.OrdinalIgnoreCase);

    // -----------------------------------------------------------------------
    // Envio
    // -----------------------------------------------------------------------

    public Task<ResultadoEnvio> EnviarTextoAsync(
        string telefone, string texto, CancellationToken ct = default) =>
        EnviarAsync(new Dictionary<string, object?>
        {
            ["channel"] = _opcoes.Canal,
            ["from"] = _opcoes.RemetenteTelefone,
            ["to"] = telefone,
            ["messageType"] = "text",
            ["messageContent"] = texto,
            ["analyticTags"] = _opcoes.TagsAnaliticas,
        }, "texto", ct);

    /// <summary>
    /// Botões de resposta rápida. O WhatsApp aceita três; além disso a Meta
    /// recusa a mensagem inteira, então cortamos aqui com aviso em log em vez
    /// de deixar o envio falhar.
    /// </summary>
    public Task<ResultadoEnvio> EnviarBotoesAsync(
        EnvioBotoesRequest request, CancellationToken ct = default)
    {
        var botoes = request.Botoes.Take(3).ToList();

        if (request.Botoes.Count > 3)
            logger.LogWarning("Pedido com {Total} botões; o WhatsApp aceita 3. Os demais foram descartados.", request.Botoes.Count);

        var interativo = new Dictionary<string, object?>
        {
            ["type"] = "button",
            ["body"] = new { type = "text", text = request.Texto },
            ["action"] = new
            {
                buttons = botoes.Select(b => new { type = "reply", reply = new { id = b.Id, title = b.Titulo } })
            },
        };

        var extendida = new Dictionary<string, object?>
        {
            [CanalCloudApi ? "whatsappCloudApiInteractiveObject" : "whatsapp360DialogInteractiveObject"] = interativo,
        };

        return EnviarAsync(new Dictionary<string, object?>
        {
            ["channel"] = _opcoes.Canal,
            ["from"] = _opcoes.RemetenteTelefone,
            ["to"] = request.Telefone,
            ["messageType"] = "interactive",
            ["extendedMessage"] = extendida,
            ["analyticTags"] = _opcoes.TagsAnaliticas,
        }, "botões", ct);
    }

    public Task<ResultadoEnvio> EnviarTemplateAsync(
        EnvioTemplateRequest request, CancellationToken ct = default)
    {
        // Parâmetros posicionais: a ordem da lista é a ordem de {{1}}, {{2}}...
        // no corpo aprovado pela Meta. Mandar por nome não funciona.
        var componentes = new object[]
        {
            new
            {
                type = "body",
                parameters = request.Variaveis.Select(v => new { type = "text", text = v.Valor }),
            },
        };

        var idioma = request.Idioma ?? _opcoes.IdiomaTemplate;

        var extendida = new Dictionary<string, object?>();

        if (Canal360)
        {
            extendida["whatsapp360DialogTemplateMessage"] = new
            {
                templateName = request.TemplateNome,
                templateNamespace = _opcoes.TemplateNamespace,
                language = idioma,
                components = componentes,
            };
        }
        else
        {
            extendida["WhatsappCloudApiTemplateMessageObject"] = new
            {
                templateName = request.TemplateNome,
                language = idioma,
                components = componentes,
            };
        }

        return EnviarAsync(new Dictionary<string, object?>
        {
            ["channel"] = _opcoes.Canal,
            ["from"] = _opcoes.RemetenteTelefone,
            ["to"] = request.Telefone,
            ["messageType"] = "template",
            ["extendedMessage"] = extendida,
            ["analyticTags"] = _opcoes.TagsAnaliticas,
        }, $"template {request.TemplateNome}", ct);
    }

    private async Task<ResultadoEnvio> EnviarAsync(
        Dictionary<string, object?> corpo, string descricao, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opcoes.RemetenteTelefone))
        {
            const string erro = "SleekFlow:RemetenteTelefone não configurado; o campo 'from' é obrigatório.";
            logger.LogError(erro);
            return ResultadoEnvio.Falha(erro);
        }

        try
        {
            var resposta = await http.PostAsJsonAsync(RotaEnvio, corpo, Json, ct);

            if (!resposta.IsSuccessStatusCode)
            {
                var erro = Redigir(await resposta.Content.ReadAsStringAsync(ct));
                logger.LogError(
                    "SleekFlow recusou o envio de {Descricao}: {Codigo} {Erro}",
                    descricao, (int)resposta.StatusCode, erro);
                return ResultadoEnvio.Falha($"{(int)resposta.StatusCode}: {erro}");
            }

            return ResultadoEnvio.Ok(await ExtrairIdAsync(resposta, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao enviar {Descricao} pelo SleekFlow.", descricao);
            return ResultadoEnvio.Falha(ex.Message);
        }
    }

    /// <summary>
    /// A resposta de envio varia de forma entre canais; procuramos os nomes
    /// prováveis em vez de fixar um e perder o id em silêncio.
    /// </summary>
    private static async Task<string?> ExtrairIdAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        var corpo = await resposta.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(corpo)) return null;

        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var chave in new[] { "id", "messageId", "messageChecksum", "messageUniqueID" })
            {
                if (doc.RootElement.TryGetProperty(chave, out var valor))
                    return valor.ValueKind == JsonValueKind.String ? valor.GetString() : valor.ToString();
            }
        }
        catch (JsonException)
        {
            // Resposta não-JSON não é motivo para considerar o envio falho.
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // Janela de 24h
    // -----------------------------------------------------------------------

    public async Task<bool?> JanelaDe24hAbertaAsync(string telefone, CancellationToken ct = default)
    {
        try
        {
            var resposta = await http.PostAsJsonAsync(
                RotaJanela, new { whatsappPhoneNumbers = new[] { telefone } }, Json, ct);

            if (!resposta.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Consulta de janela de 24h falhou ({Codigo}); o chamador decide pelo caminho seguro.",
                    (int)resposta.StatusCode);
                return null;
            }

            var corpo = await resposta.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(corpo);

            // O formato varia; procuramos qualquer booleano que indique janela
            // aberta, em objeto ou em lista.
            return ProcurarJanelaAberta(doc.RootElement);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível consultar a janela de 24h de {Telefone}.", telefone);
            return null;
        }
    }

    private static bool? ProcurarJanelaAberta(JsonElement elemento)
    {
        switch (elemento.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in elemento.EnumerateArray())
                {
                    var achado = ProcurarJanelaAberta(item);
                    if (achado is not null) return achado;
                }
                return null;

            case JsonValueKind.Object:
                foreach (var prop in elemento.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                        && prop.Name.Contains("window", StringComparison.OrdinalIgnoreCase))
                    {
                        return prop.Value.GetBoolean();
                    }

                    var achado = ProcurarJanelaAberta(prop.Value);
                    if (achado is not null) return achado;
                }
                return null;

            default:
                return null;
        }
    }

    // -----------------------------------------------------------------------
    // Etiquetas e transferência
    // -----------------------------------------------------------------------

    public async Task<bool> AjustarEtiquetasAsync(
        string telefone,
        IReadOnlyCollection<string> adicionar,
        IReadOnlyCollection<string> remover,
        CancellationToken ct = default)
    {
        if (adicionar.Count == 0 && remover.Count == 0) return true;

        // O corpo é um array de contatos, e o telefone vai sem o "+".
        var corpo = new[]
        {
            new Dictionary<string, object?>
            {
                ["phoneNumber"] = telefone,
                ["addLabels"] = adicionar,
                ["removeLabels"] = remover,
            },
        };

        try
        {
            var resposta = await http.PostAsJsonAsync(RotaContato, corpo, Json, ct);

            if (!resposta.IsSuccessStatusCode)
            {
                var erro = Redigir(await resposta.Content.ReadAsStringAsync(ct));
                logger.LogError(
                    "Ajuste de etiquetas de {Telefone} falhou: {Codigo} {Erro}. " +
                    "Adicionar: [{Adicionar}] Remover: [{Remover}]",
                    telefone, (int)resposta.StatusCode, erro,
                    string.Join(',', adicionar), string.Join(',', remover));
                return false;
            }

            logger.LogInformation(
                "Etiquetas de {Telefone} ajustadas. Adicionar: [{Adicionar}] Remover: [{Remover}]",
                telefone, string.Join(',', adicionar), string.Join(',', remover));

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao ajustar etiquetas de {Telefone}.", telefone);
            return false;
        }
    }

    public Task<bool> MarcarAgendamentoEmCursoAsync(string telefone, CancellationToken ct = default) =>
        AjustarEtiquetasAsync(telefone, [_opcoes.EtiquetaAgendamentoEmCurso], [], ct);

    public Task<bool> LimparAgendamentoEmCursoAsync(string telefone, CancellationToken ct = default) =>
        AjustarEtiquetasAsync(telefone, [], [_opcoes.EtiquetaAgendamentoEmCurso], ct);

    /// <summary>
    /// Entrega ao atendimento humano. A etiqueta de transferência é o
    /// mecanismo principal, porque a API não atribui conversa a uma equipe; o
    /// staffId, quando configurado, é um reforço.
    /// </summary>
    public async Task TransferirParaAtendimentoAsync(
        string telefone, string contexto, CancellationToken ct = default)
    {
        logger.LogInformation("Transferindo {Telefone} para a Recepção. Contexto: {Contexto}", telefone, contexto);

        await AjustarEtiquetasAsync(
            telefone,
            adicionar: [_opcoes.EtiquetaTransferirRecepcao],
            remover: [_opcoes.EtiquetaAgendamentoEmCurso],
            ct);
    }

    /// <summary>
    /// A SleekFlow devolve a chave usada dentro do corpo de erro de 401
    /// ("Authentication failed. APIKey=[...]"). Registrar isso em log põe a
    /// credencial num arquivo que vai para suporte, backup e terminal de quem
    /// estiver depurando. Trocamos o valor por um marcador.
    /// </summary>
    internal static string Redigir(string? corpo)
    {
        if (string.IsNullOrEmpty(corpo)) return string.Empty;

        return RegexChave().Replace(corpo, "APIKey=[REDIGIDA]");
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"APIKey\s*=\s*\[[^\]]*\]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex RegexChave();
}
