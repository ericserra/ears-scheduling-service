using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Options;
using EARS.Integration.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Application.UseCases;

/// <summary>
/// Fecha conversas abandonadas e, principalmente, tira a etiqueta
/// <c>agendamento-em-curso</c> de quem desistiu no meio.
/// </summary>
/// <remarks>
/// Esta rotina não é higiene de banco: é o que impede uma trava silenciosa.
/// O gatilho do fluxo de entrada no Flow Builder só dispara quando a etiqueta
/// <em>não</em> está no contato. Um paciente que começa a agendar e some fica
/// etiquetado; sem ninguém para limpar, ele nunca mais vê o menu e as
/// mensagens dele caem no vazio — sem erro, sem log, sem reclamação que
/// aponte para a causa.
///
/// Antes a expiração só era avaliada quando chegava mensagem nova, o que
/// nunca acontece justamente no caso que importa.
/// </remarks>
public sealed class ExpirarConversasUseCase(
    IConversaRepository conversas,
    IAgendamentoRepository agendamentos,
    ISleekFlowClient sleekFlow,
    IUnitOfWork uow,
    IOptions<AutomacaoOptions> options,
    ILogger<ExpirarConversasUseCase> logger)
{
    private const int LotePadrao = 200;

    private readonly AutomacaoOptions _opcoes = options.Value;

    /// <summary>Devolve quantas conversas expiraram. A etiqueta da confirmação D-1 é liberada em seguida.</summary>
    public async Task<int> ExecutarAsync(CancellationToken ct = default)
    {
        var expiradas = await ExpirarConversasParadasAsync(ct);
        await LiberarEtiquetasDeConfirmacaoAsync(ct);
        return expiradas;
    }

    private async Task<int> ExpirarConversasParadasAsync(CancellationToken ct)
    {
        var limite = DateTimeOffset.UtcNow - _opcoes.ExpiracaoConversa;
        var paradas = await conversas.ListarParadasAsync(limite, LotePadrao, ct);

        if (paradas.Count == 0) return 0;

        var etiquetasRemovidas = 0;

        foreach (var conversa in paradas)
        {
            ct.ThrowIfCancellationRequested();

            conversa.Expirar();

            if (!conversa.EtiquetaAplicada) continue;

            if (await sleekFlow.LimparAgendamentoEmCursoAsync(conversa.Telefone, ct))
            {
                conversa.MarcarEtiquetaRemovida();
                etiquetasRemovidas++;
            }
            else
            {
                // Fica marcada como aplicada de propósito: a próxima execução
                // tenta de novo. Desistir aqui deixaria o paciente sem menu.
                logger.LogError(
                    "Conversa {ConversaId} expirada, mas a etiqueta de {Telefone} não pôde ser removida. " +
                    "Nova tentativa na próxima execução.",
                    conversa.Id, conversa.Telefone);
            }
        }

        await uow.SalvarAsync(ct);

        logger.LogInformation(
            "{Total} conversa(s) expirada(s); {Etiquetas} etiqueta(s) liberada(s).",
            paradas.Count, etiquetasRemovidas);

        return paradas.Count;
    }

    /// <summary>
    /// A confirmação D-1 etiqueta o contato para que a resposta aos botões chegue
    /// ao fluxo do webhook. Quem nunca responde ficaria sem menu, então passado o
    /// prazo a etiqueta sai. Se o contato abriu uma conversa de agendamento nesse
    /// meio tempo, a etiqueta é dela e fica: quem a remove é a expiração acima.
    /// </summary>
    private async Task LiberarEtiquetasDeConfirmacaoAsync(CancellationToken ct)
    {
        var limite = DateTimeOffset.UtcNow - _opcoes.ExpiracaoEtiquetaConfirmacao;
        var pendentes = await agendamentos.ListarComEtiquetaDeConfirmacaoAsync(limite, LotePadrao, ct);

        if (pendentes.Count == 0) return;

        var liberados = 0;

        foreach (var grupo in pendentes
                     .Where(a => !string.IsNullOrWhiteSpace(a.Paciente?.Telefone))
                     .GroupBy(a => a.Paciente!.Telefone))
        {
            ct.ThrowIfCancellationRequested();

            var ativa = await conversas.ObterAtivaPorTelefoneAsync(grupo.Key, ct);
            if (ativa is not null && !ativa.Expirada(_opcoes.ExpiracaoConversa)) continue;

            if (await sleekFlow.LimparAgendamentoEmCursoAsync(grupo.Key, ct))
            {
                foreach (var agendamento in grupo) agendamento.MarcarEtiquetaConfirmacaoRemovida();
                liberados++;
            }
            else
            {
                logger.LogError(
                    "Etiqueta da confirmação D-1 de {Telefone} não pôde ser removida. " +
                    "Nova tentativa na próxima execução.",
                    grupo.Key);
            }
        }

        await uow.SalvarAsync(ct);

        logger.LogInformation(
            "{Total} contato(s) com etiqueta de confirmação D-1 vencida; {Liberados} liberado(s).",
            pendentes.Select(a => a.Paciente?.Telefone).Distinct().Count(), liberados);
    }

    /// <summary>
    /// Conversas que expiraram mas seguem com a etiqueta presa. Serve ao
    /// endpoint administrativo, para a recepção conseguir enxergar o problema
    /// antes do paciente reclamar.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListarEtiquetasPresasAsync(CancellationToken ct = default)
    {
        var paradas = await conversas.ListarParadasAsync(DateTimeOffset.UtcNow, LotePadrao, ct);

        return paradas
            .Where(c => c.EtiquetaAplicada && c.Estado == EstadoConversa.Expirada)
            .Select(c => c.Telefone)
            .ToList();
    }
}
