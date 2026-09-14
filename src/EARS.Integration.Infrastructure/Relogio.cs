using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Options;
using Microsoft.Extensions.Options;
using TimeZoneConverter;

namespace EARS.Integration.Infrastructure;

public sealed class Relogio(IOptions<AutomacaoOptions> options) : IRelogio
{
    private readonly TimeZoneInfo _fuso = TZConvert.GetTimeZoneInfo(options.Value.FusoHorario);

    public DateTimeOffset AgoraUtc => DateTimeOffset.UtcNow;

    public DateTimeOffset AgoraLocal => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _fuso);

    public DateOnly HojeLocal => DateOnly.FromDateTime(AgoraLocal.Date);
}
