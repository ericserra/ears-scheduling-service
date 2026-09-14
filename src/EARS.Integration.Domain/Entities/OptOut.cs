namespace EARS.Integration.Domain.Entities;

/// <summary>Lista de descadastro. Verificada antes de qualquer envio.</summary>
public class OptOut : EntidadeBase
{
    private OptOut() { }

    public OptOut(string telefone, string? motivo)
    {
        Telefone = telefone;
        Motivo = motivo;
    }

    public string Telefone { get; private set; } = null!;
    public string? Motivo { get; private set; }
}
