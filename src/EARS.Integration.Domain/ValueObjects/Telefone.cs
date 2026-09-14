using System.Text.RegularExpressions;

namespace EARS.Integration.Domain.ValueObjects;

/// <summary>
/// Telefone em formato E.164 sem o sinal de mais (ex.: 5551999998888).
/// Assume Brasil (+55) quando o DDI não é informado.
/// </summary>
public sealed partial record Telefone
{
    private const string DdiBrasil = "55";

    public string Value { get; }

    private Telefone(string value) => Value = value;

    public static bool TryCreate(string? entrada, out Telefone? telefone)
    {
        telefone = null;
        if (string.IsNullOrWhiteSpace(entrada)) return false;

        var digitos = SomenteDigitos().Replace(entrada, string.Empty);

        // 10 ou 11 dígitos = número nacional sem DDI.
        if (digitos.Length is 10 or 11)
            digitos = DdiBrasil + digitos;

        if (digitos.Length is < 12 or > 13) return false;
        if (!digitos.StartsWith(DdiBrasil, StringComparison.Ordinal)) return false;

        telefone = new Telefone(digitos);
        return true;
    }

    public static Telefone Create(string entrada) =>
        TryCreate(entrada, out var t) && t is not null
            ? t
            : throw new ArgumentException($"Telefone inválido: {entrada}", nameof(entrada));

    public override string ToString() => Value;

    [GeneratedRegex(@"\D")]
    private static partial Regex SomenteDigitos();
}
