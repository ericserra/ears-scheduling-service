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
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EARS.Integration.UnitTests;

/// <summary>
/// Disparo geral pedido pela clínica: a decisão de enviar é o status da agenda
/// da Amplimed, lido na hora. Quem já está confirmado, cancelado, bloqueado ou
/// fora da agenda não recebe; e sem conseguir ler a agenda, ninguém recebe.
/// </summary>
public class EnviarConfirmacaoD1PelaAgendaTests
{
    private static readonly DateOnly Dia = new(2026, 11, 16);
    private const string Usuclin = "57622";

    private readonly IPacienteRepository _pacientes = Substitute.For<IPacienteRepository>();
    private readonly IAmplimedPacientesClient _pacientesClient = Substitute.For<IAmplimedPacientesClient>();
    private readonly IAmplimedAgendamentoClient _agenda = Substitute.For<IAmplimedAgendamentoClient>();
    private readonly IAgendamentoRepository _agendamentos = Substitute.For<IAgendamentoRepository>();
    private readonly ISleekFlowClient _sleekFlow = Substitute.For<ISleekFlowClient>();
    private readonly IOptOutRepository _optOut = Substitute.For<IOptOutRepository>();
    private readonly IMensagemLogRepository _logs = Substitute.For<IMensagemLogRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IRelogio _relogio = Substitute.For<IRelogio>();
    private readonly DateTimeOffset _agora = DateTimeOffset.UtcNow;

    public EnviarConfirmacaoD1PelaAgendaTests()
    {
        _relogio.HojeLocal.Returns(Dia.AddDays(-1));
        _relogio.ParaLocal(Arg.Any<DateTimeOffset>()).Returns(c => c.Arg<DateTimeOffset>().ToOffset(TimeSpan.FromHours(-3)));
        _relogio.AgoraUtc.Returns(_agora);
        _sleekFlow.EnviarTemplateAsync(Arg.Any<EnvioTemplateRequest>(), Arg.Any<CancellationToken>())
            .Returns(ResultadoEnvio.Ok("msg-1"));
        _sleekFlow.MarcarAgendamentoEmCursoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private EnviarConfirmacaoD1UseCase Criar(bool leituraViaApi = true)
    {
        var opcoes = Options.Create(new AutomacaoOptions { LeituraAgendaViaApi = leituraViaApi });
        var prontidao = Substitute.For<IProntidaoIntegracao>();
        prontidao.AmplimedConfigurada.Returns(true);

        var sincronizar = new SincronizarAgendaUseCase(
            _agenda,
            _pacientesClient,
            prontidao,
            _pacientes,
            _agendamentos, _uow, _relogio, opcoes,
            NullLogger<SincronizarAgendaUseCase>.Instance);

        return new EnviarConfirmacaoD1UseCase(
            _agendamentos, sincronizar, _sleekFlow, _optOut, _logs, _uow, _relogio, opcoes,
            NullLogger<EnviarConfirmacaoD1UseCase>.Instance);
    }

    private static readonly OpcoesDisparo Geral = new(ValidarNaAgenda: true);

    private static Agendamento Consulta(long id, string telefone = "5551988887777")
    {
        var paciente = new Paciente("Maria Souza", Telefone.Create(telefone));
        var agendamento = Agendamento.DaAgenda(
            Usuclin, id, paciente.Id, "Dra. Inesângela",
            new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero), 30, StatusAgendamento.Agendado);

        typeof(Agendamento).GetProperty(nameof(Agendamento.Paciente))!.SetValue(agendamento, paciente);
        return agendamento;
    }

