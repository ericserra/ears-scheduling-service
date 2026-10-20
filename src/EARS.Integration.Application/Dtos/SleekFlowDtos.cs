namespace EARS.Integration.Application.Dtos;

/// <summary>Payload normalizado do webhook de mensagem recebida.</summary>
public sealed record MensagemRecebida(
    string Telefone,
    string? ContatoId,
    string? Texto,
    string? BotaoPayload,
    DateTimeOffset RecebidaEm);

/// <summary>
/// Parâmetro de template. O <see cref="Nome"/> é só para log e leitura: a
/// Meta casa os parâmetros por <em>posição</em>, então a ordem da lista é o
/// que importa.
/// </summary>
public sealed record VariavelTemplate(string Nome, string Valor);

public sealed record EnvioTemplateRequest(
    string Telefone,
    string TemplateNome,
    IReadOnlyList<VariavelTemplate> Variaveis,
    string? Idioma = null);

/// <summary>Botão de resposta rápida. O WhatsApp aceita no máximo três.</summary>
public sealed record BotaoResposta(string Id, string Titulo);

public sealed record EnvioBotoesRequest(
    string Telefone,
    string Texto,
    IReadOnlyList<BotaoResposta> Botoes);

public sealed record ResultadoEnvio(bool Sucesso, string? MensagemId, string? Erro)
{
    public static ResultadoEnvio Ok(string? id) => new(true, id, null);
    public static ResultadoEnvio Falha(string erro) => new(false, null, erro);
}
