namespace EARS.Integration.Domain.Enums;

/// <summary>
/// Por que o Flow Builder entregou a conversa ao motor. Vem do ramo do menu
/// que o paciente escolheu, não de inferência sobre o texto.
/// </summary>
public enum MotivoAtendimento
{
    /// <summary>Ramo 1 do menu.</summary>
    AgendarConsulta = 0,

    /// <summary>Ramo 3 do menu, parte de remarcação.</summary>
    Remarcar = 1,

    /// <summary>Ramo 3 do menu, parte de cancelamento.</summary>
    Cancelar = 2
}
