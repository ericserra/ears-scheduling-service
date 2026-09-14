using EARS.Integration.Domain.Enums;

namespace EARS.Integration.Domain.Entities;

/// <summary>
/// Espelho local do agendamento. Em cenário B (API sem leitura de agenda por período)
/// este espelho é a única fonte para o disparo de confirmação D-1.
/// </summary>
public class Agendamento : EntidadeBase
{
    private Agendamento() { }

    public Agendamento(
        string amplimedId,
        Guid pacienteId,
        string profissionalId,
        string profissionalNome,
        DateTimeOffset inicioEm,
        OrigemAgendamento origem)
    {
        AmplimedId = amplimedId;
        PacienteId = pacienteId;
        ProfissionalId = profissionalId;
        ProfissionalNome = profissionalNome;
        InicioEm = inicioEm;
        Origem = origem;
        Status = StatusAgendamento.Agendado;
    }

    public string AmplimedId { get; private set; } = null!;
    public Guid PacienteId { get; private set; }
    public Paciente? Paciente { get; private set; }

    public string ProfissionalId { get; private set; } = null!;
    public string ProfissionalNome { get; private set; } = null!;
    public DateTimeOffset InicioEm { get; private set; }

    public StatusAgendamento Status { get; private set; }
    public OrigemAgendamento Origem { get; private set; }

    /// <summary>Quando a solicitação de confirmação D-1 foi enviada.</summary>
    public DateTimeOffset? ConfirmacaoEnviadaEm { get; private set; }

    /// <summary>Quando o lembrete único da manhã seguinte foi enviado.</summary>
    public DateTimeOffset? LembreteEnviadoEm { get; private set; }

    public DateTimeOffset? RespondidoEm { get; private set; }

    public void MarcarConfirmacaoEnviada() { ConfirmacaoEnviadaEm = DateTimeOffset.UtcNow; Tocar(); }
    public void MarcarLembreteEnviado() { LembreteEnviadoEm = DateTimeOffset.UtcNow; Tocar(); }

    public void Confirmar()
    {
        Status = StatusAgendamento.Confirmado;
        RespondidoEm = DateTimeOffset.UtcNow;
        Tocar();
    }

    public void Cancelar()
    {
        Status = StatusAgendamento.Cancelado;
        RespondidoEm = DateTimeOffset.UtcNow;
        Tocar();
    }

    public void MarcarNaoConfirmado()
    {
        if (Status == StatusAgendamento.Agendado)
        {
            Status = StatusAgendamento.NaoConfirmado;
            Tocar();
        }
    }

    public void MarcarRemarcado()
    {
        Status = StatusAgendamento.Remarcado;
        RespondidoEm = DateTimeOffset.UtcNow;
        Tocar();
    }

    /// <summary>Elegível ao disparo D-1: ainda agendado e sem envio anterior.</summary>
    public bool ElegivelParaConfirmacao() =>
        Status == StatusAgendamento.Agendado && ConfirmacaoEnviadaEm is null;

    /// <summary>Elegível ao lembrete único: já recebeu o D-1, não respondeu e não foi lembrado.</summary>
    public bool ElegivelParaLembrete() =>
        Status == StatusAgendamento.Agendado
        && ConfirmacaoEnviadaEm is not null
        && RespondidoEm is null
        && LembreteEnviadoEm is null;
}
