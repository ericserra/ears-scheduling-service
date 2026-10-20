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
    ISleekFlowClient sleekFlow,
    IUnitOfWork uow,
    IOptions<AutomacaoOptions> options,
    ILogger<ExpirarConversasUseCase> logger)
{
    private const int LotePadrao = 200;

    private readonly AutomacaoOptions _opcoes = options.Value;

    public async Task<int> ExecutarAsync(CancellationToken ct = default)
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
