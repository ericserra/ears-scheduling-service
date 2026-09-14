using EARS.Integration.Application.Abstractions;
using EARS.Integration.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EARS.Integration.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Paciente> Pacientes => Set<Paciente>();
    public DbSet<Agendamento> Agendamentos => Set<Agendamento>();
    public DbSet<Conversa> Conversas => Set<Conversa>();
    public DbSet<MensagemLog> MensagensLog => Set<MensagemLog>();
    public DbSet<OptOut> OptOuts => Set<OptOut>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public Task<int> SalvarAsync(CancellationToken ct = default) => SaveChangesAsync(ct);
}
