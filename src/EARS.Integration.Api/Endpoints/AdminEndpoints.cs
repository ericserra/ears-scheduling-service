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

        grupo.MapPost("/confirmacao/disparar", async (
            EnviarConfirmacaoD1UseCase useCase, CancellationToken ct) =>
        {
            var enviados = await useCase.ExecutarAsync(ct);
            return Results.Ok(new { enviados });
        })
        .WithSummary("Dispara manualmente a varredura de confirmação D-1.");

        grupo.MapPost("/agenda/sincronizar", async (
            DateOnly? dia, SincronizarAgendaUseCase useCase, CancellationToken ct) =>
        {
            var resultado = dia is { } alvo
                ? await useCase.ExecutarParaDiaAsync(alvo, ct)
                : await useCase.ExecutarAsync(ct);

            return Results.Ok(resultado);
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

        grupo.MapGet("/config", (IOptions<AutomacaoOptions> options) =>
            Results.Ok(options.Value))
        .WithSummary("Exibe os parâmetros de automação em vigor.");
    }
}
