using System.Globalization;

namespace EARS.Integration.Infrastructure.Clients.Amplimed;

/// <summary>
/// Conversão das datas da Amplimed.
/// </summary>
/// <remarks>
/// A API devolve data e hora <em>sem indicação de fuso</em> e em dois formatos:
/// <c>"2023-11-14 08:00:00"</c> em listar-horarios e no histórico,
/// <c>"2025-04-11T15:00:00"</c> em listar-agendamentos. Em ambos o relógio é o
/// da clínica, então interpretar como UTC deslocaria tudo em três horas e o
/// disparo D-1 cairia no dia errado. Toda leitura passa por aqui.
///
/// Os formatos de escrita também divergem entre as duas APIs: a de Agendamento
/// espera <c>yyyy-MM-dd</c> e a de Pacientes <c>dd/MM/yyyy</c>.
/// </remarks>
public static class DataHoraAmplimed
{
    private static readonly string[] FormatosLeitura =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-dd"
    ];

    /// <summary>Formato aceito pela API de Agendamento (datas de query e de corpo).</summary>
    public static string Data(DateOnly data) => data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Formato aceito pela API de Cadastro de Pacientes.</summary>
    public static string DataBr(DateOnly data) => data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Lê <c>dd/MM/yyyy</c>, como vem de GET /pattient.</summary>
    public static DateOnly? LerDataBr(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return null;

        return DateOnly.TryParseExact(
            bruto.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data
            : null;
    }

    public static DateOnly? LerData(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return null;

        return DateOnly.TryParseExact(
            bruto.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data
            : null;
    }

    /// <summary>
    /// Interpreta o instante no fuso da clínica e devolve um
    /// <see cref="DateTimeOffset"/> com o deslocamento correto.
    /// </summary>
    public static DateTimeOffset? LerInstante(string? bruto, TimeZoneInfo fuso)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return null;

        if (!DateTime.TryParseExact(
                bruto.Trim(), FormatosLeitura, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
        {
            return null;
        }

        return ComFuso(local, fuso);
    }

    /// <summary>
    /// Casa um <see cref="DateTime"/> local com o fuso informado, tratando os
    /// dois casos de horário de verão: instante inexistente (adianta uma hora)
    /// e instante ambíguo (usa o deslocamento padrão do fuso).
    /// </summary>
    public static DateTimeOffset ComFuso(DateTime local, TimeZoneInfo fuso)
    {
        var semFuso = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (fuso.IsInvalidTime(semFuso))
            semFuso = semFuso.AddHours(1);

        return new DateTimeOffset(semFuso, fuso.GetUtcOffset(semFuso));
    }

    /// <summary>Dia local correspondente ao instante, no fuso da clínica.</summary>
    public static DateOnly DiaLocal(DateTimeOffset instante, TimeZoneInfo fuso) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instante, fuso).Date);
}
