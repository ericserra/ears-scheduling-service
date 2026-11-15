using EARS.Integration.Domain.Entities;

namespace EARS.Integration.Application.Abstractions;

public interface IPacienteRepository
{
    Task<Paciente?> ObterPorIdAsync(Guid id, CancellationToken ct = default);

    Task<Paciente?> ObterPorCpfAsync(string cpf, CancellationToken ct = default);

    /// <summary>
    /// Busca pelo par nome + data de nascimento, que é a ordem de coleta da
    /// clínica. Devolve a lista inteira: mais de um resultado significa
    /// homônimos, e aí o CPF entra para desempatar.
    /// </summary>
    Task<IReadOnlyList<Paciente>> ListarPorNomeEDataNascimentoAsync(
        string nome, DateOnly dataNascimento, CancellationToken ct = default);

    /// <summary>Busca pelo codReferencia cunhado por este serviço (idUnico/codRef na Amplimed).</summary>
    Task<Paciente?> ObterPorCodReferenciaAsync(string codReferencia, CancellationToken ct = default);

    /// <summary>
    /// Único caminho para resolver telefone, já que a API de Pacientes não
    /// oferece busca por celular. Serve a mensagens recebidas de quem já é conhecido.
    /// </summary>
    Task<Paciente?> ObterPorTelefoneAsync(string telefone, CancellationToken ct = default);

    /// <summary>
    /// Telefone + nome: o par que identifica quem chegou pela agenda sem CPF. Só o
    /// telefone não basta — mãe e filhos costumam compartilhar o mesmo número.
    /// </summary>
    Task<Paciente?> ObterPorTelefoneENomeAsync(string telefone, string nome, CancellationToken ct = default);

    Task AdicionarAsync(Paciente paciente, CancellationToken ct = default);
}

public interface IAgendamentoRepository
{
    Task<Agendamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Pelo UUID devolvido por cadastrar-agendamento.</summary>
    Task<Agendamento?> ObterPorIdIntegracaoAsync(string idIntegracao, CancellationToken ct = default);

    /// <summary>Pelo par (usuclin, idAgendamento) da listagem da agenda.</summary>
    Task<Agendamento?> ObterPorIdAmplimedAsync(
        string usuclin, long idAgendamento, CancellationToken ct = default);

    /// <summary>
    /// Agendamento do paciente naquele horário que ainda não tem o par inteiro da
    /// agenda. É o que permite casar o que nasceu no WhatsApp com a linha
    /// correspondente em listar-agendamentos, em vez de duplicar o espelho.
    /// </summary>
    Task<Agendamento?> ObterSemVinculoDaAgendaAsync(
        Guid pacienteId, DateTimeOffset inicioEm, CancellationToken ct = default);

    /// <summary>Agendamentos do dia informado, base de leitura do disparo D-1 e do lembrete.</summary>
    Task<IReadOnlyList<Agendamento>> ListarPorDiaAsync(DateOnly dia, CancellationToken ct = default);

    /// <summary>Último agendamento ativo do telefone, para correlacionar a resposta ao botão.</summary>
    Task<Agendamento?> ObterAtivoMaisRecentePorTelefoneAsync(string telefone, CancellationToken ct = default);

    /// <summary>
    /// Consultas futuras do telefone ainda por confirmar (Agendado ou NaoConfirmado),
    /// da mais próxima para a mais distante. Serve para decidir se um "confirmar"
    /// sem template tem um alvo único.
    /// </summary>
    Task<IReadOnlyList<Agendamento>> ListarAtivosFuturosPorTelefoneAsync(
        string telefone, DateTimeOffset aPartirDe, int maximo, CancellationToken ct = default);

    /// <summary>
    /// Agendamentos cuja etiqueta da confirmação D-1 segue no contato e cujo
    /// envio é anterior ao limite. Base da liberação do menu para quem nunca
    /// respondeu ao template.
    /// </summary>
    Task<IReadOnlyList<Agendamento>> ListarComEtiquetaDeConfirmacaoAsync(
        DateTimeOffset enviadosAntesDe, int maximo, CancellationToken ct = default);

    Task AdicionarAsync(Agendamento agendamento, CancellationToken ct = default);
}

public interface IConversaRepository
{
    Task<Conversa?> ObterAtivaPorTelefoneAsync(string telefone, CancellationToken ct = default);

    /// <summary>
    /// Conversas paradas além da janela e ainda não encerradas. Base do job de
    /// expiração, que é o que tira a etiqueta de quem desistiu no meio — sem
    /// ele o paciente fica sem menu para sempre.
    /// </summary>
    Task<IReadOnlyList<Conversa>> ListarParadasAsync(
        DateTimeOffset limite, int maximo, CancellationToken ct = default);

    Task AdicionarAsync(Conversa conversa, CancellationToken ct = default);
}

public interface IMensagemLogRepository
{
    Task AdicionarAsync(MensagemLog log, CancellationToken ct = default);
}

public interface IOptOutRepository
{
    Task<bool> EstaDescadastradoAsync(string telefone, CancellationToken ct = default);
    Task AdicionarAsync(OptOut optOut, CancellationToken ct = default);
}

public interface IOutboxRepository
{
    Task AdicionarAsync(OutboxMessage mensagem, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxMessage>> ListarPendentesAsync(int limite, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SalvarAsync(CancellationToken ct = default);
}
