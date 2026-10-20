using EARS.Integration.Domain.Enums;

namespace EARS.Integration.Domain.Entities;

/// <summary>
/// Estado da conversa de agendamento, persistido a cada turno para que
/// uma reinicialização do serviço não interrompa um agendamento em andamento.
/// </summary>
public class Conversa : EntidadeBase
{
    private Conversa() { }

    public Conversa(
        string telefone,
        string? sleekFlowContatoId,
        MotivoAtendimento motivo,
        string? sleekFlowConversaId = null)
    {
        Telefone = telefone;
        SleekFlowContatoId = sleekFlowContatoId;
        SleekFlowConversaId = sleekFlowConversaId;
        Motivo = motivo;
        Estado = EstadoConversa.Inicio;
        UltimaInteracaoEm = DateTimeOffset.UtcNow;
    }

    public string Telefone { get; private set; } = null!;
    public string? SleekFlowContatoId { get; private set; }

    /// <summary>Id da conversa no SleekFlow, quando o Flow Builder o informa.</summary>
    public string? SleekFlowConversaId { get; private set; }

    /// <summary>Ramo do menu que originou o atendimento.</summary>
    public MotivoAtendimento Motivo { get; private set; }

    public EstadoConversa Estado { get; private set; }
    public DateTimeOffset UltimaInteracaoEm { get; private set; }

    /// <summary>
    /// Se a etiqueta agendamento-em-curso está aplicada no contato. Enquanto
    /// for verdadeira, o menu do Flow Builder não reabre — por isso ela
    /// precisa cair ao concluir, transferir ou expirar.
    /// </summary>
    public bool EtiquetaAplicada { get; private set; }

    // Dados coletados ao longo do fluxo.
    public string? NomeInformado { get; private set; }
    public string? CpfInformado { get; private set; }

    /// <summary>Falso quando o contato agenda para outra pessoa (mãe, filho).</summary>
    public bool? PacienteEhOContato { get; private set; }

    /// <summary>Idade do paciente. Decide quais profissionais podem atender.</summary>
    public int? IdadePaciente { get; private set; }

    /// <summary>Convênio informado, como o paciente escreveu.</summary>
    public string? Convenio { get; private set; }

    /// <summary>Preferência de dia e turno, em texto livre.</summary>
    public string? PreferenciaDiaTurno { get; private set; }

    /// <summary>Agendamento alvo da remarcação ou do cancelamento.</summary>
    public Guid? AgendamentoAlvoId { get; private set; }

    /// <summary>
    /// Coletada só quando o CPF não existe na base da Amplimed: o upsert do
    /// cadastrar-agendamento casa por CPF + data de nascimento.
    /// </summary>
    public DateOnly? DataNascimentoInformada { get; private set; }
    public Guid? PacienteId { get; private set; }

    /// <summary>
    /// Horários ofertados no último turno, em JSON, cada um já com o nome de
    /// quem atende. O paciente responde com o número da opção, então a lista
    /// precisa sobreviver entre turnos.
    ///
    /// Não há campo de profissional escolhido: a clínica trata a preferência
    /// de médica como opcional, e o motor não pergunta.
    /// </summary>
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
    public void RegistrarParaQuem(bool ehOContato) { PacienteEhOContato = ehOContato; Tocar(); }
    public void RegistrarIdade(int idade) { IdadePaciente = idade; Tocar(); }
    public void RegistrarConvenio(string convenio) { Convenio = convenio; Tocar(); }
    public void RegistrarPreferencia(string preferencia) { PreferenciaDiaTurno = preferencia; Tocar(); }
    public void RegistrarAgendamentoAlvo(Guid id) { AgendamentoAlvoId = id; Tocar(); }
    public void MarcarEtiquetaAplicada() { EtiquetaAplicada = true; Tocar(); }
    public void MarcarEtiquetaRemovida() { EtiquetaAplicada = false; Tocar(); }

    public void Expirar() => TransicionarPara(EstadoConversa.Expirada);
    public void VincularPaciente(Guid pacienteId) { PacienteId = pacienteId; Tocar(); }
    public void RegistrarHorariosOfertados(string json) { HorariosOfertadosJson = json; Tocar(); }

    public void TransferirParaHumano() => TransicionarPara(EstadoConversa.TransferidaParaHumano);

    /// <summary>Conversa parada há mais tempo que a janela configurada.</summary>
    public bool Expirada(TimeSpan janela) =>
        DateTimeOffset.UtcNow - UltimaInteracaoEm > janela;

    /// <summary>Estados em que a conversa já não espera nada do paciente.</summary>
    public bool EmEstadoFinal() =>
        Estado is EstadoConversa.Concluida
            or EstadoConversa.TransferidaParaHumano
            or EstadoConversa.Expirada;
}
