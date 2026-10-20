using EARS.Integration.Application.Dtos;
using EARS.Integration.Infrastructure.Clients;
using EARS.Integration.Infrastructure.Clients.SleekFlow;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EARS.Integration.UnitTests;

public class SleekFlowClientTests
{
    private static SleekFlowClient Criar(HttpFake fake, SleekFlowOptions? opcoes = null) =>
        new(fake.Client("https://api.sleekflow.io/"),
            Options.Create(opcoes ?? new SleekFlowOptions
            {
                ApiKey = "chave",
                RemetenteTelefone = "5551999990000",
            }),
            NullLogger<SleekFlowClient>.Instance);

    [Fact]
    public async Task TextoVaiNaRotaEComOCampoFromDaDocumentacao()
    {
        var fake = new HttpFake().Responder("""{ "id": "msg-1" }""");

        var resultado = await Criar(fake).EnviarTextoAsync("5551988887777", "Olá");

        resultado.Sucesso.Should().BeTrue();
        fake.Rota().Should().Be("/api/message/send/json");

        var corpo = fake.Corpos[0];
        corpo.Should().Contain("\"from\":\"5551999990000\"");
        corpo.Should().Contain("\"to\":\"5551988887777\"");
        corpo.Should().Contain("\"messageType\":\"text\"");
    }

    [Fact]
    public async Task SemRemetenteNaoTentaEnviar()
    {
        var fake = new HttpFake();

        var resultado = await Criar(fake, new SleekFlowOptions { ApiKey = "chave" })
            .EnviarTextoAsync("5551988887777", "Olá");

        resultado.Sucesso.Should().BeFalse();
        fake.Requisicoes.Should().BeEmpty("o campo from é obrigatório; sem ele a chamada só queimaria cota");
    }

    [Fact]
    public async Task TemplateMandaParametrosPosicionaisNaOrdemDaLista()
    {
        var fake = new HttpFake().Responder("{}");

        await Criar(fake).EnviarTemplateAsync(new EnvioTemplateRequest(
            "5551988887777",
            "ears_confirmacao_consulta",
            [new("nome", "Maria"), new("data", "21/10"), new("hora", "09:00")]));

        var corpo = fake.Corpos[0];
        corpo.Should().Contain("WhatsappCloudApiTemplateMessageObject");
        corpo.Should().Contain("\"templateName\":\"ears_confirmacao_consulta\"");

        // A Meta casa por posição: a ordem no JSON é o contrato.
        corpo.IndexOf("Maria", StringComparison.Ordinal)
            .Should().BeLessThan(corpo.IndexOf("21/10", StringComparison.Ordinal));
        corpo.IndexOf("21/10", StringComparison.Ordinal)
            .Should().BeLessThan(corpo.IndexOf("09:00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Canal360UsaOutroObjetoDeTemplateEExigeNamespace()
    {
        var fake = new HttpFake().Responder("{}");

        await Criar(fake, new SleekFlowOptions
        {
            ApiKey = "chave",
            RemetenteTelefone = "5551999990000",
            Canal = "whatsapp360dialog",
            TemplateNamespace = "ns-123",
        }).EnviarTemplateAsync(new EnvioTemplateRequest("5551988887777", "t", []));

        var corpo = fake.Corpos[0];
        corpo.Should().Contain("whatsapp360DialogTemplateMessage");
        corpo.Should().Contain("\"templateNamespace\":\"ns-123\"");
    }

    [Fact]
    public async Task BotoesParamEmTres()
    {
        var fake = new HttpFake().Responder("{}");

        await Criar(fake).EnviarBotoesAsync(new EnvioBotoesRequest(
            "5551988887777", "Confirma?",
            [new("c", "Confirmar"), new("r", "Remarcar"), new("x", "Cancelar"), new("z", "Quarto")]));

        var corpo = fake.Corpos[0];
        corpo.Should().Contain("Confirmar").And.Contain("Remarcar").And.Contain("Cancelar");
        corpo.Should().NotContain("Quarto", "o WhatsApp recusa a mensagem inteira com mais de três botões");
    }

    [Fact]
    public async Task EtiquetaVaiNoUpsertDeContatoComoArray()
    {
        var fake = new HttpFake().Responder("{}");

        var ok = await Criar(fake).MarcarAgendamentoEmCursoAsync("5551988887777");

        ok.Should().BeTrue();
        fake.Rota().Should().Be("/api/contact/addOrUpdate");

        var corpo = fake.Corpos[0];
        corpo.Should().StartWith("[", "o endpoint recebe uma lista de contatos");
        corpo.Should().Contain("\"phoneNumber\":\"5551988887777\"");
        corpo.Should().Contain("\"addLabels\":[\"agendamento-em-curso\"]");
    }

    [Fact]
    public async Task LimparEtiquetaUsaRemoveLabels()
    {
        var fake = new HttpFake().Responder("{}");

        await Criar(fake).LimparAgendamentoEmCursoAsync("5551988887777");

        fake.Corpos[0].Should().Contain("\"removeLabels\":[\"agendamento-em-curso\"]");
    }

    [Fact]
    public async Task TransferenciaTrocaAsDuasEtiquetasDeUmaVez()
    {
        var fake = new HttpFake().Responder("{}");

        await Criar(fake).TransferirParaAtendimentoAsync("5551988887777", "urgência");

        var corpo = fake.Corpos[0];
        corpo.Should().Contain("\"addLabels\":[\"transferir-recepcao\"]");
        corpo.Should().Contain("\"removeLabels\":[\"agendamento-em-curso\"]",
            "quem foi para a Recepção não pode continuar sem menu");
    }

    [Fact]
    public async Task EtiquetaRecusadaDevolveFalsoEmVezDeLancar()
    {
        var fake = new HttpFake().Responder("erro", System.Net.HttpStatusCode.BadRequest);

        var ok = await Criar(fake).MarcarAgendamentoEmCursoAsync("5551988887777");

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task JanelaDe24hLeORetornoDaApi()
    {
        var fake = new HttpFake().Responder(
            """[{ "whatsappPhoneNumber": "5551988887777", "isWindowOpen": true }]""");

        var aberta = await Criar(fake).JanelaDe24hAbertaAsync("5551988887777");

        aberta.Should().BeTrue();
        fake.Rota().Should().Be("/api/whatsapp/reply-window-status");
    }

    [Fact]
    public async Task JanelaIndisponivelDevolveNuloParaOChamadorDecidir()
    {
        var fake = new HttpFake().Responder("erro", System.Net.HttpStatusCode.ServiceUnavailable);

        (await Criar(fake).JanelaDe24hAbertaAsync("5551988887777")).Should().BeNull();
    }
}
