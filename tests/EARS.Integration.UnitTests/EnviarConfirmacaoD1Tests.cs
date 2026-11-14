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
/// O disparo manual existe para testar sem atingir a agenda inteira. Estes
/// testes seguram as três garantias que isso exige: o filtro de telefone vale,
/// a simulação não envia nada e o reenvio só acontece quando pedido.
/// </summary>
public class EnviarConfirmacaoD1Tests
{
    private static readonly DateOnly Dia = new(2026, 11, 15);

    private readonly IAgendamentoRepository _agendamentos = Substitute.For<IAgendamentoRepository>();
    private readonly ISleekFlowClient _sleekFlow = Substitute.For<ISleekFlowClient>();
    private readonly IOptOutRepository _optOut = Substitute.For<IOptOutRepository>();
    private readonly IMensagemLogRepository _logs = Substitute.For<IMensagemLogRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IRelogio _relogio = Substitute.For<IRelogio>();

    public EnviarConfirmacaoD1Tests()
    {
        _relogio.HojeLocal.Returns(Dia.AddDays(-1));
        _sleekFlow.EnviarTemplateAsync(Arg.Any<EnvioTemplateRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResultadoEnvio.Ok("msg-1"));
        _sleekFlow.MarcarAgendamentoEmCursoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private EnviarConfirmacaoD1UseCase Criar() => new(
        _agendamentos,
        null!, // só usado com LeituraAgendaViaApi, que aqui fica desligada
        _sleekFlow, _optOut, _logs, _uow, _relogio,
        Options.Create(new AutomacaoOptions { LeituraAgendaViaApi = false }),
        NullLogger<EnviarConfirmacaoD1UseCase>.Instance);

    private static Agendamento Consulta(string nome, string telefone, long id, bool jaConfirmada = false)
    {
        var paciente = new Paciente(nome, Telefone.Create(telefone));
        var agendamento = Agendamento.DaAgenda(
            "57622", id, paciente.Id, "Dra. Inesângela",
            new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero), 30, StatusAgendamento.Agendado);

        typeof(Agendamento).GetProperty(nameof(Agendamento.Paciente))!.SetValue(agendamento, paciente);
        if (jaConfirmada) agendamento.MarcarConfirmacaoEnviada();
        return agendamento;
    }

    private void AgendaDoDia(params Agendamento[] consultas) =>
        _agendamentos.ListarPorDiaAsync(Dia, Arg.Any<CancellationToken>()).Returns(consultas);

    private static HashSet<string> So(string telefone) => [Telefone.Create(telefone).Value];

    [Fact]
    public async Task FiltroDeTelefoneEnviaSoParaQuemFoiPedido()
    {
        var eric = Consulta("Eric Serra", "5516997598594", 1);
        var outro = Consulta("Maria Souza", "5551988887777", 2);
        AgendaDoDia(eric, outro);

        var resultado = await Criar().ExecutarParaDiaAsync(
            Dia, new OpcoesDisparo(So("5516997598594")));

        resultado.Enviados.Should().Be(1);
        resultado.Itens.Should().ContainSingle().Which.Situacao.Should().Be("enviado");
        await _sleekFlow.Received(1).EnviarTemplateAsync(
            Arg.Is<EnvioTemplateRequest>(r => r.Telefone == eric.Paciente!.Telefone),
            Arg.Any<CancellationToken>());
        outro.ConfirmacaoEnviadaEm.Should().BeNull();
    }

    [Fact]
    public async Task SimulacaoRelataMasNaoEnviaNemMarca()
    {
        var consulta = Consulta("Eric Serra", "5516997598594", 1);
        AgendaDoDia(consulta);

        var resultado = await Criar().ExecutarParaDiaAsync(
            Dia, new OpcoesDisparo(So("5516997598594"), Simular: true));

        resultado.Simulacao.Should().BeTrue();
        resultado.Enviados.Should().Be(0);
        resultado.Itens.Should().ContainSingle().Which.Situacao.Should().StartWith("simulado");
        consulta.ConfirmacaoEnviadaEm.Should().BeNull();
        await _sleekFlow.DidNotReceive().EnviarTemplateAsync(
            Arg.Any<EnvioTemplateRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuemJaRecebeuSoRecebeDeNovoComReenviar()
    {
        var consulta = Consulta("Eric Serra", "5516997598594", 1, jaConfirmada: true);
        AgendaDoDia(consulta);
        var filtro = So("5516997598594");

        var semReenvio = await Criar().ExecutarParaDiaAsync(Dia, new OpcoesDisparo(filtro));
        semReenvio.Enviados.Should().Be(0);
        semReenvio.Itens.Should().ContainSingle().Which.Situacao.Should().Contain("reenviar=true");

        var comReenvio = await Criar().ExecutarParaDiaAsync(Dia, new OpcoesDisparo(filtro, Reenviar: true));
        comReenvio.Enviados.Should().Be(1);
    }

    [Fact]
    public async Task EnvioBemSucedidoAplicaAEtiquetaDeAgendamentoEmCurso()
    {
        var consulta = Consulta("Eric Serra", "5516997598594", 1);
        AgendaDoDia(consulta);

        await Criar().ExecutarParaDiaAsync(Dia, new OpcoesDisparo(So("5516997598594")));

        consulta.EtiquetaConfirmacaoAplicada.Should().BeTrue();
        await _sleekFlow.Received(1).MarcarAgendamentoEmCursoAsync(
            consulta.Paciente!.Telefone, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalhaNoEnvioNaoMarcaNemEtiqueta()
    {
        var consulta = Consulta("Eric Serra", "5516997598594", 1);
        AgendaDoDia(consulta);
        _sleekFlow.EnviarTemplateAsync(Arg.Any<EnvioTemplateRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResultadoEnvio.Falha("canal não encontrado"));

        var resultado = await Criar().ExecutarParaDiaAsync(Dia, new OpcoesDisparo());

        resultado.Enviados.Should().Be(0);
        resultado.Itens.Should().ContainSingle().Which.Situacao.Should().Contain("canal não encontrado");
        consulta.ConfirmacaoEnviadaEm.Should().BeNull();
        consulta.EtiquetaConfirmacaoAplicada.Should().BeFalse();
        await _sleekFlow.DidNotReceive().MarcarAgendamentoEmCursoAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DescadastradoNuncaRecebe()
    {
        var consulta = Consulta("Eric Serra", "5516997598594", 1);
        AgendaDoDia(consulta);
        _optOut.EstaDescadastradoAsync(consulta.Paciente!.Telefone, Arg.Any<CancellationToken>()).Returns(true);

        var resultado = await Criar().ExecutarParaDiaAsync(Dia, new OpcoesDisparo(Reenviar: true));

        resultado.Enviados.Should().Be(0);
        resultado.Itens.Should().ContainSingle().Which.Situacao.Should().Contain("descadastrado");
    }
}
