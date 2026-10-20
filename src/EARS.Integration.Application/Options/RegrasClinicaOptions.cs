using System.Globalization;
using System.Text;

namespace EARS.Integration.Application.Options;

/// <summary>
/// Regras de negócio da Clínica EARS, em configuração e não em código.
/// </summary>
/// <remarks>
/// Elas são decisões da clínica (handoff da SleekFlow, 20/10/2026) e mudam sem
/// aviso: médica entra, convênio sai, faixa etária se ajusta. Compilar isso
/// significaria um deploy a cada mudança de grade.
/// </remarks>
public sealed class RegrasClinicaOptions
{
    public const string SectionName = "Clinica";

    /// <summary>Frase exata acordada com a clínica para toda transferência.</summary>
    public string FraseTransferencia { get; set; } =
        "Vou encaminhar seu atendimento para a nossa equipe, que continua com você por aqui.";

    /// <summary>Idade abaixo da qual o paciente é criança, para a grade de profissionais.</summary>
    public int IdadeCrianca { get; set; } = 12;

    /// <summary>
    /// Idade mínima assumida para profissional que não está na lista abaixo.
    /// O padrão é conservador: na dúvida, não ofertar para criança.
    /// </summary>
    public int IdadeMinimaPadrao { get; set; } = 12;

    public List<ProfissionalRegra> Profissionais { get; set; } =
    [
        new() { Nome = "Leticia Rosito", IdadeMinima = 0 },
        new() { Nome = "Caroline Elias", IdadeMinima = 0 },
        new() { Nome = "Renata Tramontin", IdadeMinima = 12 },
    ];

    /// <summary>
    /// Convênios cuja triagem é humana. Não ofertar horário nem citar médicas:
    /// a conversa vai direto para a Recepção.
    /// </summary>
    public List<string> ConveniosTriagemHumana { get; set; } = ["IPE", "Unimed Unifácil"];

    /// <summary>Convênios que cobrem apenas consulta, não exame.</summary>
    public List<string> ConveniosSomenteConsulta { get; set; } = ["Saúde PAS"];

    /// <summary>Convênios aceitos. Fora desta lista, a saída é particular.</summary>
    public List<string> ConveniosAceitos { get; set; } = [];

    /// <summary>Resposta quando o convênio não está na lista.</summary>
    public string RespostaConvenioForaDaLista { get; set; } =
        "Esse convênio não está na nossa lista. Posso seguir como particular, " +
        "com pagamento em dinheiro ou Pix. Quer continuar assim?";

    /// <summary>
    /// Teleconsulta: a clínica avalia caso a caso. O bot não confirma nem nega.
    /// </summary>
    public string RespostaTeleconsulta { get; set; } =
        "Sobre atendimento por vídeo, quem avalia é a nossa equipe. " +
        "Vou seguir com o agendamento e eles falam com você sobre isso.";

    /// <summary>
    /// Confirmação de cancelamento. A clínica não cobra taxa — nunca mencionar
    /// multa, nem para dizer que não há, porque introduz a ideia.
    /// </summary>
    public string RespostaCancelamento { get; set; } =
        "Pronto, sua consulta foi cancelada. Se quiser remarcar, é só escrever por aqui.";

    /// <summary>
    /// Termos que caracterizam urgência. A conversa vai na hora para a
    /// Recepção, e o bot nunca encaminha ao pronto-atendimento por conta.
    /// </summary>
    public List<string> TermosUrgencia { get; set; } =
    [
        "perda subita", "perda repentina", "perdi a audicao", "parei de ouvir",
        "nao estou ouvindo", "surdez subita", "surdez repentina", "urgencia", "emergencia",
    ];

    /// <summary>Termos que pedem atendimento humano em qualquer ponto.</summary>
    public List<string> TermosAtendente { get; set; } =
        ["atendente", "humano", "recepcao", "pessoa", "alguem", "secretaria"];

    public List<string> TermosTeleconsulta { get; set; } =
        ["teleconsulta", "online", "video", "remoto", "a distancia"];

    // -----------------------------------------------------------------------

    /// <summary>Idade mínima do profissional, pelo nome como a Amplimed devolve.</summary>
    public int IdadeMinimaDe(string nomeProfissional)
    {
        var regra = Profissionais.FirstOrDefault(p => CorrespondeAoNome(p.Nome, nomeProfissional));
        return regra?.IdadeMinima ?? IdadeMinimaPadrao;
    }

    public bool AtendeIdade(string nomeProfissional, int? idade) =>
        idade is null || idade >= IdadeMinimaDe(nomeProfissional);

    public bool ExigeTriagemHumana(string? convenio) => Contem(ConveniosTriagemHumana, convenio);

    public bool SomenteConsulta(string? convenio) => Contem(ConveniosSomenteConsulta, convenio);

    /// <summary>
    /// Lista vazia significa "não sei quais são os aceitos", e nesse caso não
    /// empurramos ninguém para particular por engano.
    /// </summary>
    public bool ConvenioAceito(string? convenio) =>
        ConveniosAceitos.Count == 0 || Contem(ConveniosAceitos, convenio);

    public bool MencionaUrgencia(string? texto) => Contem(TermosUrgencia, texto, porSubstring: true);

    public bool PedeAtendente(string? texto) => Contem(TermosAtendente, texto, porSubstring: true);

    public bool MencionaTeleconsulta(string? texto) => Contem(TermosTeleconsulta, texto, porSubstring: true);

    /// <summary>
    /// Comparação tolerante a acento e caixa. "Unimed Unifácil" digitado como
    /// "unimed unifacil" tem de cair na mesma regra.
    /// </summary>
    private static bool Contem(IEnumerable<string> termos, string? entrada, bool porSubstring = false)
    {
        if (string.IsNullOrWhiteSpace(entrada)) return false;

        var alvo = Normalizar(entrada);

        return termos.Any(t =>
        {
            var termo = Normalizar(t);
            if (termo.Length == 0) return false;
            return porSubstring ? alvo.Contains(termo, StringComparison.Ordinal) : alvo == termo;
        });
    }

    private static bool CorrespondeAoNome(string regra, string nomeVindoDaApi)
    {
        var a = Normalizar(regra);
        var b = Normalizar(nomeVindoDaApi);

        // A Amplimed devolve "Dra Maria Silva"; a regra guarda "Maria Silva".
        return a.Length > 0 && (b.Contains(a, StringComparison.Ordinal) || a.Contains(b, StringComparison.Ordinal));
    }

    public static string Normalizar(string entrada)
    {
        var decomposto = entrada.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);

        foreach (var c in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

public sealed class ProfissionalRegra
{
    public string Nome { get; set; } = string.Empty;

    /// <summary>Código na Amplimed, quando conhecido. Opcional.</summary>
    public string? CodigoAmplimed { get; set; }

    /// <summary>Idade mínima atendida. 0 significa que atende crianças.</summary>
    public int IdadeMinima { get; set; }
}
