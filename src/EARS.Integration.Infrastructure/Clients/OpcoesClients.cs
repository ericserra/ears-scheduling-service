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

    /// <summary>
    /// Vai no cabeçalho <c>x-Authorization</c>. A Amplimed entrega o valor já
    /// com o prefixo ("Basic abc123"), e a documentação o descreve sem ele, o
    /// que faz a mesma string ser colada dos dois jeitos. <see cref="Cabecalho"/>
    /// aceita as duas formas em vez de produzir "Basic Basic abc123".
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Valor pronto para o cabeçalho, com exatamente um "Basic ".</summary>
    public string Cabecalho => Credenciais.ComoBasic(Token);
}

/// <summary>
/// Tolerância de formato para as credenciais que chegam por e-mail da Amplimed.
/// </summary>
public static class Credenciais
{
    private const string Prefixo = "Basic ";

    /// <summary>
    /// Devolve o token com um único prefixo <c>Basic</c>, tenha ele vindo com
    /// ou sem. Vazio continua vazio: quem decide o que fazer com credencial
    /// ausente é a prontidão, não este método.
    /// </summary>
    public static string ComoBasic(string? token)
    {
        var limpo = (token ?? string.Empty).Trim();

        if (limpo.Length == 0) return string.Empty;

        return limpo.StartsWith(Prefixo, StringComparison.OrdinalIgnoreCase)
            ? Prefixo + limpo[Prefixo.Length..].Trim()
            : Prefixo + limpo;
    }
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

    /// <summary>
    /// Vai no cabeçalho <c>x-authorization</c>, exatamente como a Amplimed
    /// mandou. O e-mail de credenciais entrega esta API com o prefixo
    /// ("Basic abc123"), e é assim que o valor deve ser configurado.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Número da clínica na Amplimed (usuclin). Obrigatório em
    /// <c>listar-agendamentos</c> e <c>alterar-status-agendamento</c>, ou seja,
    /// sem ele o cenário A da Cláusula 3ª não roda. Fornecido pela clínica.
    /// </summary>
    public string Usuclin { get; set; } = string.Empty;
}

/// <summary>
/// SleekFlow Platform API. Base <c>https://api.sleekflow.io</c>, autenticada
/// pelo cabeçalho <c>X-Sleekflow-Api-Key</c> — não é Bearer.
/// </summary>
public sealed class SleekFlowOptions
{
    public const string SectionName = "SleekFlow";

    public string BaseUrl { get; set; } = "https://api.sleekflow.io/";

    /// <summary>Enviado no cabeçalho <c>X-Sleekflow-Api-Key</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Segredo compartilhado para validar a assinatura do webhook.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Canal da conta: <c>whatsappcloudapi</c>, <c>whatsapp360dialog</c> ou
    /// <c>whatsapp</c> (Twilio). Decide o formato do objeto de template e de
    /// botões, que difere entre eles.
    /// </summary>
    public string Canal { get; set; } = "whatsappcloudapi";

    /// <summary>
    /// Número remetente da clínica, em formato internacional sem o "+".
    /// Obrigatório: o <c>send/json</c> exige o campo <c>from</c>.
    /// </summary>
    public string RemetenteTelefone { get; set; } = string.Empty;

    /// <summary>Namespace dos templates. Só o 360dialog exige.</summary>
    public string TemplateNamespace { get; set; } = string.Empty;

    public string IdiomaTemplate { get; set; } = "pt_BR";

    /// <summary>
    /// Etiqueta que impede o menu do Flow Builder de reabrir enquanto o
    /// agendamento está em curso. O gatilho do fluxo de entrada é
    /// "Label NÃO contém este valor", então o nome precisa bater exatamente.
    /// </summary>
    public string EtiquetaAgendamentoEmCurso { get; set; } = "agendamento-em-curso";

    /// <summary>
    /// Etiqueta de transferência. A Platform API não atribui conversa a uma
    /// equipe (o assignee só aceita staffId), então a entrega à Recepção é
    /// feita aplicando esta etiqueta, que dispara um fluxo no Flow Builder.
    /// Ver ADR 0004.
    /// </summary>
    public string EtiquetaTransferirRecepcao { get; set; } = "transferir-recepcao";

    /// <summary>
    /// Atribuição direta a um atendente, quando a clínica preferir. Vazio
    /// deixa a entrega só por etiqueta.
    /// </summary>
    public string StaffIdRecepcao { get; set; } = string.Empty;

    /// <summary>Marcadores analíticos anexados a cada envio.</summary>
    public List<string> TagsAnaliticas { get; set; } = ["motor-strenos"];
}
