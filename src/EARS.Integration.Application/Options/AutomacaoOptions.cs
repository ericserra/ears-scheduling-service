namespace EARS.Integration.Application.Options;

/// <summary>
/// Parâmetros configuráveis sem alteração de código, conforme a proposta:
/// horário do disparo, prazo do lembrete e janela de envio.
/// </summary>
public sealed class AutomacaoOptions
{
    public const string SectionName = "Automacao";

    /// <summary>Fuso da clínica. Também usado para interpretar as datas da Amplimed,
    /// que vêm sem indicação de fuso (ex.: "2025-04-11T15:00:00").</summary>
    public string FusoHorario { get; set; } = "America/Sao_Paulo";

    /// <summary>Cron do disparo de confirmação D-1. Padrão: 17h todos os dias.</summary>
    public string CronConfirmacao { get; set; } = "0 0 17 * * ?";

    /// <summary>Cron do lembrete único. Padrão: 7h30 todos os dias.</summary>
    public string CronLembrete { get; set; } = "0 30 7 * * ?";

    /// <summary>Cron do despachante de outbox. Padrão: a cada 5 minutos.</summary>
    public string CronOutbox { get; set; } = "0 0/5 * * * ?";

    /// <summary>
    /// Cron do polling da agenda. A Amplimed não oferece webhook nesta
    /// integração, então conciliar a agenda depende de consulta periódica.
    /// Padrão: a cada 15 minutos.
    /// </summary>
    public string CronSincronizacaoAgenda { get; set; } = "0 0/15 * * * ?";

    /// <summary>Quantos dias à frente o polling da agenda cobre, contando hoje.</summary>
    public int DiasSincronizacaoAgenda { get; set; } = 2;

    /// <summary>Início da janela permitida para envio (hora local).</summary>
    public int JanelaEnvioInicioHora { get; set; } = 8;

    /// <summary>Fim da janela permitida para envio (hora local).</summary>
    public int JanelaEnvioFimHora { get; set; } = 20;

    /// <summary>Quantidade de opções de horário ofertadas ao paciente.</summary>
    public int MaxHorariosOfertados { get; set; } = 5;

    /// <summary>Horizonte de busca de horários livres na oferta ao paciente, em dias.</summary>
    public int DiasHorizonteOferta { get; set; } = 30;

    /// <summary>Janela de inatividade após a qual a conversa expira.</summary>
    public TimeSpan ExpiracaoConversa { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Teto de tentativas da outbox antes da falha definitiva.</summary>
    public int MaxTentativasOutbox { get; set; } = 8;

    /// <summary>
    /// Cláusula 3ª: define se a leitura da agenda usa a API da Amplimed
    /// (cenário A) ou apenas o espelho local (cenário B).
    ///
    /// Padrão <c>true</c> desde a verificação da documentação, que confirmou
    /// <c>listar-agendamentos</c> e <c>alterar-status-agendamento</c> — ver ADR 0002.
    /// Vira <c>false</c> só como degradação, se a clínica não liberar o usuclin
    /// ou a API ficar indisponível.
    /// </summary>
    public bool LeituraAgendaViaApi { get; set; } = true;

    public TemplatesOptions Templates { get; set; } = new();
}

public sealed class TemplatesOptions
{
    public string Confirmacao { get; set; } = "ears_confirmacao_consulta";
    public string Lembrete { get; set; } = "ears_lembrete_confirmacao";
    public string AgendamentoRealizado { get; set; } = "ears_agendamento_confirmado";
}
