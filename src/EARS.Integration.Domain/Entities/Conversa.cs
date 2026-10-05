using EARS.Integration.Domain.Enums;

namespace EARS.Integration.Domain.Entities;

/// <summary>
/// Estado da conversa de agendamento, persistido a cada turno para que
/// uma reinicialização do serviço não interrompa um agendamento em andamento.
/// </summary>
public class Conversa : EntidadeBase
{
    private Conversa() { }

    public Conversa(string telefone, string? sleekFlowContatoId)
    {
        Telefone = telefone;
        SleekFlowContatoId = sleekFlowContatoId;
        Estado = EstadoConversa.Inicio;
        UltimaInteracaoEm = DateTimeOffset.UtcNow;
    }

    public string Telefone { get; private set; } = null!;
    public string? SleekFlowContatoId { get; private set; }
    public EstadoConversa Estado { get; private set; }
    public DateTimeOffset UltimaInteracaoEm { get; private set; }

    // Dados coletados ao longo do fluxo.
    public string? NomeInformado { get; private set; }
    public string? CpfInformado { get; private set; }

    /// <summary>
    /// Coletada só quando o CPF não existe na base da Amplimed: o upsert do
    /// cadastrar-agendamento casa por CPF + data de nascimento.
    /// </summary>
    public DateOnly? DataNascimentoInformada { get; private set; }
    public Guid? PacienteId { get; private set; }
    public string? ProfissionalId { get; private set; }

    /// <summary>
    /// Profissionais ofertados no último turno, em JSON. Guardados porque o
    /// paciente responde com o número da opção, não com o código do profissional.
    /// </summary>
    public string? ProfissionaisOfertadosJson { get; private set; }

    /// <summary>Horários ofertados no último turno, em JSON, para validar a escolha.</summary>
    public string? HorariosOfertadosJson { get; private set; }

    public void TransicionarPara(EstadoConversa estado)
    {
        Estado = estado;
        UltimaInteracaoEm = DateTimeOffset.UtcNow;
        Tocar();
    }

    public void RegistrarNome(string nome) { NomeInformado = nome; Tocar(); }
    public void RegistrarCpf(string cpf) { CpfInformado = cpf; Tocar(); }
    public void RegistrarDataNascimento(DateOnly data) { DataNascimentoInformada = data; Tocar(); }
    public void VincularPaciente(Guid pacienteId) { PacienteId = pacienteId; Tocar(); }
    public void RegistrarProfissional(string profissionalId) { ProfissionalId = profissionalId; Tocar(); }
    public void RegistrarProfissionaisOfertados(string json) { ProfissionaisOfertadosJson = json; Tocar(); }
    public void RegistrarHorariosOfertados(string json) { HorariosOfertadosJson = json; Tocar(); }

    public void TransferirParaHumano() => TransicionarPara(EstadoConversa.TransferidaParaHumano);

    /// <summary>Conversa parada há mais tempo que a janela configurada.</summary>
    public bool Expirada(TimeSpan janela) =>
        DateTimeOffset.UtcNow - UltimaInteracaoEm > janela;
}
