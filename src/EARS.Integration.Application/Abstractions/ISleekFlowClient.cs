using EARS.Integration.Application.Dtos;

namespace EARS.Integration.Application.Abstractions;

public interface ISleekFlowClient
{
    /// <summary>Mensagem de texto livre. Só válida dentro da janela de 24h da Meta.</summary>
    Task<ResultadoEnvio> EnviarTextoAsync(string telefone, string texto, CancellationToken ct = default);

    /// <summary>Template aprovado. Único envio possível fora da janela de 24h.</summary>
    Task<ResultadoEnvio> EnviarTemplateAsync(EnvioTemplateRequest request, CancellationToken ct = default);

    /// <summary>Move a conversa para a fila de atendimento humano, com o contexto coletado.</summary>
    Task TransferirParaAtendimentoAsync(string telefone, string contexto, CancellationToken ct = default);
}
