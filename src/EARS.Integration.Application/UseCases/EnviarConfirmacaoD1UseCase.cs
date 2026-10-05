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

    public async Task<int> ExecutarAsync(CancellationToken ct = default)
    {
        var amanha = relogio.HojeLocal.AddDays(1);
        var alvos = await ObterAgendamentosAsync(amanha, ct);

        var enviados = 0;

        foreach (var agendamento in alvos.Where(a => a.ElegivelParaConfirmacao()))
        {
            var telefone = agendamento.Paciente?.Telefone;
            if (string.IsNullOrWhiteSpace(telefone))
            {
                logger.LogWarning("Agendamento {Id} sem telefone; ignorado.", agendamento.Id);
                continue;
            }

            if (await optOut.EstaDescadastradoAsync(telefone, ct))
            {
                logger.LogInformation("Telefone descadastrado; envio suprimido para o agendamento {Id}.", agendamento.Id);
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
            }
            else
            {
                logger.LogError("Falha ao enviar confirmação do agendamento {Id}: {Erro}",
                    agendamento.Id, resultado.Erro);
            }
        }

        await uow.SalvarAsync(ct);
        logger.LogInformation("Confirmação D-1 concluída: {Enviados} de {Total} agendamentos.", enviados, alvos.Count);
        return enviados;
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

    private static string PrimeiroNome(string nomeCompleto) =>
        nomeCompleto.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nomeCompleto;
}
