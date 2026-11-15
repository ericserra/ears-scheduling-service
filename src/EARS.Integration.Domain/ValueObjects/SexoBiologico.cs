using System.Globalization;
using System.Text;

namespace EARS.Integration.Domain.ValueObjects;

/// <summary>
/// Sexo biológico no formato que o <c>cadastrar-agendamento</c> da Amplimed aceita
/// ("M" ou "F"). O campo é obrigatório lá: sem ele o gateway responde 400
/// "Invalid params", mesmo com todo o resto do corpo correto.
/// </summary>
public static class SexoBiologico
{
    public const string Masculino = "M";
    public const string Feminino = "F";

    /// <summary>
    /// Lê a resposta livre do paciente ("feminino", "Masculino", "mulher", "f"...).
    /// Devolve <c>null</c> quando não dá para saber, para o bot perguntar de novo
    /// em vez de gravar um palpite no prontuário.
    /// </summary>
    public static string? Interpretar(string? entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada)) return null;

        var texto = SemAcento(entrada.Trim().ToLowerInvariant());

        var feminino = texto is "f" || texto.Contains("femin") || texto.Contains("mulher");
        var masculino = texto is "m" || texto.Contains("mascul") || texto.Contains("homem");

        if (feminino == masculino) return null;
        return feminino ? Feminino : Masculino;
    }

    private static string SemAcento(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);

        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
