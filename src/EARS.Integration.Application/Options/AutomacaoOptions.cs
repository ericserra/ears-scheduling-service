namespace EARS.Integration.Application.Options;

/// <summary>
/// Parâmetros configuráveis sem alteração de código, conforme a proposta:
/// horário do disparo, prazo do lembrete e janela de envio.
/// </summary>
public sealed class AutomacaoOptions
{
    public const string SectionName = "Automacao";

    /// <summary>Fuso da clínica. Porto Alegre segue America/Sao_Paulo.</summary>
    public string FusoHorario { get; set; } = "America/Sao_Paulo";

    /// <summary>Cron do disparo de confirmação D-1. Padrão: 17h todos os dias.</summary>
    public string CronConfirmacao { get; set; } = "0 0 17 * * ?";

    /// <summary>Cron do lembrete único. Padrão: 7h30 todos os dias.</summary>
    public string CronLembrete { get; set; } = "0 30 7 * * ?";

    /// <summary>Cron do despachante de outbox. Padrão: a cada 5 minutos.</summary>
    public string CronOutbox { get; set; } = "0 0/5 * * * ?";

    /// <summary>Início da janela permitida para envio (hora local).</summary>
    public int JanelaEnvioInicioHora { get; set; } = 8;

    /// <summary>Fim da janela permitida para envio (hora local).</summary>
    public int JanelaEnvioFimHora { get; set; } = 20;

    /// <summary>Quantidade de opções de horário ofertadas ao paciente.</summary>
    public int MaxHorariosOfertados { get; set; } = 5;

    /// <summary>Janela de inatividade após a qual a conversa expira.</summary>
    public TimeSpan ExpiracaoConversa { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Teto de tentativas da outbox antes da falha definitiva.</summary>
    public int MaxTentativasOutbox { get; set; } = 8;

    /// <summary>
    /// Cláusula 3ª: define se a leitura da agenda usa a API da Amplimed (cenário A)
    /// ou o espelho local (cenário B). Definido após a verificação da semana 1.
    /// </summary>
    public bool LeituraAgendaViaApi { get; set; }

    public TemplatesOptions Templates { get; set; } = new();
}

public sealed class TemplatesOptions
{
    public string Confirmacao { get; set; } = "ears_confirmacao_consulta";
    public string Lembrete { get; set; } = "ears_lembrete_confirmacao";
    public string AgendamentoRealizado { get; set; } = "ears_agendamento_confirmado";
}
