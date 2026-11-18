using System.Text.RegularExpressions;

namespace EARS.Integration.Domain.ValueObjects;

/// <summary>
/// Telefone em formato E.164 sem o sinal de mais (ex.: 5551999998888).
/// Assume Brasil (+55) quando o DDI não é informado.
/// </summary>
/// <remarks>
/// Celular brasileiro sai sempre com o nono dígito (13 dígitos). O WhatsApp, em
/// alguns números antigos, reporta o identificador sem ele (12 dígitos), e o mesmo
/// paciente passava a ter dois "telefones" diferentes: a resposta dele não casava
/// com o cadastro nem com a etiqueta do SleekFlow. Telefone fixo (começa em 2 a 5)
/// não é tocado.
/// </remarks>
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

        // 55 + DDD (2) + 8 dígitos que começam em 6 a 9 = celular sem o nono dígito.
        if (digitos.Length == 12 && digitos[4] is >= '6' and <= '9')
            digitos = digitos[..4] + "9" + digitos[4..];

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
