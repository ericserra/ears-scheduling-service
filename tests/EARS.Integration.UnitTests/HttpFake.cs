using System.Net;
using System.Text;

namespace EARS.Integration.UnitTests;

/// <summary>
/// Handler de teste que devolve respostas prontas e guarda as requisições feitas,
/// para verificar rota, querystring e corpo enviados à Amplimed.
/// </summary>
public sealed class HttpFake : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Corpo)> _respostas = new();

    public List<HttpRequestMessage> Requisicoes { get; } = [];
    public List<string> Corpos { get; } = [];

    public HttpFake Responder(string corpo, HttpStatusCode status = HttpStatusCode.OK)
    {
        _respostas.Enqueue((status, corpo));
        return this;
    }

    public HttpClient Client(string baseUrl = "https://stgintegracoes.amplimed.cloud/") =>
        new(this) { BaseAddress = new Uri(baseUrl) };

    /// <summary>Caminho + querystring da n-ésima requisição.</summary>
    public string Rota(int indice = 0) =>
        Requisicoes[indice].RequestUri!.PathAndQuery;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requisicoes.Add(request);
        Corpos.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var (status, corpo) = _respostas.Count > 0
            ? _respostas.Dequeue()
            : (HttpStatusCode.OK, "[]");

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(corpo, Encoding.UTF8, "application/json")
        };
    }
}
