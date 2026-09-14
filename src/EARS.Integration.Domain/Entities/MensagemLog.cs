using EARS.Integration.Domain.Enums;

namespace EARS.Integration.Domain.Entities;

/// <summary>
/// Trilha de auditoria exigida em contrato: toda mensagem enviada e recebida
/// fica registrada com data, hora, destinatário e resultado.
/// </summary>
public class MensagemLog : EntidadeBase
{
    private MensagemLog() { }

    public MensagemLog(
        string telefone,
        TipoMensagem tipo,
        string? templateNome,
        string conteudo,
        Guid? agendamentoId,
        bool sucesso,
        string? erro = null)
    {
        Telefone = telefone;
        Tipo = tipo;
        TemplateNome = templateNome;
        Conteudo = conteudo;
        AgendamentoId = agendamentoId;
        Sucesso = sucesso;
        Erro = erro;
    }

    public string Telefone { get; private set; } = null!;
    public TipoMensagem Tipo { get; private set; }
    public string? TemplateNome { get; private set; }
    public string Conteudo { get; private set; } = null!;
    public Guid? AgendamentoId { get; private set; }
    public bool Sucesso { get; private set; }
    public string? Erro { get; private set; }
}
