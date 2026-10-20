using EARS.Integration.Domain.Entities;

namespace EARS.Integration.Application.Abstractions;

/// <summary>
/// Abertura do diálogo, separada do processamento de mensagens para que o
/// caso de uso de início não precise conhecer a máquina de estados inteira.
/// </summary>
public interface IDialogoAtendimento
{
    /// <summary>Faz a primeira pergunta, conforme o ramo do menu que originou o atendimento.</summary>
    Task AbrirAsync(Conversa conversa, CancellationToken ct = default);
}
