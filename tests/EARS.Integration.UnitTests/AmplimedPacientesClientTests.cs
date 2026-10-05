using EARS.Integration.Application.Dtos;
using EARS.Integration.Infrastructure.Clients.Amplimed;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EARS.Integration.UnitTests;

public class AmplimedPacientesClientTests
{
    private static AmplimedPacientesClient Criar(HttpFake fake) =>
        new(fake.Client(), NullLogger<AmplimedPacientesClient>.Instance);

    [Fact]
    public async Task BuscaPorCpfMandaPaginaPorqueAsDatasSeriamObrigatorias()
    {
        var fake = new HttpFake().Responder(PayloadsDaDocumentacao.BuscarPacienteArray);

        var paciente = await Criar(fake).BuscarPorCpfAsync("164.857.030-55");

        fake.Rota().Should().StartWith("/pacientes/integration/pattient?")
            .And.Contain("cpf=164.857.030-55")
            .And.Contain("page=1");

        paciente.Should().NotBeNull();
        paciente!.CodReferencia.Should().Be("123");
        paciente.Cpf.Should().Be("16485703055");
        paciente.Celular.Should().Be("(48) 99999-9999");
        paciente.DataNascimento.Should().Be(new DateOnly(1998, 1, 1));
    }

    [Fact]
    public async Task AceitaAListagemEnvelopadaEmData()
    {
        var fake = new HttpFake().Responder(PayloadsDaDocumentacao.BuscarPacienteEnvelope);

        var encontrados = await Criar(fake).ListarAsync(new FiltroPacientes(Cpf: "13037981903"));

        var paciente = encontrados.Should().ContainSingle().Subject;
        paciente.Nome.Should().Be("John Connor");
        paciente.CodReferencia.Should().BeNull("codReferencia vazio significa cadastro ainda não vinculado");
        paciente.Celular.Should().BeNull();
        paciente.DataNascimento.Should().Be(new DateOnly(1985, 2, 28));
    }

    [Fact]
    public async Task CadastroEnviaDataEmDdMmAaaaECelularMascarado()
    {
        var fake = new HttpFake().Responder(PayloadsDaDocumentacao.CadastrarPaciente);

        var ok = await Criar(fake).CadastrarAsync(new CadastrarPacienteRequest(
            CodReferencia: "ears-abc",
            Nome: "Paciente Integração",
            Cpf: "109.219.779-64",
            Celular: "5548999999999",
            DataNascimento: new DateOnly(2000, 11, 11),
            Genero: "F"));

        ok.Should().BeTrue();
        fake.Rota().Should().Be("/pacientes/integration/pattient");

        var corpo = fake.Corpos[0];
        corpo.Should().Contain("\"dataNascimento\":\"11/11/2000\"");
        corpo.Should().Contain("\"celular\":\"(48) 99999-9999\"");
        corpo.Should().Contain("\"cpf\":\"10921977964\"");
        corpo.Should().Contain("\"codReferencia\":\"ears-abc\"");
    }

    [Fact]
    public async Task VinculaCodReferenciaPorCpf()
    {
        var fake = new HttpFake().Responder(
            """{ "success": true, "codReferencia": "6", "message": "Reference code updated successfully" }""");

        var ok = await Criar(fake).VincularCodReferenciaAsync("123.123.123-00", "ears-abc");

        ok.Should().BeTrue();
        fake.Requisicoes[0].Method.Should().Be(HttpMethod.Put);
        fake.Rota().Should().Be("/pacientes/integration/codreferencia");
        fake.Corpos[0].Should().Contain("\"cpf\":\"12312312300\"");
    }

    [Theory]
    [InlineData("5548999999999", "(48) 99999-9999")]
    [InlineData("48999999999", "(48) 99999-9999")]
    [InlineData("554899999999", "(48) 9999-9999")]
    public void CelularSaiNoFormatoDaDocumentacao(string entrada, string esperado)
    {
        AmplimedPacientesClient.FormatarCelular(entrada).Should().Be(esperado);
    }
}
