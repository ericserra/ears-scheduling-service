using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Options;
using EARS.Integration.Application.UseCases;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EARS.Integration.UnitTests;

/// <summary>
/// A expiração é o que devolve o menu a quem desistiu no meio do agendamento.
/// Sem ela o paciente fica com a etiqueta agendamento-em-curso para sempre,
/// escreve no WhatsApp e nada responde — sem erro em lugar nenhum.
/// </summary>
public class ExpirarConversasTests
{
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ISleekFlowClient _sleekFlow = Substitute.For<ISleekFlowClient>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ExpirarConversasUseCase Criar() => new(
        _conversas, _sleekFlow, _uow,
        Options.Create(new AutomacaoOptions()),
        NullLogger<ExpirarConversasUseCase>.Instance);

    private static Conversa ConversaEmCurso(bool comEtiqueta = true)
    {
        var conversa = new Conversa("5551988887777", "contato-1", MotivoAtendimento.AgendarConsulta);
        conversa.TransicionarPara(EstadoConversa.AguardandoConvenio);
        if (comEtiqueta) conversa.MarcarEtiquetaAplicada();
        return conversa;
    }

    [Fact]
    public async Task ConversaParadaEhExpiradaEPerdeAEtiqueta()
    {
        var conversa = ConversaEmCurso();
        _conversas.ListarParadasAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([conversa]);
        _sleekFlow.LimparAgendamentoEmCursoAsync(conversa.Telefone, Arg.Any<CancellationToken>()).Returns(true);

        var total = await Criar().ExecutarAsync();

        total.Should().Be(1);
        conversa.Estado.Should().Be(EstadoConversa.Expirada);
        conversa.EtiquetaAplicada.Should().BeFalse();
        await _sleekFlow.Received(1).LimparAgendamentoEmCursoAsync(conversa.Telefone, Arg.Any<CancellationToken>());
        await _uow.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalhaAoRemoverEtiquetaMantemAMarcaParaTentarDeNovo()
    {
        var conversa = ConversaEmCurso();
        _conversas.ListarParadasAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([conversa]);
        _sleekFlow.LimparAgendamentoEmCursoAsync(conversa.Telefone, Arg.Any<CancellationToken>()).Returns(false);

        await Criar().ExecutarAsync();

        conversa.Estado.Should().Be(EstadoConversa.Expirada);
        conversa.EtiquetaAplicada.Should().BeTrue(
            "desistir aqui deixaria o paciente sem menu; a próxima execução tenta de novo");
    }

    [Fact]
    public async Task ConversaSemEtiquetaNaoGeraChamadaAoSleekFlow()
    {
        var conversa = ConversaEmCurso(comEtiqueta: false);
        _conversas.ListarParadasAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([conversa]);

        await Criar().ExecutarAsync();

        conversa.Estado.Should().Be(EstadoConversa.Expirada);
        await _sleekFlow.DidNotReceive()
            .LimparAgendamentoEmCursoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NadaParadoNaoSalvaNadaNemChamaOSleekFlow()
    {
        _conversas.ListarParadasAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        (await Criar().ExecutarAsync()).Should().Be(0);

        await _uow.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OLimiteConsultadoRespeitaAJanelaConfigurada()
    {
        _conversas.ListarParadasAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var antes = DateTimeOffset.UtcNow;
        await Criar().ExecutarAsync();

        await _conversas.Received(1).ListarParadasAsync(
            Arg.Is<DateTimeOffset>(limite =>
                limite <= antes - TimeSpan.FromHours(6).Add(TimeSpan.FromSeconds(-1))),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }
}
