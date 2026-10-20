using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.Options;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Application.UseCases;

/// <summary>
/// Porta de entrada do motor. É chamada pelo Flow Builder quando o paciente
/// escolhe "Agendar consulta" ou "Remarcar ou cancelar" no menu.
/// </summary>
/// <remarks>
/// A ordem aqui importa e não é arbitrária: a etiqueta
/// <c>agendamento-em-curso</c> é aplicada <em>antes</em> da primeira pergunta.
/// O gatilho do fluxo de entrada é "a etiqueta NÃO contém esse valor"; se a
/// pergunta saísse primeiro, a resposta do paciente reabriria o menu e ele
/// cairia de volta no início.
/// </remarks>
public sealed class IniciarAtendimentoUseCase(
    IConversaRepository conversas,
    IDialogoAtendimento dialogo,
    ISleekFlowClient sleekFlow,
    IUnitOfWork uow,
    IOptions<AutomacaoOptions> options,
    ILogger<IniciarAtendimentoUseCase> logger)
{
    private readonly AutomacaoOptions _opcoes = options.Value;

    public async Task<ResultadoInicio> ExecutarAsync(
        IniciarAtendimentoRequest request, CancellationToken ct = default)
    {
        var emCurso = await conversas.ObterAtivaPorTelefoneAsync(request.Telefone, ct);

        if (emCurso is not null && !emCurso.Expirada(_opcoes.ExpiracaoConversa))
        {
            // O fluxo chamou duas vezes, ou o paciente voltou ao menu por outro
            // caminho. Retomar é melhor que recomeçar e perder o que ele já disse.
            logger.LogInformation(
                "Atendimento já em curso para {Telefone} no estado {Estado}; retomando.",
                request.Telefone, emCurso.Estado);

            await GarantirEtiquetaAsync(emCurso, ct);
            await uow.SalvarAsync(ct);

            return ResultadoInicio.Em(emCurso.Estado.ToString());
        }

        if (emCurso is not null)
        {
            emCurso.Expirar();
            logger.LogInformation("Conversa anterior de {Telefone} expirada; começando outra.", request.Telefone);
        }

        var conversa = new Conversa(
            request.Telefone, request.ContatoId, request.Motivo, request.ConversaId);

        if (!string.IsNullOrWhiteSpace(request.NomeContato))
            conversa.RegistrarNome(request.NomeContato);

        await conversas.AdicionarAsync(conversa, ct);
        await GarantirEtiquetaAsync(conversa, ct);

        await dialogo.AbrirAsync(conversa, ct);

        await uow.SalvarAsync(ct);

        return ResultadoInicio.Em(conversa.Estado.ToString());
    }

    private async Task GarantirEtiquetaAsync(Conversa conversa, CancellationToken ct)
    {
        if (conversa.EtiquetaAplicada) return;

        if (await sleekFlow.MarcarAgendamentoEmCursoAsync(conversa.Telefone, ct))
        {
            conversa.MarcarEtiquetaAplicada();
            return;
        }

        // Sem a etiqueta o menu reabre a cada resposta do paciente e ele nunca
        // sai do início. Seguimos mesmo assim — o atendimento ainda funciona
        // se o fluxo tiver aplicado a etiqueta pelo nó nativo —, mas o log
        // precisa gritar, porque o sintoma no WhatsApp é confuso.
        logger.LogError(
            "Não foi possível aplicar a etiqueta de agendamento em curso para {Telefone}. " +
            "O menu pode reabrir a cada resposta do paciente.",
            conversa.Telefone);
    }
}
