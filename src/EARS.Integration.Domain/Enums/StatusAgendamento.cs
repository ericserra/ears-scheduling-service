namespace EARS.Integration.Domain.Enums;

/// <summary>
/// Ciclo de vida local do agendamento. Os sete primeiros espelham a relação de
/// status da API de Agendamento da Amplimed; <see cref="NaoConfirmado"/> é
/// exclusivamente local — a Amplimed não tem equivalente, então esse estado
/// nunca é propagado para lá (ver <c>MapaStatus</c> na camada de aplicação).
/// </summary>
public enum StatusAgendamento
{
    Agendado = 0,
    Confirmado = 1,
    Cancelado = 2,

    /// <summary>Silêncio após o lembrete único. Estado local, vira tarefa da recepção.</summary>
    NaoConfirmado = 3,

    Remarcado = 4,
    NaoCompareceu = 5,
    Finalizado = 6,
    Esperando = 7,
    EmAtendimento = 8
}
