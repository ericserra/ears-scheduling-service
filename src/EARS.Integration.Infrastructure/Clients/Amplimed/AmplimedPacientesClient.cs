using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Infrastructure.Clients.Amplimed;

/// <summary>
/// Client da API de Cadastro de Pacientes.
/// </summary>
/// <remarks>
/// Três particularidades que o resto do sistema não precisa conhecer:
/// a rota é <c>pattient</c> (com dois t, como na documentação); a data de
/// nascimento trafega em <c>dd/MM/yyyy</c> aqui e em <c>yyyy-MM-dd</c> na API de
/// Agendamento; e a listagem às vezes vem como array puro, às vezes envelopada
/// em <c>{ "data": [...] }</c>.
///
/// Não existe busca por telefone. Quem chega pelo WhatsApp é resolvido por CPF
/// (pedido na conversa) ou pelo espelho local.
/// </remarks>
public sealed class AmplimedPacientesClient(
    HttpClient http,
    IOptions<AmplimedOptions> amplimed,
    ILogger<AmplimedPacientesClient> logger) : IAmplimedPacientesClient
{
    private const string Rota = "pacientes/integration/pattient";
    private const string RotaCodReferencia = "pacientes/integration/codreferencia";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<PacienteAmplimed?> BuscarPorCpfAsync(string cpf, CancellationToken ct = default)
    {
        var procurado = SomenteDigitos(cpf);
        var encontrados = await ListarAsync(new FiltroPacientes(Cpf: cpf, Pagina: 1), ct);

        // A documentação de "Buscar Paciente" só declara o filtro por janela de
        // atualização. Se o cpf da querystring for ignorado, a resposta vem com
        // a lista inteira e pegar o primeiro casaria o paciente errado — o tipo
        // de erro que termina em consulta marcada no nome de outra pessoa.
        // Conferir aqui custa nada e fecha essa porta.
        var doCpf = encontrados.Where(p => p.Cpf == procurado).ToList();

        if (doCpf.Count == 0 && encontrados.Count > 0)
        {
            logger.LogWarning(
                "Buscar Paciente devolveu {Total} cadastros e nenhum com o CPF pedido; " +
                "o filtro de CPF provavelmente não é aplicado pela Amplimed.", encontrados.Count);
            return null;
        }

        if (doCpf.Count > 1)
        {
            logger.LogWarning(
                "Busca por CPF devolveu {Total} cadastros na Amplimed; usando o primeiro.", doCpf.Count);
        }

        return doCpf.FirstOrDefault();
    }

    public async Task<PacienteAmplimed?> BuscarPorCodReferenciaAsync(
        string codReferencia, CancellationToken ct = default)
    {
        var encontrados = await ListarAsync(
            new FiltroPacientes(CodReferencia: codReferencia, Pagina: 1), ct);

        // Mesma precaução da busca por CPF: só aceitamos o registro que de fato
        // carrega o codReferencia pedido.
        var exato = encontrados.FirstOrDefault(p =>
            string.Equals(p.CodReferencia, codReferencia, StringComparison.OrdinalIgnoreCase));

        if (exato is null && encontrados.Count > 0)
        {
            logger.LogWarning(
                "Buscar Paciente devolveu {Total} cadastros e nenhum com o codReferencia pedido.",
                encontrados.Count);
        }

        return exato;
    }

    public async Task<IReadOnlyList<PacienteAmplimed>> ListarAsync(
        FiltroPacientes filtro, CancellationToken ct = default)
    {
        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(filtro.Cpf))
            query.Add($"cpf={Uri.EscapeDataString(filtro.Cpf)}");
        if (!string.IsNullOrWhiteSpace(filtro.Nome))
            query.Add($"nome={Uri.EscapeDataString(filtro.Nome)}");
        if (!string.IsNullOrWhiteSpace(filtro.CodReferencia))
            query.Add($"codReferencia={Uri.EscapeDataString(filtro.CodReferencia)}");
        if (filtro.AtualizadoDe is { } de)
            query.Add($"dataAtualizacaoInicial={DataHoraAmplimed.Data(de)}");
        if (filtro.AtualizadoAte is { } ate)
            query.Add($"dataAtualizacaoFinal={DataHoraAmplimed.Data(ate)}");

        // dataAtualizacaoInicial/Final são obrigatórios, exceto quando page é
        // enviado. Numa busca pontual por CPF não há janela de atualização que
        // faça sentido, então mandamos a página.
        var pagina = filtro.Pagina ?? (filtro.AtualizadoDe is null || filtro.AtualizadoAte is null ? 1 : null);
        if (pagina is { } p) query.Add($"page={Math.Max(1, p)}");

        var url = query.Count == 0 ? Rota : $"{Rota}?{string.Join('&', query)}";
        var resposta = await http.GetAsync(url, ct);

        if (resposta.StatusCode == HttpStatusCode.NotFound) return [];

        if (!resposta.IsSuccessStatusCode)
        {
            var erro = await resposta.Content.ReadAsStringAsync(ct);
            throw new AmplimedException(
                $"GET {Rota} falhou ({(int)resposta.StatusCode}): {erro}", resposta.StatusCode);
        }

        var corpo = await resposta.Content.ReadAsStringAsync(ct);
        return Normalizar(corpo);
    }

    public async Task<bool> CadastrarAsync(CadastrarPacienteRequest request, CancellationToken ct = default)
    {
        var corpo = new CadastrarPacienteJson(
            request.CodReferencia,
            request.Nome,
            request.Nome,
            string.IsNullOrWhiteSpace(request.Cpf) ? null : SomenteDigitos(request.Cpf),
            request.DataNascimento is { } nascimento ? DataHoraAmplimed.DataBr(nascimento) : null,
            request.Genero,
            FormatarCelular(request.Celular),
            request.Email,
            request.Observacoes is null ? null : new Dictionary<string, string>(request.Observacoes));

        var resposta = await http.PostAsJsonAsync(Rota, corpo, Json, ct);

        if (!resposta.IsSuccessStatusCode)
        {
            var erro = await resposta.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "POST {Rota} recusou o cadastro do codReferencia {CodReferencia}: {Codigo} {Erro}",
                Rota, request.CodReferencia, (int)resposta.StatusCode, erro);
            return false;
        }

        return await SucessoAsync(resposta, ct);
    }

    /// <summary>
    /// Vincula o nosso codReferencia a um cadastro que a Amplimed já tinha para
    /// o CPF. Sem isso, um paciente antigo da clínica entraria no
    /// cadastrar-agendamento como idUnico desconhecido.
    /// </summary>
    public async Task<bool> VincularCodReferenciaAsync(
        string cpf, string codReferencia, CancellationToken ct = default)
    {
        var usuclin = amplimed.Value.Agendamento.Usuclin;

        if (string.IsNullOrWhiteSpace(usuclin))
        {
            logger.LogError(
                "Vínculo de codReferencia abortado: a rota codreferencia exige o cabeçalho usuclin, " +
                "e Amplimed:Agendamento:Usuclin está vazio.");
            return false;
        }

        var corpo = new VincularCodReferenciaJson(SomenteDigitos(cpf), codReferencia);

        // Esta rota foge do padrão das outras duas: a documentação a mostra com
        // "Authorization" (sem o x-) e exige o cabeçalho "usuclin". O
        // x-Authorization do HttpClient continua indo junto, porque não custa e
        // cobre o caso de a documentação estar com erro de digitação.
        using var requisicao = new HttpRequestMessage(HttpMethod.Put, RotaCodReferencia)
        {
            Content = JsonContent.Create(corpo, options: Json),
        };
        requisicao.Headers.TryAddWithoutValidation(
            "Authorization", $"Basic {amplimed.Value.Pacientes.Token}");
        requisicao.Headers.TryAddWithoutValidation("usuclin", usuclin);

        var resposta = await http.SendAsync(requisicao, ct);

        if (!resposta.IsSuccessStatusCode)
        {
            var erro = await resposta.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "PUT {Rota} recusou o vínculo do codReferencia {CodReferencia}: {Codigo} {Erro}",
                RotaCodReferencia, codReferencia, (int)resposta.StatusCode, erro);
            return false;
        }

        return await SucessoAsync(resposta, ct);
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// 2xx com corpo vazio é sucesso. Desserializar direto quebraria aqui, e um
    /// cadastro que deu certo seria contado como falha.
    /// </summary>
    private async Task<bool> SucessoAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        var corpo = (await resposta.Content.ReadAsStringAsync(ct)).Trim();
        if (corpo.Length == 0) return true;

        try
        {
            return JsonSerializer.Deserialize<SucessoJson>(corpo, Json)?.Success ?? true;
        }
        catch (JsonException)
        {
            logger.LogWarning("Resposta da API de Pacientes não era JSON reconhecível: {Corpo}", corpo);
            return true;
        }
    }

    /// <summary>Aceita tanto o array puro quanto o envelope <c>{ data: [...] }</c>.</summary>
    private IReadOnlyList<PacienteAmplimed> Normalizar(string corpo)
    {
        if (string.IsNullOrWhiteSpace(corpo)) return [];

        List<PacienteJson>? bruto;

        using var doc = JsonDocument.Parse(corpo);

        bruto = doc.RootElement.ValueKind switch
        {
            JsonValueKind.Array => doc.RootElement.Deserialize<List<PacienteJson>>(Json),
            JsonValueKind.Object => doc.RootElement.Deserialize<PacientesEnvelopeJson>(Json)?.Data,
            _ => null
        };

        if (bruto is null) return [];

        return bruto
            .Select(p => new PacienteAmplimed(
                string.IsNullOrWhiteSpace(p.CodReferencia) ? null : p.CodReferencia,
                p.NomeRegistro ?? p.NomeSocial ?? "(sem nome)",
                string.IsNullOrWhiteSpace(p.Cpf) ? null : SomenteDigitos(p.Cpf),
                string.IsNullOrWhiteSpace(p.Celular) ? null : p.Celular,
                DataHoraAmplimed.LerDataBr(p.DataNascimento),
                string.IsNullOrWhiteSpace(p.Email) ? null : p.Email,
                string.IsNullOrWhiteSpace(p.Genero) ? null : p.Genero))
            .ToList();
    }

    /// <summary>
    /// A documentação pede <c>(XX) XXXXX-XXXX</c>. Nosso value object guarda
    /// E.164 sem o mais (5548999999999), então o DDI sai aqui.
    /// </summary>
    public static string? FormatarCelular(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return null;

        var digitos = SomenteDigitos(telefone);

        if (digitos.Length is 12 or 13 && digitos.StartsWith("55", StringComparison.Ordinal))
            digitos = digitos[2..];

        return digitos.Length switch
        {
            11 => $"({digitos[..2]}) {digitos[2..7]}-{digitos[7..]}",
            10 => $"({digitos[..2]}) {digitos[2..6]}-{digitos[6..]}",
            _ => digitos
        };
    }

    private static string SomenteDigitos(string entrada) =>
        string.Concat(entrada.Where(char.IsDigit));
}
