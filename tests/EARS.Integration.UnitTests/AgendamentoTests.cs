using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace EARS.Integration.UnitTests;

public class AgendamentoTests
{
    private static Agendamento Novo() => new(
        "AMP-1", Guid.NewGuid(), "PROF-1", "Dra. Exemplo",
        DateTimeOffset.UtcNow.AddDays(1), OrigemAgendamento.WhatsApp);

    [Fact]
    public void NovoAgendamentoEhElegivelParaConfirmacao()
    {
        Novo().ElegivelParaConfirmacao().Should().BeTrue();
    }

    [Fact]
    public void NaoDeveReenviarConfirmacaoJaEnviada()
    {
        var a = Novo();
        a.MarcarConfirmacaoEnviada();
        a.ElegivelParaConfirmacao().Should().BeFalse();
    }

    [Fact]
    public void LembreteSoValeAposConfirmacaoSemResposta()
    {
        var a = Novo();
        a.ElegivelParaLembrete().Should().BeFalse();

        a.MarcarConfirmacaoEnviada();
        a.ElegivelParaLembrete().Should().BeTrue();

        a.Confirmar();
        a.ElegivelParaLembrete().Should().BeFalse();
    }

    [Fact]
    public void ConfirmarAlteraStatusERegistraResposta()
    {
        var a = Novo();
        a.Confirmar();

        a.Status.Should().Be(StatusAgendamento.Confirmado);
        a.RespondidoEm.Should().NotBeNull();
    }

    [Fact]
    public void NaoConfirmadoSoSeAindaAgendado()
    {
        var a = Novo();
        a.Cancelar();
        a.MarcarNaoConfirmado();

        a.Status.Should().Be(StatusAgendamento.Cancelado);
    }
}
