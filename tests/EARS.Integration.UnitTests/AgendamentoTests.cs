using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace EARS.Integration.UnitTests;

public class AgendamentoTests
{
    private static readonly DateTimeOffset Amanha = DateTimeOffset.UtcNow.AddDays(1);

    private static Agendamento DoWhatsApp() => Agendamento.DaIntegracao(
        "195fb405-40bb-40d6-9f52-a22d3d6369b9", "slot-1", Guid.NewGuid(),
        "654e42a2fb7e795963e1eb14", "Dra. Exemplo", Amanha);

    private static Agendamento DaAgenda(StatusAgendamento status = StatusAgendamento.Agendado) =>
        Agendamento.DaAgenda("19", 1690, Guid.NewGuid(), "Dr João", Amanha, 30, status);

    [Fact]
    public void NovoAgendamentoEhElegivelParaConfirmacao()
    {
        DoWhatsApp().ElegivelParaConfirmacao().Should().BeTrue();
    }

    [Fact]
    public void NaoDeveReenviarConfirmacaoJaEnviada()
    {
        var a = DoWhatsApp();
        a.MarcarConfirmacaoEnviada();
        a.ElegivelParaConfirmacao().Should().BeFalse();
    }

    [Fact]
    public void LembreteSoValeAposConfirmacaoSemResposta()
    {
        var a = DoWhatsApp();
        a.ElegivelParaLembrete().Should().BeFalse();

        a.MarcarConfirmacaoEnviada();
        a.ElegivelParaLembrete().Should().BeTrue();

        a.Confirmar();
        a.ElegivelParaLembrete().Should().BeFalse();
    }

    [Fact]
    public void ConfirmarAlteraStatusERegistraResposta()
    {
        var a = DoWhatsApp();
        a.Confirmar();

        a.Status.Should().Be(StatusAgendamento.Confirmado);
        a.RespondidoEm.Should().NotBeNull();
    }

    [Fact]
    public void NaoConfirmadoSoSeAindaAgendado()
    {
        var a = DoWhatsApp();
        a.Cancelar();
        a.MarcarNaoConfirmado();

        a.Status.Should().Be(StatusAgendamento.Cancelado);
    }

    // --- Os dois espaços de identificador (ADR 0003) ------------------------

    [Fact]
    public void AgendamentoDoWhatsAppNasceSemOParDaAgenda()
    {
        var a = DoWhatsApp();

        a.PodeCancelarNaAmplimed().Should().BeTrue("cancelar-agendamento aceita o UUID da integração");
        a.PodeAlterarStatusNaAmplimed().Should().BeFalse("o par (usuclin, idAgendamento) só vem do polling");
    }

    [Fact]
    public void AgendamentoDaAgendaPodeTerStatusAlteradoMasNaoCancelado()
    {
        var a = DaAgenda();

        a.PodeAlterarStatusNaAmplimed().Should().BeTrue();
        a.PodeCancelarNaAmplimed().Should().BeFalse("cancelar-agendamento só vale para o criado via integração");
    }

    [Fact]
    public void VincularAgendaHabilitaAAlteracaoDeStatus()
    {
        var a = DoWhatsApp();
        a.VincularAgenda("19", 1690);

        a.PodeAlterarStatusNaAmplimed().Should().BeTrue();
        a.IdAgendamentoAmplimed.Should().Be(1690);
    }

    // --- Conciliação com a agenda ------------------------------------------

    [Fact]
    public void ConciliacaoAtualizaStatusEHorarioQuandoAindaNaoHouveResposta()
    {
        var a = DaAgenda();
        var novoHorario = Amanha.AddHours(2);

        a.ConciliarComAgenda(StatusAgendamento.Confirmado, novoHorario, 45);

        a.Status.Should().Be(StatusAgendamento.Confirmado);
        a.InicioEm.Should().Be(novoHorario);
        a.DuracaoMinutos.Should().Be(45);
        a.SincronizadoEm.Should().NotBeNull();
    }

    [Fact]
    public void RespostaDoPacienteNaoEhSobrescritaPelaAgenda()
    {
        var a = DaAgenda();
        a.Confirmar();

        a.ConciliarComAgenda(StatusAgendamento.Agendado, Amanha, 30);

        a.Status.Should().Be(StatusAgendamento.Confirmado,
            "a listagem pode estar atrasada; quem respondeu foi o paciente");
    }

    [Fact]
    public void NaoConfirmadoSobreviveAConciliacao()
    {
        var a = DaAgenda();
        a.MarcarConfirmacaoEnviada();
        a.MarcarLembreteEnviado();
        a.MarcarNaoConfirmado();

        a.ConciliarComAgenda(StatusAgendamento.Agendado, Amanha, 30);

        a.Status.Should().Be(StatusAgendamento.NaoConfirmado,
            "a Amplimed segue mostrando Agendado, mas o silêncio do paciente é informação nossa");
    }
}
