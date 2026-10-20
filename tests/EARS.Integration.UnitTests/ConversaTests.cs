using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace EARS.Integration.UnitTests;

public class ConversaTests
{
    private static Conversa Nova(MotivoAtendimento motivo = MotivoAtendimento.AgendarConsulta) =>
        new("5551988887777", "contato-1", motivo, "conversa-1");

    [Fact]
    public void ConversaNasceSemEtiquetaAplicada()
    {
        Nova().EtiquetaAplicada.Should().BeFalse(
            "a etiqueta só existe depois que o SleekFlow confirma que a aplicou");
    }

    [Fact]
    public void MotivoVemDoRamoDoMenuENaoMudaDepois()
    {
        Nova(MotivoAtendimento.Cancelar).Motivo.Should().Be(MotivoAtendimento.Cancelar);
    }

    [Theory]
    [InlineData(EstadoConversa.Concluida)]
    [InlineData(EstadoConversa.TransferidaParaHumano)]
    [InlineData(EstadoConversa.Expirada)]
    public void EstadosFinaisSaoReconhecidos(EstadoConversa estado)
    {
        var conversa = Nova();
        conversa.TransicionarPara(estado);
        conversa.EmEstadoFinal().Should().BeTrue();
    }

    [Fact]
    public void EstadoDeColetaNaoEhFinal()
    {
        var conversa = Nova();
        conversa.TransicionarPara(EstadoConversa.AguardandoConvenio);
        conversa.EmEstadoFinal().Should().BeFalse();
    }

    [Fact]
    public void ADuracaoDaInatividadeEhMedidaDaUltimaInteracao()
    {
        var conversa = Nova();

        conversa.Expirada(TimeSpan.FromHours(6)).Should().BeFalse();
        conversa.Expirada(TimeSpan.FromTicks(1)).Should().BeTrue();
    }

    [Fact]
    public void ColetaSegueAOrdemDaClinicaESobreviveAReinicio()
    {
        var conversa = Nova();

        conversa.RegistrarParaQuem(false);
        conversa.RegistrarIdade(7);
        conversa.RegistrarConvenio("Particular");
        conversa.RegistrarPreferencia("terça de manhã");
        conversa.RegistrarNome("Joana da Silva");
        conversa.RegistrarDataNascimento(new DateOnly(2019, 3, 1));

        conversa.PacienteEhOContato.Should().BeFalse();
        conversa.IdadePaciente.Should().Be(7);
        conversa.Convenio.Should().Be("Particular");
        conversa.PreferenciaDiaTurno.Should().Be("terça de manhã");
        conversa.NomeInformado.Should().Be("Joana da Silva");
        conversa.DataNascimentoInformada.Should().Be(new DateOnly(2019, 3, 1));
    }
}
