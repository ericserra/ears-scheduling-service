using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.Options;
using EARS.Integration.Application.UseCases;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.ValueObjects;
using EARS.Integration.Infrastructure.Clients;
using EARS.Integration.Infrastructure.Clients.Amplimed;
using EARS.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Api.Endpoints;

/// <summary>
/// Endpoints administrativos de consulta e operação, protegidos por chave de API.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>Quando este processo subiu. Delata container que não reiniciou.</summary>
    private static readonly DateTimeOffset Iniciado = DateTimeOffset.UtcNow;

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/admin")
            .WithTags("Admin")
            .AddEndpointFilter(async (ctx, next) =>
            {
                var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var esperada = config["Admin:ApiKey"];

                if (string.IsNullOrEmpty(esperada))
                    return Results.Problem("Chave administrativa não configurada.", statusCode: 500);

                var recebida = ctx.HttpContext.Request.Headers["X-Api-Key"].ToString();
                return string.Equals(recebida, esperada, StringComparison.Ordinal)
                    ? await next(ctx)
                    : Results.Unauthorized();
            });

        grupo.MapGet("/agendamentos", async (DateOnly? dia, AppDbContext db, IRelogio relogio, CancellationToken ct) =>
        {
            var alvo = dia ?? relogio.HojeLocal;
            var inicio = relogio.InicioDoDiaLocal(alvo);
            var fim = relogio.InicioDoDiaLocal(alvo.AddDays(1));

            var lista = await db.Agendamentos
                .Include(a => a.Paciente)
                .Where(a => a.InicioEm >= inicio && a.InicioEm < fim)
                .OrderBy(a => a.InicioEm)
                .Select(a => new
                {
                    a.Id,
                    a.IdIntegracao,
                    a.Usuclin,
                    a.IdAgendamentoAmplimed,
                    Paciente = a.Paciente!.Nome,
                    a.ProfissionalNome,
                    a.InicioEm,
                    Status = a.Status.ToString(),
                    Origem = a.Origem.ToString(),
                    a.ConfirmacaoEnviadaEm,
                    a.RespondidoEm,
                    a.SincronizadoEm
                })
                .ToListAsync(ct);

            return Results.Ok(lista);
        })
        .WithSummary("Lista os agendamentos de um dia, com o status de confirmação.");

        grupo.MapGet("/logs", async (int pagina, AppDbContext db, CancellationToken ct) =>
        {
            const int tamanho = 50;
            var lista = await db.MensagensLog
                .OrderByDescending(m => m.CriadoEm)
                .Skip(Math.Max(0, pagina - 1) * tamanho)
                .Take(tamanho)
                .ToListAsync(ct);

            return Results.Ok(lista);
        })
        .WithSummary("Trilha de auditoria de mensagens enviadas e recebidas.");

        // Disparo manual da confirmação D-1, para teste. Sem filtro ele atingiria
        // todos os pacientes do dia, então exige telefones=... ou todos=true.
        grupo.MapPost("/confirmacao/disparar", async (
            DateOnly? dia, string? telefones, bool? todos, bool? simular, bool? reenviar,
            EnviarConfirmacaoD1UseCase useCase, IRelogio relogio, CancellationToken ct) =>
        {
            HashSet<string>? filtro = null;

            if (!string.IsNullOrWhiteSpace(telefones))
            {
                filtro = [];

                foreach (var bruto in telefones.Split(
                             ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!Telefone.TryCreate(bruto, out var numero) || numero is null)
                        return Results.BadRequest(new { erro = $"Telefone inválido: {bruto}" });

                    filtro.Add(numero.Value);
                }
            }

            if (filtro is null && todos != true)
            {
                return Results.BadRequest(new
                {
                    erro = "Informe telefones=5551999999999,5551888888888 ou todos=true. " +
                           "Sem filtro, o disparo atinge todos os pacientes do dia.",
                });
            }

            var resultado = await useCase.ExecutarParaDiaAsync(
                dia ?? relogio.HojeLocal.AddDays(1),
                new OpcoesDisparo(filtro, simular ?? false, reenviar ?? false),
                ct);

            return Results.Ok(resultado);
        })
        .WithSummary("Dispara a confirmação D-1 de um dia, com filtro de telefone, simulação e reenvio.");

        grupo.MapPost("/agenda/sincronizar", async (
            DateOnly? dia, SincronizarAgendaUseCase useCase,
            IOptions<AmplimedOptions> opcoes, CancellationToken ct) =>
        {
            try
            {
                var resultado = dia is { } alvo
                    ? await useCase.ExecutarParaDiaAsync(alvo, ct)
                    : await useCase.ExecutarAsync(ct);

                return Results.Ok(resultado);
            }
            catch (AmplimedException ex)
            {
                // Credencial recusada não é falha do serviço. Devolver 500 com
                // stack trace obriga a abrir log para descobrir que o problema
                // é configuração; o 502 com o status original diz isso de fora.
                return Results.Json(
                    new
                    {
                        status = (int)ex.Status,
                        erro = ex.Message,
                        host = opcoes.Value.Agendamento.BaseUrl,
                        identificador = opcoes.Value.Agendamento.Identificador,
                        usuclin = opcoes.Value.Agendamento.Usuclin,
                        dica = (int)ex.Status == 401
                            ? "A Amplimed recusou a credencial. Confira o formato de " +
                              "Amplimed__Agendamento__Token: o e-mail entrega \"Basic abc123\", " +
                              "e essa rota pode esperar só o abc123. Confira também se o " +
                              "identificador e o usuclin estão corretos."
                            : null,
                    },
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .WithSummary("Força o polling da agenda da Amplimed, de um dia ou da janela configurada.");

        grupo.MapPost("/conversas/expirar", async (
            ExpirarConversasUseCase useCase, CancellationToken ct) =>
        {
            var total = await useCase.ExecutarAsync(ct);
            return Results.Ok(new { expiradas = total });
        })
        .WithSummary("Força a expiração de conversas abandonadas e libera as etiquetas presas.");

        grupo.MapGet("/conversas/etiquetas-presas", async (
            ExpirarConversasUseCase useCase, CancellationToken ct) =>
        {
            var telefones = await useCase.ListarEtiquetasPresasAsync(ct);

            return Results.Ok(new
            {
                total = telefones.Count,
                telefones,
                observacao = telefones.Count == 0
                    ? "Nenhum paciente preso sem menu."
                    : "Estes números seguem com a etiqueta agendamento-em-curso: " +
                      "eles escrevem no WhatsApp e o menu não abre.",
            });
        })
        .WithSummary("Pacientes que ficaram com a etiqueta de agendamento em curso presa.");

        grupo.MapPost("/opt-out", async (
            string telefone, string? motivo,
            IOptOutRepository repo, IUnitOfWork uow, CancellationToken ct) =>
        {
            await repo.AdicionarAsync(new OptOut(telefone, motivo), ct);
            await uow.SalvarAsync(ct);
            return Results.NoContent();
        })
        .WithSummary("Registra um telefone na lista de descadastro.");

        // Cancelamento pela recepção ou por outro sistema da clínica. Aceita os
        // três identificadores que existem na prática: o par da agenda, o UUID
        // da integração e o telefone do paciente.
        grupo.MapPost("/agendamentos/cancelar", async (
            long? idAgendamento,
            string? usuclin,
            string? idIntegracao,
            string? telefone,
            string? motivo,
            bool? avisarPaciente,
            CancelarAgendamentoUseCase cancelamento,
            IOptions<AmplimedOptions> opcoes,
            CancellationToken ct) =>
        {
            var pedido = new PedidoCancelamento(
                motivo, OrigemCancelamento.Clinica, avisarPaciente ?? true);

            ResultadoCancelamento resultado;

            if (idAgendamento is { } numero)
            {
                var clinica = string.IsNullOrWhiteSpace(usuclin)
                    ? opcoes.Value.Agendamento.Usuclin
                    : usuclin;

                if (string.IsNullOrWhiteSpace(clinica))
                    return Results.BadRequest(new { erro = "usuclin não informado e não configurado." });

                resultado = await cancelamento.PorIdAmplimedAsync(clinica, numero, pedido, ct);
            }
            else if (!string.IsNullOrWhiteSpace(idIntegracao))
            {
                resultado = await cancelamento.PorIdIntegracaoAsync(idIntegracao, pedido, ct);
            }
            else if (!string.IsNullOrWhiteSpace(telefone))
            {
                resultado = await cancelamento.PorTelefoneAsync(telefone, pedido, ct);
            }
            else
            {
                return Results.BadRequest(new
                {
                    erro = "Informe idAgendamento, idIntegracao ou telefone.",
                    exemplo = "/admin/agendamentos/cancelar?idAgendamento=154788&motivo=Paciente%20ligou",
                });
            }

            return resultado.Cancelado ? Results.Ok(resultado) : Results.NotFound(resultado);
        })
        .WithSummary("Cancela um agendamento na Amplimed e avisa o paciente.");

        // Responde à pergunta que a documentação deixou em aberto: o "Buscar
        // Paciente" aceita mesmo o filtro de CPF, ou devolve a lista inteira?
        // Se devolver tudo, casar pelo primeiro registro marcaria consulta no
        // nome de outra pessoa, então o client confere o CPF por conta.
        grupo.MapGet("/amplimed/paciente", async (
            string cpf,
            IAmplimedPacientesClient pacientes,
            IOptions<AmplimedOptions> opcoes,
            CancellationToken ct) =>
        {
            if (!Cpf.TryCreate(cpf, out var documento) || documento is null)
                return Results.BadRequest(new { erro = "CPF inválido." });

            var host = opcoes.Value.Pacientes.BaseUrl;

            try
            {
                var devolvidos = await pacientes.ListarAsync(
                    new FiltroPacientes(Cpf: documento.Value, Pagina: 1), ct);

                var comOCpf = devolvidos.Where(p => p.Cpf == documento.Value).ToList();

                return Results.Ok(new
                {
                    host,
                    cpf = documento.Value,
                    totalDevolvido = devolvidos.Count,
                    comOCpfPedido = comOCpf.Count,
                    // null quando a resposta veio vazia: aí não dá para concluir nada.
                    filtroDeCpfRespeitado = devolvidos.Count == 0
                        ? (bool?)null
                        : devolvidos.Count == comOCpf.Count,
                    paciente = comOCpf
                        .Select(x => new { x.CodReferencia, x.Nome, x.Cpf, x.DataNascimento })
                        .FirstOrDefault(),
                });
            }
            catch (AmplimedException ex)
            {
                return Results.Json(
                    new { host, cpf = documento.Value, status = (int)ex.Status, erro = ex.Message },
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .WithSummary("Busca um paciente por CPF e diz se a Amplimed respeitou o filtro.");

        grupo.MapPost("/sleekflow/teste", async (
            string telefone,
            string? texto,
            bool? apenasJanela,
            ISleekFlowClient sleekFlow,
            IOptions<SleekFlowOptions> opcoes,
            CancellationToken ct) =>
        {
            if (!Telefone.TryCreate(telefone, out var destino) || destino is null)
                return Results.BadRequest(new { erro = "Telefone inválido. Use DDI e DDD, ex.: 5551999998888." });

            // Devolvido em toda resposta: sem isto, um 401 por host errado e um
            // 401 por chave errada ficam indistinguíveis de fora.
            var host = opcoes.Value.BaseUrl;

            // A janela de 24h é consulta, não envio: dá para checar a credencial
            // sem mandar mensagem para ninguém.
            var janela = await sleekFlow.JanelaDe24hAbertaAsync(destino.Value, ct);

            if (apenasJanela == true)
                return Results.Ok(new { host, telefone = destino.Value, janela24h = janela, envio = (object?)null });

            var envio = await sleekFlow.EnviarTextoAsync(
                destino.Value,
                string.IsNullOrWhiteSpace(texto) ? "Teste de integração do motor Strenos." : texto,
                ct);

            return Results.Ok(new
            {
                host,
                telefone = destino.Value,
                janela24h = janela,
                envio = new { envio.Sucesso, envio.MensagemId, envio.Erro },
                observacao = janela == false
                    ? "Janela de 24h fechada: fora dela a Meta só aceita template aprovado, então texto livre falha."
                    : null,
            });
        })
        .WithSummary("Exercita a credencial do SleekFlow sem depender da Amplimed. apenasJanela=true consulta sem enviar.");

        grupo.MapGet("/prontidao", (IProntidaoIntegracao prontidao) => Results.Ok(new
        {
            pronta = prontidao.Pronta,
            amplimed = prontidao.AmplimedConfigurada,
            sleekflow = prontidao.SleekFlowConfigurado,
            pendencias = prontidao.Pendencias,
            observacao = prontidao.Pronta
                ? "Configuração completa. O motor aceita atendimento."
                : "Configuração incompleta. O motor recusa abrir atendimento e devolve 503 ao Flow Builder.",
        }))
        .WithSummary("O que falta configurar para o motor atender de ponta a ponta.");

        // A versão vem do mesmo valor que dispara o rebuild da imagem. Sem ela
        // não dá para distinguir, de fora, "o código novo não resolveu" de "a
        // imagem não foi reconstruída", e os dois já custaram tempo aqui.
        grupo.MapGet("/config", (IOptions<AutomacaoOptions> options, IConfiguration config) =>
            Results.Ok(new
            {
                versao = config["App:Versao"] ?? "(não informada)",
                iniciadoEm = Iniciado,
                automacao = options.Value,
            }))
        .WithSummary("Parâmetros de automação em vigor e versão da imagem.");
    }
}
