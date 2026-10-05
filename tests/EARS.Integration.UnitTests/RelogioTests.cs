using EARS.Integration.Application.Options;
using EARS.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EARS.Integration.UnitTests;

public class RelogioTests
{
    private static readonly Relogio Relogio =
        new(Options.Create(new AutomacaoOptions { FusoHorario = "America/Sao_Paulo" }));

    [Fact]
    public void ODiaComecaNaMeiaNoiteDaClinicaNaoEmUtc()
    {
        var inicio = Relogio.InicioDoDiaLocal(new DateOnly(2025, 4, 11));

        inicio.Offset.Should().Be(TimeSpan.FromHours(-3));
        inicio.Hour.Should().Be(0);
        inicio.UtcDateTime.Should().Be(new DateTime(2025, 4, 11, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AJanelaDoDiaCobreUmaConsultaDas21h()
    {
        var dia = new DateOnly(2025, 4, 11);
        var inicio = Relogio.InicioDoDiaLocal(dia);
        var fim = Relogio.InicioDoDiaLocal(dia.AddDays(1));

        var consultaDas21 = new DateTimeOffset(2025, 4, 11, 21, 0, 0, TimeSpan.FromHours(-3));
        var consultaDaNoiteAnterior = new DateTimeOffset(2025, 4, 10, 22, 0, 0, TimeSpan.FromHours(-3));

        (consultaDas21 >= inicio && consultaDas21 < fim).Should().BeTrue();
        (consultaDaNoiteAnterior >= inicio).Should().BeFalse();
    }
}
