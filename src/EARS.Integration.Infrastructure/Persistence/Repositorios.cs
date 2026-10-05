using EARS.Integration.Application.Abstractions;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EARS.Integration.Infrastructure.Persistence;

public sealed class PacienteRepository(AppDbContext db) : IPacienteRepository
{
    public Task<Paciente?> ObterPorCpfAsync(string cpf, CancellationToken ct = default) =>
        db.Pacientes.FirstOrDefaultAsync(p => p.Cpf == cpf, ct);

    public Task<Paciente?> ObterPorCodReferenciaAsync(string codReferencia, CancellationToken ct = default) =>
        db.Pacientes.FirstOrDefaultAsync(p => p.CodReferencia == codReferencia, ct);

    public Task<Paciente?> ObterPorTelefoneAsync(string telefone, CancellationToken ct = default) =>
        db.Pacientes
            .Where(p => p.Telefone == telefone)
            .OrderByDescending(p => p.AtualizadoEm)
            .FirstOrDefaultAsync(ct);

    public async Task AdicionarAsync(Paciente paciente, CancellationToken ct = default) =>
        await db.Pacientes.AddAsync(paciente, ct);
}

public sealed class AgendamentoRepository(AppDbContext db, IRelogio relogio) : IAgendamentoRepository
{
    /// <summary>
    /// Tolerância ao casar o agendamento criado pela integração com a linha
    /// correspondente em listar-agendamentos. Os dois instantes vêm da mesma
    /// conversão de fuso, mas a API pode arredondar o minuto.
    /// </summary>
    private static readonly TimeSpan ToleranciaVinculo = TimeSpan.FromMinutes(1);

    public Task<Agendamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default) =>
        db.Agendamentos.Include(a => a.Paciente).FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Agendamento?> ObterPorIdIntegracaoAsync(string idIntegracao, CancellationToken ct = default) =>
        db.Agendamentos
            .Include(a => a.Paciente)
            .FirstOrDefaultAsync(a => a.IdIntegracao == idIntegracao, ct);

    public Task<Agendamento?> ObterPorIdAmplimedAsync(
        string usuclin, long idAgendamento, CancellationToken ct = default) =>
        db.Agendamentos
            .Include(a => a.Paciente)
            .FirstOrDefaultAsync(
                a => a.Usuclin == usuclin && a.IdAgendamentoAmplimed == idAgendamento, ct);

    public Task<Agendamento?> ObterSemVinculoDaAgendaAsync(
        Guid pacienteId, DateTimeOffset inicioEm, CancellationToken ct = default)
    {
        var de = inicioEm - ToleranciaVinculo;
        var ate = inicioEm + ToleranciaVinculo;

        return db.Agendamentos
            .Where(a => a.PacienteId == pacienteId
                        && a.IdAgendamentoAmplimed == null
                        && a.InicioEm >= de
                        && a.InicioEm <= ate)
            .OrderBy(a => a.InicioEm)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Agendamento>> ListarPorDiaAsync(DateOnly dia, CancellationToken ct = default)
    {
        // A borda do dia é a do fuso da clínica: em UTC, consultas das 21h
        // cairiam no dia seguinte e as da noite anterior entrariam por engano.
        var inicio = relogio.InicioDoDiaLocal(dia);
        var fim = relogio.InicioDoDiaLocal(dia.AddDays(1));

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
