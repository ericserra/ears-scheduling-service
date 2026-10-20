using EARS.Integration.Application.Options;
using FluentAssertions;
using Xunit;

namespace EARS.Integration.UnitTests;

/// <summary>
/// As regras vêm do handoff da SleekFlow (20/10/2026) e são decisões da
/// clínica. Os testes travam o comportamento, não os valores: a lista de
/// médicas e de convênios mora em configuração e muda sem deploy.
/// </summary>
public class RegrasClinicaTests
{
    private static readonly RegrasClinicaOptions Regras = new();

    [Theory]
    [InlineData("Dra. Leticia Rosito", 4, true)]
    [InlineData("Caroline Elias", 7, true)]
    [InlineData("Dra Renata Tramontin", 7, false)]
    [InlineData("Dra Renata Tramontin", 12, true)]
    public void GradeEtariaSegueAListaDaClinica(string profissional, int idade, bool esperado)
    {
        Regras.AtendeIdade(profissional, idade).Should().Be(esperado);
    }

    [Fact]
    public void ProfissionalDesconhecidoNaoAtendeCriancaPorPadrao()
    {
        Regras.AtendeIdade("Dra. Alguém Que Não Está Na Lista", 6).Should().BeFalse(
            "na dúvida sobre a grade, não ofertar para criança é o lado seguro do erro");

        Regras.AtendeIdade("Dra. Alguém Que Não Está Na Lista", 30).Should().BeTrue();
    }

    [Fact]
    public void IdadeDesconhecidaNaoEliminaNinguem()
    {
        Regras.AtendeIdade("Dra Renata Tramontin", null).Should().BeTrue(
            "sem idade informada não dá para aplicar a grade; filtrar seria inventar regra");
    }

    [Theory]
    [InlineData("IPE")]
    [InlineData("ipe")]
    [InlineData("Unimed Unifácil")]
    [InlineData("unimed unifacil")]
    public void TriagemHumanaEhToleranteAAcentoECaixa(string convenio)
    {
        Regras.ExigeTriagemHumana(convenio).Should().BeTrue(
            "o paciente digita como quiser, e essa regra decide se ele vê horários");
    }

    [Fact]
    public void ConvenioComumNaoCaiEmTriagemHumana()
    {
        Regras.ExigeTriagemHumana("Unimed").Should().BeFalse();
        Regras.ExigeTriagemHumana(null).Should().BeFalse();
    }

    [Fact]
    public void SaudePasEhSomenteConsulta()
    {
        Regras.SomenteConsulta("Saúde PAS").Should().BeTrue();
        Regras.SomenteConsulta("saude pas").Should().BeTrue();
    }

    [Fact]
    public void ListaDeAceitosVaziaNaoEmpurraNinguemParaParticular()
    {
        Regras.ConvenioAceito("Qualquer Convênio").Should().BeTrue(
            "lista vazia significa que não sabemos quais são os aceitos");
    }

    [Fact]
    public void ListaDeAceitosPreenchidaFiltra()
    {
        var regras = new RegrasClinicaOptions { ConveniosAceitos = ["Unimed", "Bradesco Saúde"] };

        regras.ConvenioAceito("unimed").Should().BeTrue();
        regras.ConvenioAceito("Amil").Should().BeFalse();
    }

    [Theory]
    [InlineData("acho que tive uma perda súbita de audição ontem")]
    [InlineData("PERDI A AUDICAO do lado direito")]
    [InlineData("parei de ouvir de repente")]
    public void UrgenciaEhDetectadaNoMeioDaFrase(string texto)
    {
        Regras.MencionaUrgencia(texto).Should().BeTrue();
    }

    [Fact]
    public void ConversaNormalNaoViraUrgencia()
    {
        Regras.MencionaUrgencia("quero marcar uma consulta de rotina").Should().BeFalse();
    }

    [Theory]
    [InlineData("quero falar com um ATENDENTE")]
    [InlineData("me passa para a recepção")]
    [InlineData("posso falar com uma pessoa?")]
    public void PedidoDeAtendenteEhDetectado(string texto)
    {
        Regras.PedeAtendente(texto).Should().BeTrue();
    }

    [Fact]
    public void FraseDeTransferenciaEhAAcordadaComAClinica()
    {
        Regras.FraseTransferencia.Should().Be(
            "Vou encaminhar seu atendimento para a nossa equipe, que continua com você por aqui.");
    }

    [Fact]
    public void RespostaDeCancelamentoNaoMencionaTaxaNemMulta()
    {
        var resposta = RegrasClinicaOptions.Normalizar(Regras.RespostaCancelamento);

        resposta.Should().NotContain("multa");
        resposta.Should().NotContain("taxa");
        resposta.Should().NotContain("cobran");
    }
}
