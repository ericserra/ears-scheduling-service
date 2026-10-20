namespace EARS.Integration.Domain.Enums;

/// <summary>
/// Estados da máquina de conversa do motor.
/// </summary>
/// <remarks>
/// A ordem segue a coleta definida pela clínica: o que precisa e para quem
/// (idade), convênio, dia e turno, nome e data de nascimento. Médica de
/// preferência não é perguntada — é opcional, e os horários já saem com o
/// nome de quem atende.
///
/// O CPF só entra quando nome e data de nascimento não resolvem o paciente na
/// Amplimed, para não acrescentar atrito a quem já é cadastrado.
/// </remarks>
public enum EstadoConversa
{
    Inicio = 0,

    /// <summary>Consulta para o próprio contato ou para outra pessoa.</summary>
    AguardandoParaQuem = 1,

    /// <summary>Idade do paciente: decide quais profissionais podem atender.</summary>
    AguardandoIdade = 2,

    AguardandoConvenio = 3,
    AguardandoDiaTurno = 4,
    AguardandoNomePaciente = 5,
    AguardandoDataNascimento = 6,

    /// <summary>Só quando nome e data de nascimento não desambiguam.</summary>
    AguardandoCpf = 7,

    AguardandoEscolhaHorario = 8,

    /// <summary>Ramo 3: exame fica com a Recepção, consulta segue pelo motor.</summary>
    AguardandoTipoRemarcacao = 9,

    /// <summary>Confirmação explícita antes de cancelar de fato.</summary>
    AguardandoConfirmacaoCancelamento = 10,

    Concluida = 20,
    TransferidaParaHumano = 21,
    Expirada = 22
}
