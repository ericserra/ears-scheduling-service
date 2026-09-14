using EARS.Integration.Domain.ValueObjects;

namespace EARS.Integration.Domain.Entities;

/// <summary>
/// Espelho mínimo do paciente. A fonte da verdade é a Amplimed;
/// aqui guardamos apenas o necessário para correlacionar conversa e agendamento.
/// </summary>
public class Paciente : EntidadeBase
{
    private Paciente() { }

    public Paciente(string amplimedId, string nome, Cpf cpf, Telefone telefone)
    {
        AmplimedId = amplimedId;
        Nome = nome;
        Cpf = cpf.Value;
        Telefone = telefone.Value;
    }

    /// <summary>Identificador do paciente na Amplimed.</summary>
    public string AmplimedId { get; private set; } = null!;
    public string Nome { get; private set; } = null!;
    public string Cpf { get; private set; } = null!;
    public string Telefone { get; private set; } = null!;

    public void AtualizarContato(string nome, Telefone telefone)
    {
        Nome = nome;
        Telefone = telefone.Value;
        Tocar();
    }
}
