using System.Text.Json;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using EARS.Integration.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EARS.Integration.Application.UseCases;

/// <summary>Como o cancelamento foi pedido. Entra no log e no motivo enviado à Amplimed.</summary>
public enum OrigemCancelamento
{
    /// <summary>Resposta do paciente no WhatsApp.</summary>
    Paciente = 0,

    /// <summary>Recepção ou outro sistema da clínica, pelo endpoint administrativo.</summary>
    Clinica = 1,

    /// <summary>Nó HTTP do Flow Builder.</summary>
    FlowBuilder = 2,
}

public sealed record PedidoCancelamento(
    string? Motivo = null,
    OrigemCancelamento Origem = OrigemCancelamento.Clinica,
    bool AvisarPaciente = true);

public sealed record ResultadoCancelamento(
    bool Cancelado,
    string Situacao,
    Guid? AgendamentoId = null,
    bool HorarioLiberado = false,
    bool PacienteAvisado = false)
{
    public static ResultadoCancelamento NaoEncontrado(string detalhe) =>
        new(false, detalhe);
}

/// <summary>
/// Cancelamento de agendamento, com um único caminho para as três portas que o
/// pedem: a resposta do paciente no WhatsApp, o endpoint administrativo da
/// clínica e o nó HTTP do Flow Builder.
/// </summary>
/// <remarks>
/// A ordem de tentativa importa e é a regra do ADR 0003. <c>cancelar-agendamento</c>
/// vem primeiro porque devolve o horário à escala, mas só aceita o UUID de
/// agendamento criado por esta integração. Para o que nasceu no sistema da
/// clínica resta <c>alterar-status-agendamento</c>, que marca Cancelado sem
/// necessariamente liberar a vaga. Quando nenhum dos dois é possível agora, o
/// pedido vai para a outbox em vez de sumir.
///
/// Por isso <see cref="ResultadoCancelamento.HorarioLiberado"/> existe separado
/// de <see cref="ResultadoCancelamento.Cancelado"/>: quem chama precisa saber se
/// a recepção ainda tem trabalho a fazer.
/// </remarks>
public sealed class CancelarAgendamentoUseCase(
    IAgendamentoRepository agendamentos,
    IAmplimedAgendamentoClient agendamentoClient,
    ISleekFlowClient sleekFlow,
    IMensagemLogRepository logs,
    IOutboxRepository outbox,
    IUnitOfWork uow,
    ILogger<CancelarAgendamentoUseCase> logger)
{
    public const string TipoOutboxStatus = "amplimed.alterar-status-agendamento";

    private const string AvisoPadrao =
        "Sua consulta foi cancelada. Se quiser remarcar, é só escrever por aqui.";

    // -----------------------------------------------------------------------
    // Portas de entrada
    // -----------------------------------------------------------------------

    /// <summary>Pelo par (usuclin, idAgendamento) que a listagem da agenda devolve.</summary>
    public async Task<ResultadoCancelamento> PorIdAmplimedAsync(
        string usuclin, long idAgendamento, PedidoCancelamento pedido, CancellationToken ct = default)
    {
        var agendamento = await agendamentos.ObterPorIdAmplimedAsync(usuclin, idAgendamento, ct);

        if (agendamento is null)
        {
            logger.LogWarning(
                "Cancelamento pedido para ({Usuclin}, {IdAgendamento}), que não existe no espelho local.",
                usuclin, idAgendamento);

            return ResultadoCancelamento.NaoEncontrado(
                "Agendamento não encontrado. Rode /admin/agenda/sincronizar para o dia e tente de novo.");
        }

        return await ExecutarAsync(agendamento, pedido, ct);
    }

    /// <summary>Pelo UUID devolvido por <c>cadastrar-agendamento</c>.</summary>
    public async Task<ResultadoCancelamento> PorIdIntegracaoAsync(
        string idIntegracao, PedidoCancelamento pedido, CancellationToken ct = default)
    {
        var agendamento = await agendamentos.ObterPorIdIntegracaoAsync(idIntegracao, ct);

        return agendamento is null
            ? ResultadoCancelamento.NaoEncontrado("Nenhum agendamento com esse identificador de integração.")
            : await ExecutarAsync(agendamento, pedido, ct);
    }

    /// <summary>
    /// Pelo telefone do paciente, cancelando o agendamento ativo mais recente.
    /// É a porta que o Flow Builder usa, porque lá o que existe é o contato.
    /// </summary>
    public async Task<ResultadoCancelamento> PorTelefoneAsync(
        string telefone, PedidoCancelamento pedido, CancellationToken ct = default)
    {
        if (!Telefone.TryCreate(telefone, out var numero) || numero is null)
            return ResultadoCancelamento.NaoEncontrado("Telefone inválido.");

        var agendamento = await agendamentos.ObterAtivoMaisRecentePorTelefoneAsync(numero.Value, ct);

        return agendamento is null
            ? ResultadoCancelamento.NaoEncontrado("Nenhum agendamento ativo para este telefone.")
            : await ExecutarAsync(agendamento, pedido, ct);
    }

    // -----------------------------------------------------------------------

    public async Task<ResultadoCancelamento> ExecutarAsync(
        Agendamento agendamento, PedidoCancelamento pedido, CancellationToken ct = default)
    {
        if (agendamento.Status == StatusAgendamento.Cancelado)
        {
            return new ResultadoCancelamento(
                true, "Agendamento já estava cancelado.", agendamento.Id);
        }

        var motivo = string.IsNullOrWhiteSpace(pedido.Motivo)
            ? MotivoPadrao(pedido.Origem)
            : pedido.Motivo!.Trim();

        agendamento.Cancelar();

        var liberado = await LiberarOuMarcarAsync(agendamento, motivo, ct);
        var avisado = await AvisarPacienteAsync(agendamento, pedido, ct);

        await uow.SalvarAsync(ct);

        logger.LogInformation(
            "Agendamento {AgendamentoId} cancelado por {Origem}. Horário liberado: {Liberado}.",
            agendamento.Id, pedido.Origem, liberado);

        return new ResultadoCancelamento(
            Cancelado: true,
            Situacao: liberado
                ? "Cancelado e horário devolvido à agenda."
                : "Cancelado. O horário pode continuar bloqueado na agenda, porque este agendamento " +
                  "não nasceu na integração e só aceita alteração de status.",
            AgendamentoId: agendamento.Id,
            HorarioLiberado: liberado,
            PacienteAvisado: avisado);
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Devolve <c>true</c> somente quando o horário voltou para a escala, que é
    /// o que <c>cancelar-agendamento</c> faz. A alteração de status marca o
    /// cancelamento sem essa garantia.
    /// </summary>
    private async Task<bool> LiberarOuMarcarAsync(
        Agendamento agendamento, string motivo, CancellationToken ct)
    {
        if (agendamento.PodeCancelarNaAmplimed()
            && await agendamentoClient.CancelarAsync(agendamento.IdIntegracao!, motivo, ct))
        {
            return true;
        }

        if (agendamento.PodeCancelarNaAmplimed())
        {
            logger.LogWarning(
                "cancelar-agendamento não concluiu para {IdIntegracao}; caindo para alteração de status.",
                agendamento.IdIntegracao);
        }

        if (!agendamento.PodeAlterarStatusNaAmplimed())
        {
            logger.LogWarning(
                "Agendamento {AgendamentoId} ainda sem par (usuclin, idAgendamento); " +
                "cancelamento enfileirado na outbox.",
                agendamento.Id);

            await EnfileirarAsync(agendamento, ct);
            return false;
        }

        var ok = await agendamentoClient.AlterarStatusAsync(
            agendamento.Usuclin!, agendamento.IdAgendamentoAmplimed!.Value, StatusAmplimed.Cancelado, ct);

        if (!ok) await EnfileirarAsync(agendamento, ct);

        return false;
    }

    private Task EnfileirarAsync(Agendamento agendamento, CancellationToken ct) =>
        outbox.AdicionarAsync(
            new OutboxMessage(TipoOutboxStatus, JsonSerializer.Serialize(new
            {
                agendamentoId = agendamento.Id,
                idIntegracao = agendamento.IdIntegracao,
                usuclin = agendamento.Usuclin,
                idAgendamento = agendamento.IdAgendamentoAmplimed,
                statusCode = (int)StatusAmplimed.Cancelado,
            })), ct);

    /// <summary>
    /// O aviso é melhor esforço: cancelamento registrado e paciente não avisado
    /// é ruim, mas cancelamento perdido porque o WhatsApp falhou é pior.
    /// </summary>
    private async Task<bool> AvisarPacienteAsync(
        Agendamento agendamento, PedidoCancelamento pedido, CancellationToken ct)
    {
        if (!pedido.AvisarPaciente) return false;

        // O paciente que acabou de pedir o cancelamento já recebe a confirmação
        // pelo próprio fluxo da conversa; repetir aqui mandaria duas mensagens.
        if (pedido.Origem == OrigemCancelamento.Paciente) return false;

        var telefone = agendamento.Paciente?.Telefone;

        if (string.IsNullOrWhiteSpace(telefone))
        {
            logger.LogWarning(
                "Agendamento {AgendamentoId} cancelado sem telefone para avisar o paciente.",
                agendamento.Id);
            return false;
        }

        var resultado = await sleekFlow.EnviarTextoAsync(telefone, AvisoPadrao, ct);

        await logs.AdicionarAsync(new MensagemLog(
            telefone, TipoMensagem.EnviadaTexto, null,
            $"cancelamento por {pedido.Origem} · {agendamento.InicioEm:dd/MM HH:mm}",
            agendamento.Id, resultado.Sucesso, resultado.Erro), ct);

        if (!resultado.Sucesso)
        {
            logger.LogWarning(
                "Não foi possível avisar o paciente do cancelamento de {AgendamentoId}: {Erro}",
                agendamento.Id, resultado.Erro);
        }

        return resultado.Sucesso;
    }

    private static string MotivoPadrao(OrigemCancelamento origem) => origem switch
    {
        OrigemCancelamento.Paciente => "Cancelado pelo paciente via WhatsApp",
        OrigemCancelamento.FlowBuilder => "Cancelado pelo paciente via atendimento automático",
        _ => "Cancelado pela clínica",
    };
}