    private static AgendamentoDaAgenda ItemDaAgenda(
        long id, StatusAmplimed? status, string? texto = null, string? cpf = null,
        string? nome = "Maria Souza") => new(
        Usuclin, id, new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero), 30,
        status, texto ?? status?.ToString(), null, null, null,
        new ProfissionalDaAgenda("Dra. Inesângela", null, null, null, null),
        new BeneficiarioDaAgenda(null, null, cpf, nome, null));

    /// <summary>Monta o dia: o que a agenda devolve e o espelho local correspondente.</summary>
    private void Dia16(IReadOnlyList<AgendamentoDaAgenda> daAgenda, params Agendamento[] espelho)
    {
        _agenda.ListarAgendaCompletaDoDiaAsync(Dia, Arg.Any<CancellationToken>()).Returns(daAgenda);
        _agendamentos.ListarPorDiaAsync(Dia, Arg.Any<CancellationToken>()).Returns(espelho);

        foreach (var consulta in espelho)
            _agendamentos.ObterPorIdAmplimedAsync(
                    consulta.Usuclin!, consulta.IdAgendamentoAmplimed!.Value, Arg.Any<CancellationToken>())
                .Returns(consulta);
    }

    [Fact]
    public async Task AgendadoNaAgendaRecebe()
    {
        var consulta = Consulta(1);
        Dia16(new[] { ItemDaAgenda(1, StatusAmplimed.Agendado) }, consulta);

        var resultado = await Criar().ExecutarParaDiaAsync(Dia, Geral);

        resultado.Enviados.Should().Be(1);
        resultado.Itens.Should().ContainSingle().Which.Situacao.Should().Be("enviado");
        consulta.EtiquetaConfirmacaoAplicada.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmadoNaAgendaNaoRecebe()
    {
        // Mesmo que o espelho ainda diga Agendado: a agenda é quem manda.
        var consulta = Consulta(1);
        Dia16(new[] { ItemDaAgenda(1, StatusAmplimed.Confirmado) }, consulta);

        var resultado = await Criar().ExecutarParaDiaAsync(Dia, Geral);

        resultado.Enviados.Should().Be(0);
        await _sleekFlow.DidNotReceive().EnviarTemplateAsync(
            Arg.Any<EnvioTemplateRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StatusDesconhecidoComoBloqueadoNaoRecebe()
    {
        var consulta = Consulta(1);
        Dia16(new[] { ItemDaAgenda(1, status: null, texto: "Bloqueado") }, consulta);

        var resultado = await Criar().ExecutarParaDiaAsync(Dia, Geral);

        resultado.Enviados.Should().Be(0);
        resultado.Itens.Should().ContainSingle().Which.Situacao.Should().Contain("não mostra");
    }

    [Fact]
    public async Task ConsultaForaDaAgendaNaoRecebe()
    {
        var consulta = Consulta(1);
        Dia16(Array.Empty<AgendamentoDaAgenda>(), consulta);

        var resultado = await Criar().ExecutarParaDiaAsync(Dia, Geral);

        resultado.Enviados.Should().Be(0);
    }

    [Fact]
    public async Task QuemRespondeuPeloWhatsAppNaoRecebeMesmoSeAAgendaAindaDizAgendado()
    {
        var consulta = Consulta(1);
        consulta.Confirmar();
        Dia16(new[] { ItemDaAgenda(1, StatusAmplimed.Agendado) }, consulta);

        var resultado = await Criar().ExecutarParaDiaAsync(Dia, Geral);

        resultado.Enviados.Should().Be(0);
        resultado.Itens.Should().ContainSingle().Which.Situacao.Should().Contain("já respondeu");
    }

    [Fact]
    public async Task QuemJaRecebeuMasAgendaAindaDizAgendadoRecebeDeNovoDepoisDaJanela()
    {
        var consulta = Consulta(1);
        consulta.MarcarConfirmacaoEnviada();
        _relogio.AgoraUtc.Returns(_agora.AddHours(7));
        Dia16(new[] { ItemDaAgenda(1, StatusAmplimed.Agendado) }, consulta);

        var resultado = await Criar().ExecutarParaDiaAsync(Dia, Geral);

        resultado.Enviados.Should().Be(1);
        resultado.Itens.Should().ContainSingle().Which.Situacao.Should().Contain("já havia recebido");
    }

    [Fact]
    public async Task EnvioRecenteNaoDuplicaSemReenviar()
    {
        var consulta = Consulta(1);
        consulta.MarcarConfirmacaoEnviada();
        Dia16(new[] { ItemDaAgenda(1, StatusAmplimed.Agendado) }, consulta);

        var semReenvio = await Criar().ExecutarParaDiaAsync(Dia, Geral);
        semReenvio.Enviados.Should().Be(0);
        semReenvio.Itens.Should().ContainSingle().Which.Situacao.Should().Contain("reenviar=true");

        var comReenvio = await Criar().ExecutarParaDiaAsync(Dia, Geral with { Reenviar = true });
        comReenvio.Enviados.Should().Be(1);
    }

    [Fact]
    public async Task SemLerAAgendaNadaSai()
    {
        var consulta = Consulta(1);
        _agenda.ListarAgendaCompletaDoDiaAsync(Dia, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Amplimed fora do ar"));
        _agendamentos.ListarPorDiaAsync(Dia, Arg.Any<CancellationToken>()).Returns(new[] { consulta });

        var acao = () => Criar().ExecutarParaDiaAsync(Dia, Geral);

        await acao.Should().ThrowAsync<InvalidOperationException>();
        await _sleekFlow.DidNotReceive().EnviarTemplateAsync(
            Arg.Any<EnvioTemplateRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ComALeituraDaAgendaDesligadaODisparoPelaAgendaNaoComeca()
    {
        var acao = () => Criar(leituraViaApi: false).ExecutarParaDiaAsync(Dia, Geral);

        await acao.Should().ThrowAsync<AgendaIndisponivelException>();
        await _sleekFlow.DidNotReceive().EnviarTemplateAsync(
            Arg.Any<EnvioTemplateRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LocalNaoConfirmadoComAgendaAgendadoRecebe()
    {
        var consulta = Consulta(1);
        typeof(Agendamento).GetProperty(nameof(Agendamento.Status))!.SetValue(consulta, StatusAgendamento.NaoConfirmado);
        Dia16(new[] { ItemDaAgenda(1, StatusAmplimed.Agendado) }, consulta);

        var resultado = await Criar().ExecutarParaDiaAsync(Dia, Geral);

        resultado.Enviados.Should().Be(1);
    }

    [Fact]
    public async Task OHorarioDoTemplateSaiNoFusoDaClinicaENaoEmUtc()
    {
        // 12:00 UTC é 09:00 em Brasília: é o que o paciente precisa ler.
        var consulta = Consulta(1);
        Dia16(new[] { ItemDaAgenda(1, StatusAmplimed.Agendado) }, consulta);

        await Criar().ExecutarParaDiaAsync(Dia, Geral);

        await _sleekFlow.Received(1).EnviarTemplateAsync(
            Arg.Is<EnvioTemplateRequest>(r =>
                r.Variaveis.Any(v => v.Nome == "hora" && v.Valor == "09:00")
                && r.Variaveis.Any(v => v.Nome == "data" && v.Valor == "16/11")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void HorarioDaAgendaComOffsetEGravadoEmUtc()
    {
        // O Npgsql recusa DateTimeOffset com offset diferente de zero.
        var consulta = Consulta(1);
        var comOffset = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.FromHours(-3));

        consulta.ConciliarComAgenda(StatusAgendamento.Agendado, comOffset, 30);

        consulta.InicioEm.Offset.Should().Be(TimeSpan.Zero);
        consulta.InicioEm.Should().Be(comOffset);
    }

    [Fact]
    public async Task DoisAgendamentosDoMesmoCpfNoDiaCriamUmSoPaciente()
    {
        // O paciente novo ainda não está no banco quando o segundo item é lido:
        // criar de novo estoura o índice único de CPF.
        const string cpf = "52998224725";
        _agenda.ListarAgendaCompletaDoDiaAsync(Dia, Arg.Any<CancellationToken>()).Returns(new[]
        {
            ItemDaAgenda(1, StatusAmplimed.Agendado, cpf: cpf),
            ItemDaAgenda(2, StatusAmplimed.Agendado, cpf: cpf),
        });
        _pacientesClient.BuscarPorCpfAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PacienteAmplimed("EARS-1", "Maria Souza", cpf, "5551988887777", null, null, null));
        _agendamentos.ListarPorDiaAsync(Dia, Arg.Any<CancellationToken>()).Returns(Array.Empty<Agendamento>());

        await Criar().ExecutarParaDiaAsync(Dia, new OpcoesDisparo(Simular: true, ValidarNaAgenda: true));

        await _pacientes.Received(1).AdicionarAsync(Arg.Any<Paciente>(), Arg.Any<CancellationToken>());
        await _agendamentos.Received(2).AdicionarAsync(Arg.Any<Agendamento>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PacienteSemCpfAindaAgendadoApareceParaContatoManualMasEventoSemPacienteNao()
    {
        _agenda.ListarAgendaCompletaDoDiaAsync(Dia, Arg.Any<CancellationToken>()).Returns(new[]
        {
            ItemDaAgenda(1, StatusAmplimed.Agendado, cpf: null),
            ItemDaAgenda(2, StatusAmplimed.Agendado, cpf: null, nome: null),
            ItemDaAgenda(3, StatusAmplimed.Confirmado, cpf: null),
        });
        _agendamentos.ListarPorDiaAsync(Dia, Arg.Any<CancellationToken>()).Returns(Array.Empty<Agendamento>());

        var resultado = await Criar().ExecutarParaDiaAsync(Dia, new OpcoesDisparo(Simular: true, ValidarNaAgenda: true));

        // Só a consulta 1 precisa de gente: a 2 não tem paciente e a 3 já está confirmada.
        var contato = resultado.PrecisamContatoManual.Should().ContainSingle().Subject;
        contato.Paciente.Should().Be("Maria Souza");
        contato.Motivo.Should().Contain("sem CPF");
    }
}
