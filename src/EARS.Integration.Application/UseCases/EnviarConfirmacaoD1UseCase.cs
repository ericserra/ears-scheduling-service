using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.Options;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Application.UseCases;

/// <summary>
/// Módulo 2, passos 6 e 7: varre as consultas de D+1 e dispara o template
/// de confirmação. A origem da lista depende da Cláusula 3ª do contrato.
/// </summary>
public sealed class EnviarConfirmacaoD1UseCase(
    IAgendamentoRepository agendamentos,
    SincronizarAgendaUseCase sincronizarAgenda,
    ISleekFlowClient sleekFlow,
    IOptOutRepository optOut,
    IMensagemLogRepository logs,
    IUnitOfWork uow,
    IRelogio relogio,
    IOptions<AutomacaoOptions> options,
    ILogger<EnviarConfirmacaoD1UseCase> logger)
{
    private readonly AutomacaoOptions _opcoes = options.Value;

    /// <summary>Disparo agendado: a agenda de amanhã, todos os elegíveis.</summary>
    public async Task<int> ExecutarAsync(CancellationToken ct = default) =>
        (await ExecutarParaDiaAsync(relogio.HojeLocal.AddDays(1), new OpcoesDisparo(), ct)).Enviados;

    /// <summary>
    /// Disparo para um dia escolhido. É o que o endpoint administrativo usa para
    /// testar sem esperar o cron e sem atingir a agenda inteira: dá para limitar
    /// por telefone, simular (nada sai) e reenviar a quem já recebeu.
    /// </summary>
    public async Task<ResultadoDisparo> ExecutarParaDiaAsync(
        DateOnly dia, OpcoesDisparo opcoes, CancellationToken ct = default)
    {
        var alvos = await ObterAgendamentosAsync(dia, ct);
        var itens = new List<ItemDisparo>();
        var enviados = 0;

        foreach (var agendamento in alvos)
        {
            var telefone = agendamento.Paciente?.Telefone;

            // Fora do filtro não entra nem no relatório: o teste é sobre quem foi pedido.
            if (opcoes.Telefones is not null
                && (string.IsNullOrWhiteSpace(telefone) || !opcoes.Telefones.Contains(telefone)))
                continue;

            void Registrar(string situacao) => itens.Add(new ItemDisparo(
                agendamento.Id,
                agendamento.Paciente?.Nome ?? "(sem paciente)",
                telefone,
                agendamento.InicioEm,
                situacao));

            var elegivel = opcoes.Reenviar
                ? agendamento.Status == StatusAgendamento.Agendado
                : agendamento.ElegivelParaConfirmacao();

            if (!elegivel)
            {
                Registrar(agendamento.ConfirmacaoEnviadaEm is { } enviadaEm
                    ? $"ignorado: confirmação já enviada em {enviadaEm:dd/MM HH:mm} UTC (use reenviar=true)"
                    : $"ignorado: status {agendamento.Status}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(telefone))
            {
                logger.LogWarning("Agendamento {Id} sem telefone; ignorado.", agendamento.Id);
                Registrar("ignorado: paciente sem telefone");
                continue;
            }

            if (await optOut.EstaDescadastradoAsync(telefone, ct))
            {
                logger.LogInformation("Telefone descadastrado; envio suprimido para o agendamento {Id}.", agendamento.Id);
                Registrar("ignorado: telefone descadastrado");
                continue;
            }

            if (opcoes.Simular)
            {
                Registrar("simulado: seria enviado, nada saiu");
                continue;
            }

            // O template não menciona especialidade nem motivo da consulta (Cláusula 13ª, § 2º).
            var request = new EnvioTemplateRequest(
                telefone,
                _opcoes.Templates.Confirmacao,
                new List<VariavelTemplate>
                {
                    new("nome", PrimeiroNome(agendamento.Paciente!.Nome)),
                    new("profissional", agendamento.ProfissionalNome),
                    new("data", agendamento.InicioEm.ToString("dd/MM")),
                    new("hora", agendamento.InicioEm.ToString("HH:mm"))
                });

            var resultado = await sleekFlow.EnviarTemplateAsync(request, ct);

            await logs.AdicionarAsync(new MensagemLog(
                telefone, TipoMensagem.EnviadaTemplate, _opcoes.Templates.Confirmacao,
                $"confirmacao D-1 · {agendamento.InicioEm:dd/MM HH:mm}",
                agendamento.Id, resultado.Sucesso, resultado.Erro), ct);

            if (resultado.Sucesso)
            {
                agendamento.MarcarConfirmacaoEnviada();
                enviados++;
                Registrar("enviado");

                await MarcarEtiquetaAsync(agendamento, telefone, ct);
            }
            else
            {
                logger.LogError("Falha ao enviar confirmação do agendamento {Id}: {Erro}",
                    agendamento.Id, resultado.Erro);
                Registrar($"falha no envio: {resultado.Erro}");
            }
        }

        await uow.SalvarAsync(ct);
        logger.LogInformation(
            "Confirmação D-1 de {Dia} concluída: {Enviados} enviados de {Total} agendamentos{Simulacao}.",
            dia, enviados, alvos.Count, opcoes.Simular ? " (simulação)" : "");

        return new ResultadoDisparo(dia, opcoes.Simular, alvos.Count, enviados, itens);
    }

    /// <summary>
    /// Cenário A: concilia o espelho com a agenda da Amplimed antes de varrer,
    /// de modo que o disparo cobre consultas marcadas por qualquer canal.
    /// Cenário B: lê o espelho como está, cobrindo só o que veio pelo WhatsApp.
    ///
    /// Em ambos os casos a leitura é do espelho local: ele é o modelo de leitura
    /// único, e a conciliação é o que o mantém fiel. Falha na conciliação degrada
    /// para o espelho em vez de abortar o disparo — melhor confirmar o que se sabe
    /// do que não confirmar nada.
    /// </summary>
    private async Task<IReadOnlyList<Agendamento>> ObterAgendamentosAsync(DateOnly dia, CancellationToken ct)
    {
        if (_opcoes.LeituraAgendaViaApi)
        {
            try
            {
                var resultado = await sincronizarAgenda.ExecutarParaDiaAsync(dia, ct);
                logger.LogInformation(
                    "Cenário A: agenda de {Dia} conciliada ({Lidos} lidos, {Criados} novos).",
                    dia, resultado.Lidos, resultado.Criados);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Conciliação da agenda de {Dia} falhou; seguindo com o espelho local.", dia);
            }
        }

        return await agendamentos.ListarPorDiaAsync(dia, ct);
    }

    /// <summary>
    /// O fluxo do webhook só dispara para contato com a etiqueta de agendamento
    /// em curso. Sem ela, a resposta aos botões do template cai no menu. O envio
    /// já saiu, então a falha aqui não desfaz nada: fica no log para a recepção.
    /// </summary>
    private async Task MarcarEtiquetaAsync(Agendamento agendamento, string telefone, CancellationToken ct)
    {
        if (await sleekFlow.MarcarAgendamentoEmCursoAsync(telefone, ct))
        {
            agendamento.MarcarEtiquetaConfirmacaoAplicada();
            return;
        }

        logger.LogError(
            "Confirmação enviada para {Telefone}, mas a etiqueta de agendamento em curso não foi aplicada. " +
            "A resposta aos botões pode não chegar ao fluxo do webhook.",
            telefone);
    }

    private static string PrimeiroNome(string nomeCompleto) =>
        nomeCompleto.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nomeCompleto;
}

/// <summary>Como um disparo manual deve se comportar.</summary>
/// <param name="Telefones">Só estes números (já normalizados). <c>null</c> = todos os elegíveis do dia.</param>
/// <param name="Simular">Percorre e relata, mas não envia nada nem grava.</param>
/// <param name="Reenviar">Inclui quem já recebeu a confirmação, para repetir um teste.</param>
public sealed record OpcoesDisparo(
    IReadOnlySet<string>? Telefones = null,
    bool Simular = false,
    bool Reenviar = false);

public sealed record ItemDisparo(
    Guid AgendamentoId,
    string Paciente,
    string? Telefone,
    DateTimeOffset InicioEm,
    string Situacao);

public sealed record ResultadoDisparo(
    DateOnly Dia,
    bool Simulacao,
    int AgendamentosNoDia,
    int Enviados,
    IReadOnlyList<ItemDisparo> Itens);
