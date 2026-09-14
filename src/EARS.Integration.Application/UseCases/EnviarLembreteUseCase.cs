using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.Options;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Application.UseCases;

/// <summary>
/// Lembrete único da manhã seguinte. Quem continuar em silêncio é marcado como
/// não confirmado e vira tarefa da recepção — não há terceira mensagem.
/// </summary>
public sealed class EnviarLembreteUseCase(
    IAgendamentoRepository agendamentos,
    ISleekFlowClient sleekFlow,
    IOptOutRepository optOut,
    IMensagemLogRepository logs,
    IUnitOfWork uow,
    IRelogio relogio,
    IOptions<AutomacaoOptions> options,
    ILogger<EnviarLembreteUseCase> logger)
{
    private readonly AutomacaoOptions _opcoes = options.Value;

    public async Task<int> ExecutarAsync(CancellationToken ct = default)
    {
        var hoje = relogio.HojeLocal;
        var doDia = await agendamentos.ListarPorDiaAsync(hoje, ct);
        var enviados = 0;

        foreach (var agendamento in doDia.Where(a => a.ElegivelParaLembrete()))
        {
            var telefone = agendamento.Paciente?.Telefone;
            if (string.IsNullOrWhiteSpace(telefone)) continue;
            if (await optOut.EstaDescadastradoAsync(telefone, ct)) continue;

            var request = new EnvioTemplateRequest(
                telefone,
                _opcoes.Templates.Lembrete,
                new List<VariavelTemplate>
                {
                    new("nome", agendamento.Paciente!.Nome.Split(' ')[0]),
                    new("hora", agendamento.InicioEm.ToString("HH:mm"))
                });

            var resultado = await sleekFlow.EnviarTemplateAsync(request, ct);

            await logs.AdicionarAsync(new MensagemLog(
                telefone, TipoMensagem.EnviadaTemplate, _opcoes.Templates.Lembrete,
                $"lembrete · {agendamento.InicioEm:dd/MM HH:mm}",
                agendamento.Id, resultado.Sucesso, resultado.Erro), ct);

            if (resultado.Sucesso)
            {
                agendamento.MarcarLembreteEnviado();
                agendamento.MarcarNaoConfirmado();
                enviados++;
            }
        }

        await uow.SalvarAsync(ct);
        logger.LogInformation("Lembrete único enviado para {Enviados} agendamentos.", enviados);
        return enviados;
    }
}
