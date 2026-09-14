using System.Text.RegularExpressions;

namespace EARS.Integration.Domain.ValueObjects;

/// <summary>
/// CPF normalizado (somente dígitos). É o identificador primário de paciente:
/// telefone isolado não desambigua pacientes que compartilham o mesmo número.
/// </summary>
public sealed partial record Cpf
{
    public string Value { get; }

    private Cpf(string value) => Value = value;

    public static bool TryCreate(string? entrada, out Cpf? cpf)
    {
        cpf = null;
        if (string.IsNullOrWhiteSpace(entrada)) return false;

        var digitos = SomenteDigitos().Replace(entrada, string.Empty);
        if (digitos.Length != 11) return false;
        if (digitos.Distinct().Count() == 1) return false;
        if (!DigitosVerificadoresValidos(digitos)) return false;

        cpf = new Cpf(digitos);
        return true;
    }

    public static Cpf Create(string entrada) =>
        TryCreate(entrada, out var cpf) && cpf is not null
            ? cpf
            : throw new ArgumentException($"CPF inválido: {entrada}", nameof(entrada));

    private static bool DigitosVerificadoresValidos(string d)
    {
        static int Calcular(string origem, int pesoInicial)
        {
            var soma = 0;
            for (var i = 0; i < origem.Length; i++)
                soma += (origem[i] - '0') * (pesoInicial - i);

            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        var primeiro = Calcular(d[..9], 10);
        if (primeiro != d[9] - '0') return false;

        var segundo = Calcular(d[..10], 11);
        return segundo == d[10] - '0';
    }

    /// <summary>Formato 000.000.000-00, para exibição.</summary>
    public string Formatado() =>
        $"{Value[..3]}.{Value[3..6]}.{Value[6..9]}-{Value[9..]}";

    public override string ToString() => Value;

    [GeneratedRegex(@"\D")]
    private static partial Regex SomenteDigitos();
}
