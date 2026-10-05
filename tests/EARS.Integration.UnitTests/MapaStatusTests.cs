using EARS.Integration.Application.Dtos;
using EARS.Integration.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace EARS.Integration.UnitTests;

public class MapaStatusTests
{
    [Theory]
    [InlineData(StatusAmplimed.Confirmado, 0)]
    [InlineData(StatusAmplimed.Agendado, 1)]
    [InlineData(StatusAmplimed.Esperando, 2)]
    [InlineData(StatusAmplimed.Cancelado, 3)]
    [InlineData(StatusAmplimed.NaoCompareceu, 4)]
    [InlineData(StatusAmplimed.EmAtendimento, 5)]
    [InlineData(StatusAmplimed.Finalizado, 6)]
    [InlineData(StatusAmplimed.Reagendado, 7)]
    public void OValorDoEnumEhOStatusCodeDaDocumentacao(StatusAmplimed status, int codigo)
    {
        ((int)status).Should().Be(codigo);
    }

    [Theory]
    [InlineData("Agendado", StatusAmplimed.Agendado)]
    [InlineData("Cancelado", StatusAmplimed.Cancelado)]
    [InlineData("confirmado", StatusAmplimed.Confirmado)]
    [InlineData("Em atendimento", StatusAmplimed.EmAtendimento)]
    [InlineData("Ncompareceu", StatusAmplimed.NaoCompareceu)]
    [InlineData("Não compareceu", StatusAmplimed.NaoCompareceu)]
    [InlineData("  Reagendado  ", StatusAmplimed.Reagendado)]
    public void TextoDaListagemEhToleranteAAcentoCaixaEEspaco(string texto, StatusAmplimed esperado)
    {
        MapaStatus.DeTexto(texto).Should().Be(esperado);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Alguma coisa nova")]
    public void TextoDesconhecidoNaoViraStatusInventado(string? texto)
    {
        MapaStatus.DeTexto(texto).Should().BeNull();
    }

    [Fact]
    public void NaoConfirmadoNaoEhPropagadoParaAAmplimed()
    {
        MapaStatus.ParaAmplimed(StatusAgendamento.NaoConfirmado).Should().BeNull(
            "a Amplimed não tem esse estado; propagar mentiria para a recepção");
    }

    [Theory]
    [InlineData(StatusAgendamento.Confirmado, StatusAmplimed.Confirmado)]
    [InlineData(StatusAgendamento.Cancelado, StatusAmplimed.Cancelado)]
    [InlineData(StatusAgendamento.Remarcado, StatusAmplimed.Reagendado)]
    public void StatusLocalViraOStatusCorrespondente(StatusAgendamento local, StatusAmplimed esperado)
    {
        MapaStatus.ParaAmplimed(local).Should().Be(esperado);
    }

    [Fact]
    public void TraducaoEhSimetricaNosEstadosCompartilhados()
    {
        foreach (var status in Enum.GetValues<StatusAmplimed>())
            MapaStatus.ParaAmplimed(MapaStatus.ParaLocal(status)).Should().Be(status);
    }
}
