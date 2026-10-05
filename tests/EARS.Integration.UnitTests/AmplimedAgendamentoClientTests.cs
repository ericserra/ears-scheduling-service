using System.Net;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.Options;
using EARS.Integration.Infrastructure.Clients;
using EARS.Integration.Infrastructure.Clients.Amplimed;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EARS.Integration.UnitTests;

public class AmplimedAgendamentoClientTests
{
    private const string Identificador = "ident-123";
    private const string Usuclin = "19";

    private static AmplimedAgendamentoClient Criar(HttpFake fake) => new(
        fake.Client(),
        Options.Create(new AmplimedOptions
        {
            Agendamento = new AgendamentoApiOptions
            {
                Identificador = Identificador,
                Token = "tok",
                Usuclin = Usuclin
            }
        }),
        Options.Create(new AutomacaoOptions()),
        NullLogger<AmplimedAgendamentoClient>.Instance);

    [Fact]
    public async Task IdentificadorVaiNoCaminhoDaRota()
    {
        var fake = new HttpFake().Responder(PayloadsDaDocumentacao.ListarProfissionais);

        await Criar(fake).ListarProfissionaisAsync();

        fake.Rota().Should().StartWith($"/integracoes/referencia/{Identificador}/listar-profissionais");
    }

    [Fact]
    public async Task ListarProfissionaisLeProcedimentosEDatas()
    {
        var fake = new HttpFake().Responder(PayloadsDaDocumentacao.ListarProfissionais);

        var profissionais = await Criar(fake).ListarProfissionaisAsync(
            new FiltroProfissionais(new DateOnly(2023, 11, 1), new DateOnly(2023, 11, 30)));

        fake.Rota().Should().Contain("dataInicial=2023-11-01").And.Contain("dataFinal=2023-11-30");

        var profissional = profissionais.Should().ContainSingle().Subject;
        profissional.CodigoProfissional.Should().Be("654e42a2fb7e795963e1eb14");
        profissional.Nome.Should().Be("Médico Teste Integração");
        profissional.DatasDisponiveis.Should().HaveCount(3);
        profissional.Procedimentos.Should().HaveCount(2);
        profissional.Convenios.Should().ContainSingle();
    }

    [Fact]
    public async Task HorarioPreservaOIdHorarioEInterpretaAHoraNoFusoDaClinica()
    {
        var fake = new HttpFake().Responder(PayloadsDaDocumentacao.ListarHorarios);

        var horarios = await Criar(fake).ListarHorariosDisponiveisAsync(
            "654e42a2fb7e795963e1eb14", new DateOnly(2023, 11, 14), new DateOnly(2023, 11, 14));

        horarios.Should().HaveCount(2);

        var primeiro = horarios[0];
        primeiro.IdHorario.Should().Be("654e42a2fb7e795963e1eb14_2023-11-14_08:00:00_08:15:00_false_2_8");
        primeiro.Inicio.Offset.Should().Be(TimeSpan.FromHours(-3));
        primeiro.Inicio.Hour.Should().Be(8);
        primeiro.Valor.Should().Be(150.2m);
    }

    [Fact]
    public async Task ListarAgendamentosAchataARespostaAninhadaETraduzOStatus()
    {
        var fake = new HttpFake().Responder(PayloadsDaDocumentacao.ListarAgendamentos);

        var pagina = await Criar(fake).ListarAgendamentosDoDiaAsync(new DateOnly(2025, 4, 11));

        fake.Rota().Should().Be(
            $"/integracoes/referencia/{Identificador}/{Usuclin}/listar-agendamentos?data=2025-04-11&page=1");

        pagina.TotalPaginas.Should().Be(1);
        pagina.Agendamentos.Should().HaveCount(2);

        var primeiro = pagina.Agendamentos[0];
        primeiro.IdAgendamento.Should().Be(1);
        primeiro.Usuclin.Should().Be("1");
        primeiro.Status.Should().Be(StatusAmplimed.Agendado);
        primeiro.Profissional.Nome.Should().Be("Dr João");
        primeiro.Beneficiario.Cpf.Should().Be("111.111.111-11");
        primeiro.Inicio.Hour.Should().Be(15);
        primeiro.Inicio.Offset.Should().Be(TimeSpan.FromHours(-3));

        pagina.Agendamentos[1].Status.Should().Be(StatusAmplimed.Cancelado);
    }

    [Fact]
    public async Task ListarAgendamentosNaoChamaAApiSemUsuclin()
    {
        var fake = new HttpFake();

        var client = new AmplimedAgendamentoClient(
            fake.Client(),
            Options.Create(new AmplimedOptions
            {
                Agendamento = new AgendamentoApiOptions { Identificador = Identificador, Token = "tok" }
            }),
            Options.Create(new AutomacaoOptions()),
            NullLogger<AmplimedAgendamentoClient>.Instance);

        var pagina = await client.ListarAgendamentosDoDiaAsync(new DateOnly(2025, 4, 11));

        pagina.Agendamentos.Should().BeEmpty();
        fake.Requisicoes.Should().BeEmpty();
    }

