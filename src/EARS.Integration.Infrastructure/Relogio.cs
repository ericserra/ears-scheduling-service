using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Options;
using EARS.Integration.Infrastructure.Clients.Amplimed;
using Microsoft.Extensions.Options;
using TimeZoneConverter;

namespace EARS.Integration.Infrastructure;

public sealed class Relogio(IOptions<AutomacaoOptions> options) : IRelogio
{
    private readonly TimeZoneInfo _fuso = TZConvert.GetTimeZoneInfo(options.Value.FusoHorario);

    public DateTimeOffset AgoraUtc => DateTimeOffset.UtcNow;

    /// <summary>
    /// Para exibir e para comparar em memória. Carrega o deslocamento da
    /// clínica, então NÃO serve como parâmetro de consulta ao Postgres: use
    /// <see cref="AgoraUtc"/> nesse caso.
    /// </summary>
    public DateTimeOffset AgoraLocal => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _fuso);

    public DateOnly HojeLocal => DateOnly.FromDateTime(AgoraLocal.Date);

    /// <remarks>
    /// Devolvido em UTC de propósito. O instante é calculado no fuso da
    /// clínica, que é o que importa para a borda do dia, mas o Npgsql recusa
    /// DateTimeOffset com deslocamento diferente de zero ao comparar com
    /// timestamptz: "only offset 0 (UTC) is supported". Como DateTimeOffset
    /// identifica um instante, converter para UTC não muda o resultado da
    /// consulta, só a representação.
    /// </remarks>
    public DateTimeOffset InicioDoDiaLocal(DateOnly dia) =>
        DataHoraAmplimed.ComFuso(dia.ToDateTime(TimeOnly.MinValue), _fuso).ToUniversalTime();
}
