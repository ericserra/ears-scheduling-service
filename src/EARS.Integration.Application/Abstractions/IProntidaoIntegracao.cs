namespace EARS.Integration.Application.Abstractions;

/// <summary>
/// Diz se o serviço tem configuração suficiente para conduzir um atendimento.
/// </summary>
/// <remarks>
/// Existe por causa de uma situação real: o serviço sobe antes das credenciais
/// da clínica chegarem, para o ambiente ficar de pé enquanto o resto é
/// negociado. Nesse estado ele não pode aceitar paciente — um agendamento que
/// começa e trava no meio é pior do que um que nunca começa, porque o paciente
/// fica com a etiqueta aplicada e sem menu.
///
/// Em vez de confiar em ninguém lembrar de não apontar o webhook, o motor
/// recusa a abertura enquanto estiver incompleto.
/// </remarks>
public interface IProntidaoIntegracao
{
    /// <summary>Verdadeiro quando dá para atender de ponta a ponta.</summary>
    bool Pronta { get; }

    bool AmplimedConfigurada { get; }

    bool SleekFlowConfigurado { get; }

    /// <summary>O que falta, em nomes de configuração, para aparecer no log e no /admin.</summary>
    IReadOnlyList<string> Pendencias { get; }
}
