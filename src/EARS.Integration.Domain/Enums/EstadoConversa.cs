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
    Expirada = 8,

    /// <summary>
    /// Só é alcançado quando o CPF não existe na base da Amplimed. O upsert do
    /// <c>cadastrar-agendamento</c> casa por CPF + data de nascimento quando o
    /// idUnico é novo; sem a data, criaria um cadastro duplicado.
    /// </summary>
    AguardandoDataNascimento = 9
}
