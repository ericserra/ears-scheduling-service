using EARS.Integration.Application.Dtos;

namespace EARS.Integration.Application.Abstractions;

/// <summary>
/// SleekFlow Platform API (<c>https://api.sleekflow.io</c>), autenticada pelo
/// cabeçalho <c>X-Sleekflow-Api-Key</c>.
/// </summary>
/// <remarks>
/// Duas limitações da API moldam o que existe aqui, e estão detalhadas no
/// ADR 0004:
///
/// <list type="bullet">
/// <item>Não há rota para atribuir conversa a uma <em>equipe</em>. O
/// <c>assignee</c> aceita apenas <c>staffId</c>. Por isso a transferência para
/// a Recepção é feita por etiqueta, que dispara um fluxo no Flow Builder.</item>
/// <item>Etiqueta não tem rota própria de vínculo: aplicar e remover passam
/// pelo upsert de contato (<c>addOrUpdate</c>), com <c>addLabels</c> e
/// <c>removeLabels</c>.</item>
/// </list>
/// </remarks>
public interface ISleekFlowClient
{
    /// <summary>Texto livre. Só vale dentro da janela de 24h da Meta.</summary>
    Task<ResultadoEnvio> EnviarTextoAsync(string telefone, string texto, CancellationToken ct = default);

    /// <summary>
    /// Botões de resposta rápida, até três. Também só dentro da janela de 24h —
    /// fora dela, o caminho é template.
    /// </summary>
    Task<ResultadoEnvio> EnviarBotoesAsync(EnvioBotoesRequest request, CancellationToken ct = default);

    /// <summary>
    /// Template aprovado pela Meta. Único envio possível fora da janela de 24h.
    /// Os parâmetros são <em>posicionais</em>: a ordem da lista é a ordem de
    /// {{1}}, {{2}}... no corpo aprovado.
    /// </summary>
    Task<ResultadoEnvio> EnviarTemplateAsync(EnvioTemplateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Consulta a janela de 24h de verdade, em vez de deduzir pelo histórico
    /// local. <c>null</c> quando a API não respondeu — o chamador decide, e a
    /// escolha segura é tratar como fechada e mandar template.
    /// </summary>
    Task<bool?> JanelaDe24hAbertaAsync(string telefone, CancellationToken ct = default);

    /// <summary>
    /// Aplica a etiqueta que impede o menu do Flow Builder de reabrir enquanto
    /// o agendamento está em curso.
    /// </summary>
    Task<bool> MarcarAgendamentoEmCursoAsync(string telefone, CancellationToken ct = default);

    /// <summary>
    /// Retira a etiqueta de agendamento em curso. Esquecer esta chamada deixa
    /// o paciente sem menu para sempre: é o efeito colateral mais caro desta
    /// integração, e por isso ela aparece em toda saída do fluxo — conclusão,
    /// transferência e expiração.
    /// </summary>
    Task<bool> LimparAgendamentoEmCursoAsync(string telefone, CancellationToken ct = default);

    /// <summary>
    /// Entrega a conversa ao atendimento humano: aplica a etiqueta de
    /// transferência (que o Flow Builder observa para atribuir à equipe
    /// Recepção), tira a de agendamento em curso e, quando houver um staffId
    /// configurado, também atribui direto.
    /// </summary>
    Task TransferirParaAtendimentoAsync(
        string telefone, string contexto, CancellationToken ct = default);
}
