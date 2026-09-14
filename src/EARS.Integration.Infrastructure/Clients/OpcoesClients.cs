namespace EARS.Integration.Infrastructure.Clients;

/// <summary>
/// As três APIs da Amplimed usam esquemas de autenticação distintos —
/// por isso são três configurações e três clients, não um só.
/// </summary>
public sealed class AmplimedOptions
{
    public const string SectionName = "Amplimed";

    public PacientesApiOptions Pacientes { get; set; } = new();
    public AgendamentoApiOptions Agendamento { get; set; } = new();
    public DadosApiOptions Dados { get; set; } = new();
}

public sealed class PacientesApiOptions
{
    public string BaseUrl { get; set; } = "https://integracoes.amplimed.com.br/pacientes/integration/";
    /// <summary>Enviado no cabeçalho x-Authorization como "Basic {Token}".</summary>
    public string Token { get; set; } = string.Empty;
}

public sealed class AgendamentoApiOptions
{
    public string BaseUrl { get; set; } = "https://integracoes.amplimed.com.br/";
    public string Identificador { get; set; } = string.Empty;
    /// <summary>Enviado no cabeçalho X-authorization.</summary>
    public string Token { get; set; } = string.Empty;
}

public sealed class DadosApiOptions
{
    public string BaseUrl { get; set; } = "https://hr.amplimed.com.br/";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public int TamanhoPagina { get; set; } = 100;
}

public sealed class SleekFlowOptions
{
    public const string SectionName = "SleekFlow";

    public string BaseUrl { get; set; } = "https://api.sleekflow.io/";
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>Segredo compartilhado para validar a assinatura do webhook.</summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
