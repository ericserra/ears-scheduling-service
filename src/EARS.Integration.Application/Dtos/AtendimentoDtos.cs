using EARS.Integration.Domain.Enums;

namespace EARS.Integration.Application.Dtos;

/// <summary>
/// Pedido que o Flow Builder manda ao entregar a conversa ao motor, nos ramos
/// 1 (agendar consulta) e 3 (remarcar ou cancelar) do menu.
/// </summary>
/// <remarks>
/// O motivo vem do ramo escolhido no menu, não de inferência sobre o texto:
/// quem já sabe o que o paciente pediu é o fluxo.
/// </remarks>
public sealed record IniciarAtendimentoRequest(
    string Telefone,
    MotivoAtendimento Motivo,
    string? ContatoId = null,
    string? ConversaId = null,
    string? NomeContato = null);

/// <summary>O que o Flow Builder precisa saber depois de entregar a conversa.</summary>
public sealed record ResultadoInicio(
    bool Aceito,
    string Estado,
    string? Motivo = null)
{
    public static ResultadoInicio Em(string estado) => new(true, estado);
    public static ResultadoInicio Recusado(string motivo) => new(false, "nao-iniciado", motivo);
}
