using EARS.Integration.Application.Abstractions;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EARS.Integration.Infrastructure.Persistence;

public sealed class PacienteRepository(AppDbContext db) : IPacienteRepository
{
    public Task<Paciente?> ObterPorCpfAsync(string cpf, CancellationToken ct = default) =>
        db.Pacientes.FirstOrDefaultAsync(p => p.Cpf == cpf, ct);

    public Task<Paciente?> ObterPorAmplimedIdAsync(string amplimedId, CancellationToken ct = default) =>
        db.Pacientes.FirstOrDefaultAsync(p => p.AmplimedId == amplimedId, ct);

    public async Task AdicionarAsync(Paciente paciente, CancellationToken ct = default) =>
        await db.Pacientes.AddAsync(paciente, ct);
}

public sealed class AgendamentoRepository(AppDbContext db) : IAgendamentoRepository
{
    public Task<Agendamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default) =>
        db.Agendamentos.Include(a => a.Paciente).FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Agendamento?> ObterPorAmplimedIdAsync(string amplimedId, CancellationToken ct = default) =>
        db.Agendamentos.Include(a => a.Paciente).FirstOrDefaultAsync(a => a.AmplimedId == amplimedId, ct);

    public async Task<IReadOnlyList<Agendamento>> ListarPorDiaAsync(DateOnly dia, CancellationToken ct = default)
    {
        var inicio = new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fim = inicio.AddDays(1);

        return await db.Agendamentos
            .Include(a => a.Paciente)
            .Where(a => a.InicioEm >= inicio && a.InicioEm < fim)
            .OrderBy(a => a.InicioEm)
            .ToListAsync(ct);
    }

    public Task<Agendamento?> ObterAtivoMaisRecentePorTelefoneAsync(string telefone, CancellationToken ct = default) =>
        db.Agendamentos
            .Include(a => a.Paciente)
            .Where(a => a.Paciente != null
                        && a.Paciente.Telefone == telefone
                        && a.Status == StatusAgendamento.Agendado)
            .OrderByDescending(a => a.ConfirmacaoEnviadaEm)
            .ThenByDescending(a => a.InicioEm)
            .FirstOrDefaultAsync(ct);

    public async Task AdicionarAsync(Agendamento agendamento, CancellationToken ct = default) =>
        await db.Agendamentos.AddAsync(agendamento, ct);
}

public sealed class ConversaRepository(AppDbContext db) : IConversaRepository
{
    private static readonly EstadoConversa[] Finais =
    [
        EstadoConversa.Concluida,
        EstadoConversa.TransferidaParaHumano,
        EstadoConversa.Expirada
    ];

    public Task<Conversa?> ObterAtivaPorTelefoneAsync(string telefone, CancellationToken ct = default) =>
        db.Conversas
            .Where(c => c.Telefone == telefone && !Finais.Contains(c.Estado))
            .OrderByDescending(c => c.UltimaInteracaoEm)
            .FirstOrDefaultAsync(ct);

    public async Task AdicionarAsync(Conversa conversa, CancellationToken ct = default) =>
        await db.Conversas.AddAsync(conversa, ct);
}

public sealed class MensagemLogRepository(AppDbContext db) : IMensagemLogRepository
{
    public async Task AdicionarAsync(MensagemLog log, CancellationToken ct = default) =>
        await db.MensagensLog.AddAsync(log, ct);
}

public sealed class OptOutRepository(AppDbContext db) : IOptOutRepository
{
    public Task<bool> EstaDescadastradoAsync(string telefone, CancellationToken ct = default) =>
        db.OptOuts.AnyAsync(o => o.Telefone == telefone, ct);

    public async Task AdicionarAsync(OptOut optOut, CancellationToken ct = default) =>
        await db.OptOuts.AddAsync(optOut, ct);
}

public sealed class OutboxRepository(AppDbContext db) : IOutboxRepository
{
    public async Task AdicionarAsync(OutboxMessage mensagem, CancellationToken ct = default) =>
        await db.Outbox.AddAsync(mensagem, ct);

    public async Task<IReadOnlyList<OutboxMessage>> ListarPendentesAsync(int limite, CancellationToken ct = default)
    {
        var agora = DateTimeOffset.UtcNow;
        return await db.Outbox
            .Where(o => o.Status == StatusOutbox.Pendente && o.ProximaTentativaEm <= agora)
            .OrderBy(o => o.ProximaTentativaEm)
            .Take(limite)
            .ToListAsync(ct);
    }
}
