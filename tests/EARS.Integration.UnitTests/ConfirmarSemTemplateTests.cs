using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.Options;
using EARS.Integration.Application.UseCases;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using EARS.Integration.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EARS.Integration.UnitTests;

/// <summary>
/// O paciente escreve "confirmar" sem ter recebido o nosso template, por
/// exemplo respondendo a um lembrete que a clínica mandou por fora. Com uma
/// consulta futura só, confirma; sem alvo único, vai para a recepção.
/// </summary>
public class ConfirmarSemTemplateTests
{
    private const string Telefone = "5551988887777";

    private readonly IAgendamentoRepository _agendamentos = Substitute.For<IAgendamentoRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IRelogio _relogio = Substitute.For<IRelogio>();
    private readonly IAmplimedAgendamentoClient _amplimed = Substitute.For<IAmplimedAgendamentoClient>();
    private readonly ISleekFlowClient _sleekFlow = Substitute.For<ISleekFlowClient>();
    private readonly IMensagemLogRepository _logs = Substitute.For<IMensagemLogRepository>();
    private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public ConfirmarSemTemplateTests()
    {
        _relogio.AgoraUtc.Returns(DateTimeOffset.UtcNow);
        _amplimed.AlterarStatusAsync(
                Arg.Any<string>(), Arg.Any<long>(), Arg.Any<StatusAmplimed>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private ProcessarRespostaConfirmacaoUseCase Criar()
    {
        var cancelamento = new CancelarAgendamentoUseCase(
            _agendamentos, _amplimed, _sleekFlow, _logs, _outbox, _uow,
            NullLogger<CancelarAgendamentoUseCase>.Instance);

        return new ProcessarRespostaConfirmacaoUseCase(
            _agendamentos, _conversas, _relogio, _amplimed, _sleekFlow, _logs, _outbox,
            cancelamento, Options.Create(new RegrasClinicaOptions()), _uow,
            NullLogger<ProcessarRespostaConfirmacaoUseCase>.Instance);
    }

    private static Agendamento Consulta(long id)
    {
        var paciente = new Paciente("Maria Souza", Telefone.Create(Telefone));
        var agendamento = Agendamento.DaAgenda(
            "57622", id, paciente.Id, "Dra. Inesângela",
            DateTimeOffset.UtcNow.AddDays(1), 30, StatusAgendamento.Agendado);

        typeof(Agendamento).GetProperty(nameof(Agendamento.Paciente))!.SetValue(agendamento, paciente);
        return agendamento;
    }

    private void ConsultasFuturas(params Agendamento[] consultas) =>
        _agendamentos.ListarAtivosFuturosPorTelefoneAsync(
                Telefone, Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(consultas);

    private static MensagemRecebida Texto(string texto) =>
        new(Telefone, "contato-1", texto, null, DateTimeOffset.UtcNow);

    private static MensagemRecebida Botao(string payload) =>
        new(Telefone, "contato-1", null, payload, DateTimeOffset.UtcNow);

    [Fact]
    public async Task ConfirmarComUmaConsultaFuturaConfirmaSozinho()
    {
        var consulta = Consulta(1);
        ConsultasFuturas(consulta);

        var tratada = await Criar().ExecutarAsync(Texto("confirmar"));

        tratada.Should().BeTrue();
        consulta.Status.Should().Be(StatusAgendamento.Confirmado);
        await _amplimed.Received(1).AlterarStatusAsync(
            "57622", 1, StatusAmplimed.Confirmado, Arg.Any<CancellationToken>());
        await _sleekFlow.Received(1).EnviarTextoAsync(
            Telefone, Arg.Is<string>(t => t.Contains("confirmada")), Arg.Any<CancellationToken>());
        await _sleekFlow.DidNotReceive().TransferirParaAtendimentoAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BotaoConfirmarSemTemplateTambemConfirma()
    {
        var consulta = Consulta(1);
        ConsultasFuturas(consulta);

        await Criar().ExecutarAsync(Botao("confirmar"));

        consulta.Status.Should().Be(StatusAgendamento.Confirmado);
    }

    [Fact]
    public async Task SemConsultaFuturaVaiParaARecepcao()
    {
        ConsultasFuturas();

        var tratada = await Criar().ExecutarAsync(Texto("confirmar"));

        tratada.Should().BeTrue();
        await _sleekFlow.Received(1).TransferirParaAtendimentoAsync(
            Telefone, Arg.Is<string>(c => c.Contains("CONFIRMAÇÃO")), Arg.Any<CancellationToken>());
        await _amplimed.DidNotReceive().AlterarStatusAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<StatusAmplimed>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MaisDeUmaConsultaNaoConfirmaNenhumaEVaiParaARecepcao()
    {
        var a = Consulta(1);
        var b = Consulta(2);
        ConsultasFuturas(a, b);

        var tratada = await Criar().ExecutarAsync(Texto("confirmar"));

        tratada.Should().BeTrue();
        a.Status.Should().Be(StatusAgendamento.Agendado);
        b.Status.Should().Be(StatusAgendamento.Agendado);
        await _sleekFlow.Received(1).TransferirParaAtendimentoAsync(
            Telefone, Arg.Is<string>(c => c.Contains("mais de uma")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConversaDeAgendamentoEmCursoTemPrecedencia()
    {
        // Dentro do agendamento pelo bot, "confirmar" responde ao resumo do horário.
        ConsultasFuturas(Consulta(1));
        _conversas.ObterAtivaPorTelefoneAsync(Telefone, Arg.Any<CancellationToken>())
            .Returns(new Conversa(Telefone, "contato-1", MotivoAtendimento.AgendarConsulta));

        var tratada = await Criar().ExecutarAsync(Texto("confirmar"));

        tratada.Should().BeFalse();
        await _amplimed.DidNotReceive().AlterarStatusAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<StatusAmplimed>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("sim")]
    [InlineData("1")]
    [InlineData("oi")]
    [InlineData("quero marcar uma consulta")]
    public async Task TextoAmbiguoSemTemplateNaoEConfirmacao(string texto)
    {
        ConsultasFuturas(Consulta(1));

        var tratada = await Criar().ExecutarAsync(Texto(texto));

        tratada.Should().BeFalse();
        await _sleekFlow.DidNotReceive().EnviarTextoAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
