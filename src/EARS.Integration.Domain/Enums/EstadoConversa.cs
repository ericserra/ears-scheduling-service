namespace EARS.Integration.Domain.Enums;

/// <summary>
/// Estados da máquina de conversa do fluxo de agendamento (Módulo 1).
/// </summary>
public enum EstadoConversa
{
    Inicio = 0,
    AguardandoIdentificacao = 1,
    AguardandoCpf = 2,
    AguardandoProfissional = 3,
    AguardandoPreferencia = 4,
    AguardandoEscolhaHorario = 5,
    Concluida = 6,
    TransferidaParaHumano = 7,
    Expirada = 8
}
