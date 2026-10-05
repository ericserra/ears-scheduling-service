namespace EARS.Integration.Infrastructure.Clients;

/// <summary>
/// As duas APIs da Amplimed usadas aqui compartilham o mesmo gateway mas têm
/// esquemas de autenticação diferentes — daí duas configurações e dois clients.
/// </summary>
/// <remarks>
/// Ambientes documentados (o valor de <c>BaseUrl</c> é a raiz do gateway):
/// <list type="bullet">
/// <item>dev: <c>https://devintegracoes.amplimed.cloud/</c></item>
/// <item>qa: <c>https://qaintegracoes.amplimed.cloud/</c></item>
/// <item>stg: <c>https://stgintegracoes.amplimed.cloud/</c></item>
/// <item>produção: <c>https://integracoes.amplimed.com.br/</c></item>
/// </list>
/// O caminho de cada API (<c>integracoes/referencia/...</c>,
/// <c>pacientes/integration/...</c>) é montado pelo client, não pela configuração.
/// </remarks>
public sealed class AmplimedOptions
{
    public const string SectionName = "Amplimed";

    public PacientesApiOptions Pacientes { get; set; } = new();
    public AgendamentoApiOptions Agendamento { get; set; } = new();
}

public sealed class PacientesApiOptions
{
    /// <summary>Raiz do gateway. O client acrescenta <c>pacientes/integration/</c>.</summary>
    public string BaseUrl { get; set; } = "https://integracoes.amplimed.com.br/";

    /// <summary>Enviado no cabeçalho <c>x-Authorization</c> como "Basic {Token}".</summary>
    public string Token { get; set; } = string.Empty;
}

public sealed class AgendamentoApiOptions
{
    /// <summary>Raiz do gateway. O client acrescenta <c>integracoes/referencia/{Identificador}/</c>.</summary>
    public string BaseUrl { get; set; } = "https://integracoes.amplimed.com.br/";

    /// <summary>
    /// Identificador gerado pela Amplimed. Vai no <em>caminho</em> da rota,
    /// depois de <c>/referencia</c>, e não em cabeçalho.
    /// </summary>
    public string Identificador { get; set; } = string.Empty;

    /// <summary>Enviado no cabeçalho <c>x-authorization</c>.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Número da clínica na Amplimed (usuclin). Obrigatório em
    /// <c>listar-agendamentos</c> e <c>alterar-status-agendamento</c>, ou seja,
    /// sem ele o cenário A da Cláusula 3ª não roda. Fornecido pela clínica.
    /// </summary>
    public string Usuclin { get; set; } = string.Empty;
}

public sealed class SleekFlowOptions
{
    public const string SectionName = "SleekFlow";

    public string BaseUrl { get; set; } = "https://api.sleekflow.io/";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Segredo compartilhado para validar a assinatura do webhook.</summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
