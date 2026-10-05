using EARS.Integration.Infrastructure.Clients.Amplimed;
using FluentAssertions;
using TimeZoneConverter;
using Xunit;

namespace EARS.Integration.UnitTests;

public class DataHoraAmplimedTests
{
    private static readonly TimeZoneInfo SaoPaulo = TZConvert.GetTimeZoneInfo("America/Sao_Paulo");

    [Theory]
    [InlineData("2023-11-14 08:00:00", 8)]
    [InlineData("2025-04-11T15:00:00", 15)]
    public void OsDoisFormatosDaApiSaoLidosComoHoraLocalDaClinica(string bruto, int horaEsperada)
    {
        var instante = DataHoraAmplimed.LerInstante(bruto, SaoPaulo);

        instante.Should().NotBeNull();
        instante!.Value.Hour.Should().Be(horaEsperada,
            "interpretar como UTC deslocaria o disparo em três horas");
        instante.Value.Offset.Should().Be(TimeSpan.FromHours(-3));
    }

    [Fact]
    public void DataIlegivelDevolveNuloEmVezDeLancar()
    {
        DataHoraAmplimed.LerInstante("amanhã de manhã", SaoPaulo).Should().BeNull();
        DataHoraAmplimed.LerInstante(null, SaoPaulo).Should().BeNull();
    }

    [Fact]
    public void OsFormatosDeEscritaDiferemEntreAsDuasApis()
    {
        var data = new DateOnly(2000, 11, 5);

        DataHoraAmplimed.Data(data).Should().Be("2000-11-05", "API de Agendamento");
        DataHoraAmplimed.DataBr(data).Should().Be("05/11/2000", "API de Cadastro de Pacientes");
    }

    [Fact]
    public void LeituraDaDataDeNascimentoDaApiDePacientes()
    {
        DataHoraAmplimed.LerDataBr("01/01/1998").Should().Be(new DateOnly(1998, 1, 1));
        DataHoraAmplimed.LerDataBr("").Should().BeNull();
        DataHoraAmplimed.LerDataBr("1998-01-01").Should().BeNull("aqui o formato é dd/MM/yyyy");
    }

    [Fact]
    public void MeiaNoiteNoDiaDaConsultaMantemODiaLocal()
    {
        var instante = DataHoraAmplimed.LerInstante("2025-04-11T00:30:00", SaoPaulo);

        DataHoraAmplimed.DiaLocal(instante!.Value, SaoPaulo).Should().Be(new DateOnly(2025, 4, 11),
            "converter para UTC jogaria a consulta para o dia seguinte");
    }
}
