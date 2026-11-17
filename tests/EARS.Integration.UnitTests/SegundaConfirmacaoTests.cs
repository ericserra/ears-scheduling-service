using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.UseCases;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using EARS.Integration.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EARS.Integration.UnitTests;

/// <summary>
/// Failsafe contra clique errado: o botão do template (Confirmar, Cancelar,
/// Remarcar) só pergunta "Você deseja ...?". Quem age é o Sim; o Não volta à
/// pergunta inicial.
/// </summary>
public class SegundaConfirmacaoTests
{
    private const string Fone = "5551988887777";

    private readonly IAgendamentoRepository _agendamentos = Substitute.For<IAgendamentoRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IRelogio _relogio = Substitute.For<IRelogio>();
    private readonly IAmplimedAgendamentoClient _amplimed = Substitute.For<IAmplimedAgendamentoClient>();
    private readonly ISleekFlowClient _sleekFlow = Substitute.For<ISleekFlowClient>();
    private readonly IMensagemLogRepository _logs = Substitute.For<IMensagemLogRepository>();
    private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly DateTimeOffset _t0 = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private readonly Agendamento _consulta;

    public SegundaConfirmacaoTests()
    {
        _relogio.AgoraUtc.Returns(_t0);
        _relogio.ParaLocal(Arg.Any<DateTimeOffset>()).Returns(c => c.Arg<DateTimeOffset>().ToOffset(TimeSpan.FromHours(-3)));
        _amplimed.AlterarStatusAsync(
                Arg.Any<string>(), Arg.Any<long>(), Arg.Any<StatusAmplimed>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _sleekFlow.EnviarBotoesAsync(Arg.Any<EnvioBotoesRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResultadoEnvio.Ok("m-1"));

        var paciente = new Paciente("Maria Souza", Telefone.Create(Fone));
        _consulta = Agendamento.DaAgenda(
            "57622", 1, paciente.Id, "Dra. Inesângela",
            new DateTimeOffset(2026, 11, 20, 12, 30, 0, TimeSpan.Zero), 30, StatusAgendamento.Agendado);
        typeof(Agendamento).GetProperty(nameof(Agendamento.Paciente))!.SetValue(_consulta, paciente);
        _consulta.MarcarConfirmacaoEnviada();
        _consulta.MarcarEtiquetaConfirmacaoAplicada();

        _agendamentos.ObterAtivoMaisRecentePorTelefoneAsync(Fone, Arg.Any<CancellationToken>()).Returns(_consulta);
    }

    private ProcessarRespostaConfirmacaoUseCase Criar()
    {
        var cancelamento = new CancelarAgendamentoUseCase(
            _agendamentos, _amplimed, _sleekFlow, _logs, _outbox, _uow,
            NullLogger<CancelarAgendamentoUseCase>.Instance);

        return new ProcessarRespostaConfirmacaoUseCase(
            _agendamentos, _conversas, _relogio, _amplimed, _sleekFlow, _logs, _outbox,
            cancelamento, _uow, NullLogger<ProcessarRespostaConfirmacaoUseCase>.Instance);
    }

    private static MensagemRecebida Botao(string payload) => new(Fone, "c-1", null, payload, DateTimeOffset.UtcNow);
    private static MensagemRecebida Texto(string texto) => new(Fone, "c-1", texto, null, DateTimeOffset.UtcNow);

    private async Task NaoAgiu()
    {
        _consulta.Status.Should().Be(StatusAgendamento.Agendado);
        await _amplimed.DidNotReceive().AlterarStatusAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<StatusAmplimed>(), Arg.Any<CancellationToken>());
        await _amplimed.DidNotReceive().CancelarAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _sleekFlow.DidNotReceive().TransferirParaAtendimentoAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("confirmar", "confirmar", "Você deseja confirmar a sua consulta de 20/11 às 09:30?")]
    [InlineData("cancelar", "cancelar", "Você deseja cancelar a sua consulta de 20/11 às 09:30?")]
    [InlineData("remarcar", "remarcar", "Você deseja reagendar a sua consulta de 20/11 às 09:30?")]
    public async Task BotaoDoTemplateSoPerguntaENaoAge(string payload, string acao, string pergunta)
    {
        var tratada = await Criar().ExecutarAsync(Botao(payload));

        tratada.Should().BeTrue();
        _consulta.AcaoPendenteConfirmacao.Should().Be(acao);
        await _sleekFlow.Received(1).EnviarBotoesAsync(
            Arg.Is<EnvioBotoesRequest>(r =>
                r.Telefone == Fone
                && r.Texto == pergunta
                && r.Botoes.Select(b => b.Id).SequenceEqual(new[] { "acao_sim", "acao_nao" })),
            Arg.Any<CancellationToken>());
        await NaoAgiu();
    }

