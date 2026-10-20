using EARS.Integration.Domain.ValueObjects;

namespace EARS.Integration.Domain.Entities;

/// <summary>
/// Espelho mínimo do paciente. A fonte da verdade é a Amplimed; aqui guardamos
/// apenas o necessário para correlacionar conversa, agendamento e envio.
/// </summary>
/// <remarks>
/// A Amplimed não expõe um id próprio de paciente nas APIs de integração: o
/// identificador é o <c>codReferencia</c> (chamado <c>idUnico</c> na API de
/// Agendamento e devolvido como <c>codRef</c> na listagem da agenda), e quem o
/// cunha é o integrador. Por isso ele é derivado do <see cref="EntidadeBase.Id"/>
/// local: estável para sempre, único por paciente e sem CPF embutido.
///
/// A documentação é explícita: reaproveitar o mesmo valor para pacientes
/// diferentes mescla cadastros silenciosamente.
/// </remarks>
public class Paciente : EntidadeBase
{
    private Paciente() { }

    public Paciente(string nome, Telefone telefone, DateOnly? dataNascimento = null, Cpf? cpf = null)
    {
        Nome = nome;
        Telefone = telefone.Value;
        DataNascimento = dataNascimento;
        Cpf = cpf?.Value;
        CodReferencia = GerarCodReferencia(Id);
    }

    /// <summary>Prefixo do codReferencia cunhado por este serviço.</summary>
    public const string PrefixoCodReferencia = "ears-";

    public static string GerarCodReferencia(Guid id) => $"{PrefixoCodReferencia}{id:N}";

    /// <summary>
    /// Identificador do paciente nas APIs da Amplimed: <c>codReferencia</c> na API
    /// de Pacientes, <c>idUnico</c> na de Agendamento, <c>codRef</c> na listagem.
    /// </summary>
    public string CodReferencia { get; private set; } = null!;

    public string Nome { get; private set; } = null!;

    /// <summary>
    /// Opcional. A ordem de coleta definida pela clínica pede nome e data de
    /// nascimento, não CPF; ele só é pedido quando esses dois não resolvem o
    /// paciente na Amplimed. Ver ADR 0004.
    /// </summary>
    public string? Cpf { get; private set; }

    public string Telefone { get; private set; } = null!;

    /// <summary>
    /// Necessária para o upsert do <c>cadastrar-agendamento</c>, que casa por
    /// CPF + data de nascimento quando o idUnico ainda não existe na base.
    /// </summary>
    public DateOnly? DataNascimento { get; private set; }

    /// <summary>
    /// Verdadeiro quando o <c>PUT /codreferencia</c> já vinculou este
    /// codReferencia ao cadastro que a Amplimed tinha para o CPF.
    /// </summary>
    public bool CodReferenciaVinculado { get; private set; }

    public void AtualizarContato(string nome, Telefone telefone)
    {
        Nome = nome;
        Telefone = telefone.Value;
        Tocar();
    }

    public void RegistrarDataNascimento(DateOnly data)
    {
        DataNascimento = data;
        Tocar();
    }

    /// <summary>Preenche o CPF quando ele aparece depois, na conversa ou na Amplimed.</summary>
    public void DefinirCpf(Cpf cpf)
    {
        Cpf = cpf.Value;
        Tocar();
    }

    public void MarcarCodReferenciaVinculado()
    {
        CodReferenciaVinculado = true;
        Tocar();
    }
}
