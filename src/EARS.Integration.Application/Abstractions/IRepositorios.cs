using EARS.Integration.Domain.Entities;

namespace EARS.Integration.Application.Abstractions;

public interface IPacienteRepository
{
    Task<Paciente?> ObterPorCpfAsync(string cpf, CancellationToken ct = default);
    Task<Paciente?> ObterPorAmplimedIdAsync(string amplimedId, CancellationToken ct = default);
    Task AdicionarAsync(Paciente paciente, CancellationToken ct = default);
}

public interface IAgendamentoRepository
{
    Task<Agendamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Agendamento?> ObterPorAmplimedIdAsync(string amplimedId, CancellationToken ct = default);

    /// <summary>Agendamentos do dia informado, para o disparo D-1 (espelho local, cenário B).</summary>
    Task<IReadOnlyList<Agendamento>> ListarPorDiaAsync(DateOnly dia, CancellationToken ct = default);

    /// <summary>Último agendamento ativo do telefone, para correlacionar a resposta ao botão.</summary>
    Task<Agendamento?> ObterAtivoMaisRecentePorTelefoneAsync(string telefone, CancellationToken ct = default);

    Task AdicionarAsync(Agendamento agendamento, CancellationToken ct = default);
}

public interface IConversaRepository
{
    Task<Conversa?> ObterAtivaPorTelefoneAsync(string telefone, CancellationToken ct = default);
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