    [Fact]
    public async Task CadastrarEnviaIdHorarioEDataNoFormatoDaApiDeAgendamento()
    {
        var fake = new HttpFake().Responder(PayloadsDaDocumentacao.CadastrarAgendamento);

        var criado = await Criar(fake).CadastrarAsync(new CadastrarAgendamentoRequest(
            IdUnico: "ears-abc",
            IdHorario: "slot-1",
            Cpf: "805.127.029-36",
            Nome: "Paciente Integração",
            DataNascimento: new DateOnly(2000, 10, 11),
            Celular: "5547992640155"));

        criado.IdIntegracao.Should().Be("195fb405-40bb-40d6-9f52-a22d3d6369b9");

        var corpo = fake.Corpos[0];
        corpo.Should().Contain("\"dataNascimento\":\"2000-10-11\"");
        corpo.Should().Contain("\"cpf\":\"80512702936\"");
        corpo.Should().Contain("\"idHorario\":\"slot-1\"");
        corpo.Should().Contain("\"celular\":\"47992640155\"",
            "aqui o celular vai sem DDI e sem máscara, diferente da API de Pacientes");
    }

    [Fact]
    public async Task AlterarStatusEnviaOStatusCodeNumerico()
    {
        var fake = new HttpFake().Responder(PayloadsDaDocumentacao.AlterarStatus);

        var ok = await Criar(fake).AlterarStatusAsync(Usuclin, 1690, StatusAmplimed.Confirmado);

        ok.Should().BeTrue();
        fake.Rota().Should().EndWith("/alterar-status-agendamento");
        fake.Corpos[0].Should().Contain("\"statusCode\":0")
            .And.Contain("\"idAgendamento\":1690")
            .And.Contain("\"usuclin\":19", "o exemplo da documentação manda o usuclin como número");
    }

    [Theory]
    [InlineData(StatusAmplimed.Confirmado, 0)]
    [InlineData(StatusAmplimed.Cancelado, 3)]
    [InlineData(StatusAmplimed.Reagendado, 7)]
    public async Task StatusCodeSegueARelacaoDaDocumentacao(StatusAmplimed status, int esperado)
    {
        var fake = new HttpFake().Responder(PayloadsDaDocumentacao.AlterarStatus);

        await Criar(fake).AlterarStatusAsync(Usuclin, 1, status);

        fake.Corpos[0].Should().Contain($"\"statusCode\":{esperado}");
    }

    [Fact]
    public async Task AlterarStatusIndisponivelDegradaEmVezDeLancar()
    {
        var fake = new HttpFake().Responder("{}", HttpStatusCode.NotImplemented);

        var ok = await Criar(fake).AlterarStatusAsync(Usuclin, 1, StatusAmplimed.Confirmado);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task UsuclinNaoNumericoNaoViraChamada()
    {
        var fake = new HttpFake();

        var client = new AmplimedAgendamentoClient(
            fake.Client(),
            Options.Create(new AmplimedOptions
            {
                Agendamento = new AgendamentoApiOptions { Identificador = Identificador, Token = "tok" }
            }),
            Options.Create(new AutomacaoOptions()),
            NullLogger<AmplimedAgendamentoClient>.Instance);

        var ok = await client.AlterarStatusAsync("clinica-x", 1, StatusAmplimed.Confirmado);

        ok.Should().BeFalse();
        fake.Requisicoes.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelarUsaOUuidDaIntegracaoComMotivo()
    {
        var fake = new HttpFake().Responder("""{ "message": "Appointment cancelled" }""");

        var ok = await Criar(fake).CancelarAsync("5e179761-7806-46ca-ab1b-2ac96e0ed280", "Não poderei comparecer");

        ok.Should().BeTrue();
        fake.Rota().Should().EndWith("/cancelar-agendamento");
        fake.Corpos[0].Should().Contain("5e179761-7806-46ca-ab1b-2ac96e0ed280")
            .And.Contain("motivoCancelamento");
    }

    [Fact]
    public async Task AgendaCompletaPercorreTodasAsPaginas()
    {
        var paginaUm = PayloadsDaDocumentacao.ListarAgendamentos.Replace("\"totalPages\": 1", "\"totalPages\": 2");
        var paginaDois = PayloadsDaDocumentacao.ListarAgendamentos
            .Replace("\"currentPage\": 1", "\"currentPage\": 2")
            .Replace("\"totalPages\": 1", "\"totalPages\": 2")
            .Replace("\"idAgendamento\": 1,", "\"idAgendamento\": 11,")
            .Replace("\"idAgendamento\": 2,", "\"idAgendamento\": 12,");

        var fake = new HttpFake().Responder(paginaUm).Responder(paginaDois);

        var todos = await Criar(fake).ListarAgendaCompletaDoDiaAsync(new DateOnly(2025, 4, 11));

        todos.Should().HaveCount(4);
        fake.Requisicoes.Should().HaveCount(2);
        fake.Rota(1).Should().Contain("page=2");
    }
}
