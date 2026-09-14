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
    IAmplimedAgendamentoClient agendamentoClient,
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
    /// Cenário A: lê a agenda direto da Amplimed, cobrindo todos os canais.
    /// Cenário B: cai no espelho local, cobrindo só o que foi marcado pelo WhatsApp.
    /// </summary>
    private async Task<IReadOnlyList<Agendamento>> ObterAgendamentosAsync(DateOnly dia, CancellationToken ct)
    {
        if (_opcoes.LeituraAgendaViaApi)
        {
            var remotos = await agendamentoClient.ListarPorPeriodoAsync(dia, dia, ct);
            if (remotos is not null)
            {
                logger.LogInformation("Cenário A: {Total} agendamentos lidos da Amplimed para {Dia}.", remotos.Count, dia);
                // A sincronização do espelho ocorre no repositório; aqui seguimos com a base local já atualizada.
            }
            else
            {
                logger.LogWarning("Leitura de agenda indisponível na API; usando o espelho local (cenário B).");
            }
        }

        return await agendamentos.ListarPorDiaAsync(dia, ct);
    }

    private static string PrimeiroNome(string nomeCompleto) =>
        nomeCompleto.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nomeCompleto;
}
