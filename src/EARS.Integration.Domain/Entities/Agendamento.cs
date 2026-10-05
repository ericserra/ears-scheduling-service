using EARS.Integration.Domain.Enums;

namespace EARS.Integration.Domain.Entities;

/// <summary>
/// Espelho local do agendamento.
/// </summary>
/// <remarks>
/// A Amplimed usa dois espaços de identificador distintos para o mesmo
/// agendamento, e o serviço precisa dos dois (ver ADR 0003):
///
/// <list type="bullet">
/// <item><see cref="IdIntegracao"/> — UUID devolvido por <c>cadastrar-agendamento</c>.
/// É o único aceito por <c>cancelar-agendamento</c> e <c>excluir-agendamento</c>,
/// e só existe para o que foi marcado por esta integração.</item>
/// <item><see cref="IdAgendamentoAmplimed"/> + <see cref="Usuclin"/> — par inteiro
/// devolvido por <c>listar-agendamentos</c>. É o único aceito por
/// <c>alterar-status-agendamento</c>, e existe para todo agendamento da clínica,
/// inclusive os marcados no sistema da Amplimed.</item>
/// </list>
///
/// Um agendamento nascido no WhatsApp começa só com o UUID e ganha o par inteiro
/// na primeira sincronização da agenda que o encontrar.
/// </remarks>
public class Agendamento : EntidadeBase
{
    private Agendamento() { }

    /// <summary>Agendamento criado por esta integração (cadastrar-agendamento).</summary>
    public static Agendamento DaIntegracao(
        string idIntegracao,
        string idHorario,
        Guid pacienteId,
        string profissionalId,
        string profissionalNome,
        DateTimeOffset inicioEm,
        int? duracaoMinutos = null) => new()
        {
            IdIntegracao = idIntegracao,
            IdHorario = idHorario,
            PacienteId = pacienteId,
            ProfissionalId = profissionalId,
            ProfissionalNome = profissionalNome,
            InicioEm = inicioEm,
            DuracaoMinutos = duracaoMinutos,
            Origem = OrigemAgendamento.WhatsApp,
            Status = StatusAgendamento.Agendado
        };

    /// <summary>
    /// Agendamento lido da agenda da clínica (listar-agendamentos).
    /// </summary>
    /// <remarks>
    /// Sem <c>profissionalId</c> de propósito: a listagem da agenda identifica o
    /// profissional só por nome, conselho e especialidade — não devolve o
    /// <c>codigoProfissional</c>. A especialidade não é guardada porque o
    /// template de véspera não pode mencioná-la (Cláusula 13ª, § 2º).
    /// </remarks>
    public static Agendamento DaAgenda(
        string usuclin,
        long idAgendamentoAmplimed,
        Guid pacienteId,
        string profissionalNome,
        DateTimeOffset inicioEm,
        int? duracaoMinutos,
        StatusAgendamento status) => new()
        {
            Usuclin = usuclin,
            IdAgendamentoAmplimed = idAgendamentoAmplimed,
            PacienteId = pacienteId,
            ProfissionalNome = profissionalNome,
            InicioEm = inicioEm,
            DuracaoMinutos = duracaoMinutos,
            Origem = OrigemAgendamento.Amplimed,
            Status = status
        };

    /// <summary>UUID de <c>cadastrar-agendamento</c>. Nulo no que veio da agenda.</summary>
    public string? IdIntegracao { get; private set; }

    /// <summary>Token do slot usado na criação. Guardado para auditoria da corrida de horário.</summary>
    public string? IdHorario { get; private set; }

    /// <summary>Número da clínica na Amplimed. Obrigatório em alterar-status-agendamento.</summary>
    public string? Usuclin { get; private set; }

    /// <summary>Id inteiro da agenda. Obrigatório em alterar-status-agendamento.</summary>
    public long? IdAgendamentoAmplimed { get; private set; }

    public Guid PacienteId { get; private set; }
    public Paciente? Paciente { get; private set; }

    /// <summary>
    /// Código do profissional na Amplimed. Nulo no que veio da agenda da
    /// clínica: <c>listar-agendamentos</c> não devolve esse código.
    /// </summary>
    public string? ProfissionalId { get; private set; }
    public string ProfissionalNome { get; private set; } = null!;
    public DateTimeOffset InicioEm { get; private set; }
    public int? DuracaoMinutos { get; private set; }

    public StatusAgendamento Status { get; private set; }
    public OrigemAgendamento Origem { get; private set; }

    /// <summary>Quando a solicitação de confirmação D-1 foi enviada.</summary>
    public DateTimeOffset? ConfirmacaoEnviadaEm { get; private set; }

    /// <summary>Quando o lembrete único da manhã seguinte foi enviado.</summary>
    public DateTimeOffset? LembreteEnviadoEm { get; private set; }

    public DateTimeOffset? RespondidoEm { get; private set; }

    /// <summary>Última vez que o status foi conciliado com a agenda da Amplimed.</summary>
    public DateTimeOffset? SincronizadoEm { get; private set; }

    public void MarcarConfirmacaoEnviada() { ConfirmacaoEnviadaEm = DateTimeOffset.UtcNow; Tocar(); }
    public void MarcarLembreteEnviado() { LembreteEnviadoEm = DateTimeOffset.UtcNow; Tocar(); }

    /// <summary>
    /// Late binding do par (usuclin, idAgendamento): o agendamento criado pela
    /// integração só conhece o próprio UUID até aparecer na listagem da agenda.
    /// </summary>
    public void VincularAgenda(string usuclin, long idAgendamentoAmplimed)
    {
        Usuclin = usuclin;
        IdAgendamentoAmplimed = idAgendamentoAmplimed;
        Tocar();
    }

    /// <summary>Só é possível pedir alteração de status com o par inteiro em mãos.</summary>
    public bool PodeAlterarStatusNaAmplimed() =>
        IdAgendamentoAmplimed is not null && !string.IsNullOrWhiteSpace(Usuclin);

    /// <summary>Cancelamento e exclusão só valem para o que nasceu na integração.</summary>
    public bool PodeCancelarNaAmplimed() => !string.IsNullOrWhiteSpace(IdIntegracao);

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

    /// <summary>
    /// Conciliação com a agenda. A resposta do paciente é soberana: um status já
    /// respondido por aqui não é sobrescrito pela listagem, que pode estar atrasada.
    /// O estado local <see cref="StatusAgendamento.NaoConfirmado"/> também sobrevive,
    /// porque a Amplimed segue mostrando "Agendado" nesse caso.
    /// </summary>
    public void ConciliarComAgenda(StatusAgendamento statusRemoto, DateTimeOffset inicioRemoto, int? duracaoRemota)
    {
        SincronizadoEm = DateTimeOffset.UtcNow;

        if (inicioRemoto != InicioEm) InicioEm = inicioRemoto;
        if (duracaoRemota is not null) DuracaoMinutos = duracaoRemota;

        var podeSobrescrever = RespondidoEm is null && Status != StatusAgendamento.NaoConfirmado;
        if (podeSobrescrever && statusRemoto != Status) Status = statusRemoto;

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