    [Fact]
    public async Task SimDepoisDeConfirmarConfirmaDeVerdade()
    {
        var uso = Criar();
        await uso.ExecutarAsync(Botao("confirmar"));

        await uso.ExecutarAsync(Botao("acao_sim"));

        _consulta.Status.Should().Be(StatusAgendamento.Confirmado);
        _consulta.AcaoPendenteConfirmacao.Should().BeNull();
        await _amplimed.Received(1).AlterarStatusAsync(
            "57622", 1, StatusAmplimed.Confirmado, Arg.Any<CancellationToken>());
        await _sleekFlow.Received(1).EnviarTextoAsync(
            Fone, Arg.Is<string>(t => t.Contains("confirmada")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SimDepoisDeRemarcarEncaminhaParaARecepcao()
    {
        var uso = Criar();
        await uso.ExecutarAsync(Botao("remarcar"));

        await uso.ExecutarAsync(Texto("Sim"));

        _consulta.Status.Should().Be(StatusAgendamento.Remarcado);
        await _sleekFlow.Received(1).TransferirParaAtendimentoAsync(
            Fone, Arg.Is<string>(c => c.Contains("REMARCAÇÃO") && c.Contains("20/11 09:30")),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("acao_nao", true)]
    [InlineData("Não", false)]
    [InlineData("nao", false)]
    public async Task NaoVoltaParaAPerguntaInicialSemAgir(string resposta, bool comoBotao)
    {
        var uso = Criar();
        await uso.ExecutarAsync(Botao("cancelar"));

        await uso.ExecutarAsync(comoBotao ? Botao(resposta) : Texto(resposta));

        _consulta.AcaoPendenteConfirmacao.Should().BeNull();
        await _sleekFlow.Received(1).EnviarBotoesAsync(
            Arg.Is<EnvioBotoesRequest>(r =>
                r.Texto.StartsWith("Sem problemas!")
                && r.Botoes.Select(b => b.Id).SequenceEqual(new[] { "confirmar", "cancelar", "remarcar" })
                && r.Botoes.Select(b => b.Titulo).SequenceEqual(new[] { "Confirmar", "Cancelar", "Reagendar" })),
            Arg.Any<CancellationToken>());
        await NaoAgiu();
    }

    [Fact]
    public async Task TrocarDeIdeiaAntesDoSimTrocaAPergunta()
    {
        var uso = Criar();
        await uso.ExecutarAsync(Botao("cancelar"));

        await uso.ExecutarAsync(Botao("confirmar"));

        _consulta.AcaoPendenteConfirmacao.Should().Be("confirmar");
        await NaoAgiu();
    }

    [Fact]
    public async Task SimDeUmaPerguntaVencidaNaoExecutaNada()
    {
        var uso = Criar();
        await uso.ExecutarAsync(Botao("cancelar"));

        _relogio.AgoraUtc.Returns(_t0.AddHours(7));
        await uso.ExecutarAsync(Botao("acao_sim"));

        _consulta.AcaoPendenteConfirmacao.Should().BeNull();
        await NaoAgiu();
        await _sleekFlow.Received(1).EnviarBotoesAsync(
            Arg.Is<EnvioBotoesRequest>(r => r.Texto.StartsWith("Sem problemas!")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SimSoltoSemPerguntaPendenteApenasPergunta()
    {
        await Criar().ExecutarAsync(Texto("sim"));

        _consulta.AcaoPendenteConfirmacao.Should().Be("confirmar");
        await NaoAgiu();
    }

    [Fact]
    public async Task SeOsBotoesFalharemAPerguntaVaiComoTexto()
    {
        _sleekFlow.EnviarBotoesAsync(Arg.Any<EnvioBotoesRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResultadoEnvio.Falha("fora da janela"));

        await Criar().ExecutarAsync(Botao("confirmar"));

        await _sleekFlow.Received(1).EnviarTextoAsync(
            Fone, Arg.Is<string>(t => t.Contains("Você deseja confirmar") && t.Contains("SIM ou NÃO")),
            Arg.Any<CancellationToken>());
        _consulta.AcaoPendenteConfirmacao.Should().Be("confirmar");
        await NaoAgiu();
    }

    [Fact]
    public async Task TextoQueNaoEntendemosContinuaIndoParaAtendimentoHumano()
    {
        await Criar().ExecutarAsync(Texto("preciso falar com alguém sobre o valor"));

        await _sleekFlow.Received(1).TransferirParaAtendimentoAsync(
            Fone, Arg.Is<string>(c => c.Contains("Resposta não compreendida")), Arg.Any<CancellationToken>());
        _consulta.AcaoPendenteConfirmacao.Should().BeNull();
    }
}
