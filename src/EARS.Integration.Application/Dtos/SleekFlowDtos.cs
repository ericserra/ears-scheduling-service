namespace EARS.Integration.Application.Dtos;

/// <summary>Payload normalizado do webhook de mensagem recebida.</summary>
public sealed record MensagemRecebida(
    string Telefone,
    string? ContatoId,
    string? Texto,
    string? BotaoPayload,
    DateTimeOffset RecebidaEm);

public sealed record VariavelTemplate(string Nome, string Valor);

public sealed record EnvioTemplateRequest(
    string Telefone,
    string TemplateNome,
    IReadOnlyList<VariavelTemplate> Variaveis);

public sealed record ResultadoEnvio(bool Sucesso, string? MensagemId, string? Erro)
{
    public static ResultadoEnvio Ok(string? id) => new(true, id, null);
    public static ResultadoEnvio Falha(string erro) => new(false, null, erro);
}
