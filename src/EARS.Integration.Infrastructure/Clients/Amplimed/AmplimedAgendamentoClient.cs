using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeZoneConverter;

namespace EARS.Integration.Infrastructure.Clients.Amplimed;

/// <summary>
/// Client da API de Agendamento da Amplimed.
/// </summary>
/// <remarks>
/// Todas as rotas vivem sob <c>integracoes/referencia/{identificador}</c>: o
/// identificador vai no <em>caminho</em>, e o token no cabeçalho
/// <c>x-authorization</c>. Só o token é cabeçalho — mandar o identificador em
/// header, como uma versão anterior fazia, resulta em 404 silencioso.
/// </remarks>
public sealed class AmplimedAgendamentoClient : IAmplimedAgendamentoClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Status que indicam ausência do recurso, não falha de chamada.</summary>
    private static readonly HttpStatusCode[] Indisponivel =
    [
        HttpStatusCode.NotFound,
        HttpStatusCode.NotImplemented,
        HttpStatusCode.MethodNotAllowed
    ];

    private readonly HttpClient _http;
    private readonly ILogger<AmplimedAgendamentoClient> _logger;
    private readonly AgendamentoApiOptions _opcoes;
    private readonly TimeZoneInfo _fuso;

    public AmplimedAgendamentoClient(
        HttpClient http,
        IOptions<AmplimedOptions> amplimed,
        IOptions<AutomacaoOptions> automacao,
        ILogger<AmplimedAgendamentoClient> logger)
    {
        _http = http;
        _logger = logger;
        _opcoes = amplimed.Value.Agendamento;
        _fuso = TZConvert.GetTimeZoneInfo(automacao.Value.FusoHorario);
    }

    private string Raiz => $"integracoes/referencia/{Uri.EscapeDataString(_opcoes.Identificador)}";

    // -----------------------------------------------------------------------
    // Leitura
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<ProfissionalAmplimed>> ListarProfissionaisAsync(
        FiltroProfissionais? filtro = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (filtro?.DataInicial is { } de) query.Add($"dataInicial={DataHoraAmplimed.Data(de)}");
        if (filtro?.DataFinal is { } ate) query.Add($"dataFinal={DataHoraAmplimed.Data(ate)}");
        if (!string.IsNullOrWhiteSpace(filtro?.NomeProfissional))
            query.Add($"nomeProfissional={Uri.EscapeDataString(filtro.NomeProfissional)}");
        if (!string.IsNullOrWhiteSpace(filtro?.Especialidade))
            query.Add($"especialidade={Uri.EscapeDataString(filtro.Especialidade)}");
        if (filtro?.CodigoIbge is { } ibge) query.Add($"codigoIbge={ibge}");

        var url = $"{Raiz}/listar-profissionais{Querystring(query)}";
        var bruto = await LerAsync<List<ProfissionalJson>>(url, ct) ?? [];

        return bruto
            .Where(p => !string.IsNullOrWhiteSpace(p.CodigoProfissional))
            .Select(p => new ProfissionalAmplimed(
                p.CodigoProfissional!,
                p.NomeProfissional ?? "(sem nome)",
                p.Especialidades,
                p.Telemedicina ?? false,
                p.NomeConsultorio,
                (p.DatasDisponiveis ?? [])
                    .Select(DataHoraAmplimed.LerData)
                    .Where(d => d is not null)
                    .Select(d => d!.Value)
                    .ToList(),
                (p.Procedimentos ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                    .Select(x => new ProcedimentoAmplimed(x.Id!, x.Nome ?? string.Empty, x.ExternalId, x.Active ?? true))
                    .ToList(),
                (p.Convenios ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                    .Select(x => new ConvenioAmplimed(x.Id!, x.Nome ?? string.Empty, x.ExternalId, x.Active ?? true))
                    .ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<HorarioDisponivel>> ListarHorariosDisponiveisAsync(
        string codigoProfissional, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var url = $"{Raiz}/listar-horarios/{Uri.EscapeDataString(codigoProfissional)}" +
                  $"?dataInicial={DataHoraAmplimed.Data(de)}&dataFinal={DataHoraAmplimed.Data(ate)}";

        var bruto = await LerAsync<List<HorarioJson>>(url, ct) ?? [];
        var horarios = new List<HorarioDisponivel>(bruto.Count);

        foreach (var h in bruto)
        {
            if (string.IsNullOrWhiteSpace(h.IdHorario)) continue;

            var inicio = DataHoraAmplimed.LerInstante(h.DataHora, _fuso);
            if (inicio is null)
            {
                _logger.LogWarning("Horário {IdHorario} com dataHora ilegível: {Bruto}", h.IdHorario, h.DataHora);
                continue;
            }

            horarios.Add(new HorarioDisponivel(
                h.IdHorario!,
                h.CodigoProfissional ?? codigoProfissional,
                inicio.Value,
                h.Teleconsulta ?? false,
                h.Procedimento,
                h.ProcedimentoDesc,
                h.Convenio,
                h.ConvenioDesc,
                h.Valor));
        }

        return horarios.OrderBy(h => h.Inicio).ToList();
    }

    public async Task<PaginaDaAgenda> ListarAgendamentosDoDiaAsync(
        DateOnly dia, int pagina = 1, string? codigoProfissional = null, string? cpf = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opcoes.Usuclin))
        {
            _logger.LogError(
                "Amplimed:Agendamento:Usuclin não configurado — listar-agendamentos exige o número da clínica.");
            return new PaginaDaAgenda([], pagina, 0);
        }

        var query = new List<string> { $"data={DataHoraAmplimed.Data(dia)}", $"page={Math.Max(1, pagina)}" };
        if (!string.IsNullOrWhiteSpace(codigoProfissional))
            query.Add($"profissional={Uri.EscapeDataString(codigoProfissional)}");
        if (!string.IsNullOrWhiteSpace(cpf))
            query.Add($"cpf={Uri.EscapeDataString(cpf)}");

        var url = $"{Raiz}/{Uri.EscapeDataString(_opcoes.Usuclin)}/listar-agendamentos{Querystring(query)}";
        var bruto = await LerAsync<ListarAgendamentosJson>(url, ct);

        if (bruto is null) return new PaginaDaAgenda([], pagina, 0);

        var itens = new List<AgendamentoDaAgenda>();

        foreach (var profissional in bruto.Profissionais ?? [])
        {
            var dadosProfissional = new ProfissionalDaAgenda(
                profissional.Nome ?? "(sem nome)",
                profissional.Especialidade,
                profissional.Conselho,
                profissional.CodConselho,
                profissional.UfConselho);

            foreach (var a in profissional.Agendamentos ?? [])
            {
                if (a.IdAgendamento is not { } idAgendamento) continue;

                var inicio = DataHoraAmplimed.LerInstante(a.DataHora, _fuso);
                if (inicio is null)
                {
                    _logger.LogWarning(
                        "Agendamento {IdAgendamento} com data_hora ilegível: {Bruto}", idAgendamento, a.DataHora);
                    continue;
                }

                var status = MapaStatus.DeTexto(a.Status);
                if (status is null && !string.IsNullOrWhiteSpace(a.Status))
                {
                    _logger.LogWarning(
                        "Status desconhecido \"{Status}\" no agendamento {IdAgendamento}.", a.Status, idAgendamento);
                }

                var b = a.Beneficiario;

                itens.Add(new AgendamentoDaAgenda(
                    a.Usuclin ?? _opcoes.Usuclin!,
                    idAgendamento,
                    inicio.Value,
                    a.Duracao,
                    status,
                    a.Status,
                    a.Procedimento,
                    a.Convenio,
                    a.Unidade,
                    dadosProfissional,
                    new BeneficiarioDaAgenda(b?.Codp, b?.CodRef, b?.Cpf, b?.Nome, b?.NrCarteira)));
            }
        }

        return new PaginaDaAgenda(itens, bruto.CurrentPage ?? pagina, bruto.TotalPages ?? 1);
    }

    /// <summary>
    /// Percorre as páginas do dia. Sem filtro de profissional ou CPF a API
    /// devolve 50 por página, então uma clínica movimentada precisa de várias.
    /// </summary>
    public async Task<IReadOnlyList<AgendamentoDaAgenda>> ListarAgendaCompletaDoDiaAsync(
        DateOnly dia, CancellationToken ct = default)
    {
        const int tetoPaginas = 50; // guarda contra totalPages absurdo ou laço infinito
        var todos = new List<AgendamentoDaAgenda>();

        var primeira = await ListarAgendamentosDoDiaAsync(dia, 1, ct: ct);
        todos.AddRange(primeira.Agendamentos);

        var total = Math.Min(primeira.TotalPaginas, tetoPaginas);

        for (var pagina = 2; pagina <= total; pagina++)
        {
            ct.ThrowIfCancellationRequested();
            var seguinte = await ListarAgendamentosDoDiaAsync(dia, pagina, ct: ct);
            if (seguinte.Agendamentos.Count == 0) break;
            todos.AddRange(seguinte.Agendamentos);
        }

        if (primeira.TotalPaginas > tetoPaginas)
        {
            _logger.LogWarning(
                "Agenda de {Dia} tem {Total} páginas; lidas as primeiras {Teto}.",
                dia, primeira.TotalPaginas, tetoPaginas);
        }

        return todos;
    }

    public async Task<IReadOnlyList<AgendamentoDoHistorico>> ListarHistoricoDoPacienteAsync(
        string idUnico, CancellationToken ct = default)
    {
        var url = $"{Raiz}/historico-agendamentos/{Uri.EscapeDataString(idUnico)}";
        var bruto = await LerAsync<List<HistoricoJson>>(url, ct) ?? [];

        return bruto
            .Where(h => !string.IsNullOrWhiteSpace(h.Id))
            .Select(h => new AgendamentoDoHistorico(
                h.Id!,
                h.Profissional ?? string.Empty,
                h.NomeProfissional ?? "(sem nome)",
                DataHoraAmplimed.LerInstante(h.HoraAtendimento, _fuso) ?? default,
                MapaStatus.DeTexto(h.Status),
                h.Status,
                h.DescricaoProcedimento))
            .ToList();
    }

    public async Task<string?> BuscarLinkTeleatendimentoAsync(
        string idAgendamento, CancellationToken ct = default)
    {
        var url = $"{Raiz}/buscar-teleatendimento/{Uri.EscapeDataString(idAgendamento)}";
        var resposta = await _http.GetAsync(url, ct);

        if (Indisponivel.Contains(resposta.StatusCode))
        {
            _logger.LogInformation(
                "Sem link de teleatendimento para {IdAgendamento} ({Status}).",
                idAgendamento, resposta.StatusCode);
            return null;
        }

        resposta.EnsureSuccessStatusCode();

        // A documentação não fixa o nome do campo; procuramos os candidatos
        // prováveis em vez de assumir um e falhar calado.
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind == JsonValueKind.String) return doc.RootElement.GetString();
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

        foreach (var chave in new[] { "link", "url", "urlTeleatendimento", "linkTeleatendimento", "teleatendimento" })
        {
            if (doc.RootElement.TryGetProperty(chave, out var valor) && valor.ValueKind == JsonValueKind.String)
                return valor.GetString();
        }

        _logger.LogWarning("Resposta de buscar-teleatendimento sem campo de link reconhecido.");
        return null;
    }

    // -----------------------------------------------------------------------
    // Escrita
    // -----------------------------------------------------------------------

    public async Task<AgendamentoCriado> CadastrarAsync(
        CadastrarAgendamentoRequest request, CancellationToken ct = default)
    {
        var corpo = new CadastrarAgendamentoJson(
            request.IdUnico,
            request.IdHorario,
            string.IsNullOrWhiteSpace(request.Cpf) ? null : SomenteDigitos(request.Cpf),
            request.Nome,
            request.DataNascimento is { } nascimento ? DataHoraAmplimed.Data(nascimento) : null,
            SomenteDigitosNacionais(request.Celular),
            request.Email,
            request.SexoBiologico,
            request.NumeroCarteirinha,
            request.ValidadeCarteirinha is { } validade ? DataHoraAmplimed.Data(validade) : null,
            request.Cep,
            request.Endereco,
            request.Numero,
            request.Complemento,
            request.Bairro,
            request.Cidade,
            request.Uf,
            request.Observacoes);

        var resposta = await _http.PostAsJsonAsync($"{Raiz}/cadastrar-agendamento", corpo, Json, ct);

        if (!resposta.IsSuccessStatusCode)
        {
            var erro = await resposta.Content.ReadAsStringAsync(ct);
            throw new AmplimedException(
                $"cadastrar-agendamento falhou ({(int)resposta.StatusCode}): {erro}", resposta.StatusCode);
        }

        var criado = await resposta.Content.ReadFromJsonAsync<AgendamentoCriadoJson>(Json, ct);

        if (criado is null || string.IsNullOrWhiteSpace(criado.Id))
        {
            throw new AmplimedException(
                "cadastrar-agendamento respondeu sem o id do agendamento.", resposta.StatusCode);
        }

        return new AgendamentoCriado(criado.Id, criado.Message);
    }

    /// <summary>
    /// Exige o par (usuclin, idAgendamento) da listagem. Devolve <c>false</c> em
    /// 404/405/501 — a degradação prevista na Cláusula 3ª, com o status
    /// registrado só no espelho local.
    /// </summary>
    public async Task<bool> AlterarStatusAsync(
        string usuclin, long idAgendamento, StatusAmplimed status, CancellationToken ct = default)
    {
        if (!long.TryParse(usuclin, out var usuclinNumerico))
        {
            _logger.LogError(
                "Usuclin \"{Usuclin}\" não é numérico; alterar-status-agendamento espera número.", usuclin);
            return false;
        }

        var corpo = new AlterarStatusJson(usuclinNumerico, idAgendamento, (int)status);
        var resposta = await _http.PostAsJsonAsync($"{Raiz}/alterar-status-agendamento", corpo, Json, ct);

        if (Indisponivel.Contains(resposta.StatusCode))
        {
            _logger.LogWarning(
                "alterar-status-agendamento indisponível ({Status}) para {IdAgendamento}; " +
                "status mantido apenas no espelho local.",
                resposta.StatusCode, idAgendamento);
            return false;
        }

        if (!resposta.IsSuccessStatusCode)
        {
            var erro = await resposta.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "alterar-status-agendamento recusou {IdAgendamento} para {NovoStatus}: {Codigo} {Erro}",
                idAgendamento, status, (int)resposta.StatusCode, erro);
            return false;
        }

        _logger.LogInformation(
            "Agendamento {IdAgendamento} marcado como {NovoStatus} na Amplimed.", idAgendamento, status);
        return true;
    }

    /// <summary>Só vale para agendamento criado via integração (UUID).</summary>
    public async Task<bool> CancelarAsync(string idIntegracao, string? motivo, CancellationToken ct = default)
    {
        var corpo = new CancelarAgendamentoJson(idIntegracao, motivo);
        var resposta = await _http.PostAsJsonAsync($"{Raiz}/cancelar-agendamento", corpo, Json, ct);

        if (resposta.IsSuccessStatusCode) return true;

        var erro = await resposta.Content.ReadAsStringAsync(ct);
        _logger.LogError(
            "cancelar-agendamento recusou {IdIntegracao}: {Codigo} {Erro}",
            idIntegracao, (int)resposta.StatusCode, erro);
        return false;
    }

    public async Task<bool> ExcluirAsync(string idIntegracao, CancellationToken ct = default)
    {
        var corpo = new ExcluirAgendamentoJson(idIntegracao);
        var resposta = await _http.PostAsJsonAsync($"{Raiz}/excluir-agendamento", corpo, Json, ct);

        if (resposta.IsSuccessStatusCode) return true;

        var erro = await resposta.Content.ReadAsStringAsync(ct);
        _logger.LogError(
            "excluir-agendamento recusou {IdIntegracao}: {Codigo} {Erro}",
            idIntegracao, (int)resposta.StatusCode, erro);
        return false;
    }

    // -----------------------------------------------------------------------

    private async Task<T?> LerAsync<T>(string url, CancellationToken ct)
    {
        var resposta = await _http.GetAsync(url, ct);

        if (Indisponivel.Contains(resposta.StatusCode))
        {
            _logger.LogWarning("Amplimed respondeu {Status} em {Url}.", resposta.StatusCode, url);
            return default;
        }

        if (!resposta.IsSuccessStatusCode)
        {
            var erro = await resposta.Content.ReadAsStringAsync(ct);
            throw new AmplimedException(
                $"GET {url} falhou ({(int)resposta.StatusCode}): {erro}", resposta.StatusCode);
        }

        return await resposta.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private static string Querystring(IReadOnlyCollection<string> partes) =>
        partes.Count == 0 ? string.Empty : "?" + string.Join('&', partes);

    private static string SomenteDigitos(string entrada) =>
        string.Concat(entrada.Where(char.IsDigit));

    /// <summary>
    /// Celular sem DDI, como no exemplo da documentação (<c>47992640155</c>).
    /// O value object guarda E.164 sem o mais, então o 55 sai aqui.
    /// </summary>
    private static string SomenteDigitosNacionais(string entrada)
    {
        var digitos = SomenteDigitos(entrada);

        return digitos.Length is 12 or 13 && digitos.StartsWith("55", StringComparison.Ordinal)
            ? digitos[2..]
            : digitos;
    }
}

/// <summary>Falha de chamada à Amplimed que não é degradação prevista.</summary>
public sealed class AmplimedException(string mensagem, HttpStatusCode status)
    : Exception(mensagem)
{
    public HttpStatusCode Status { get; } = status;

    public override string ToString() =>
        $"{base.ToString()} (HTTP {(int)Status} {Status.ToString()})";
}
