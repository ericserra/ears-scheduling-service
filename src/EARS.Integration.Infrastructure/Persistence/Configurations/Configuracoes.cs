using EARS.Integration.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EARS.Integration.Infrastructure.Persistence.Configurations;

public class PacienteConfiguration : IEntityTypeConfiguration<Paciente>
{
    public void Configure(EntityTypeBuilder<Paciente> b)
    {
        b.ToTable("pacientes");
        b.HasKey(x => x.Id);
        b.Property(x => x.AmplimedId).HasMaxLength(64).IsRequired();
        b.Property(x => x.Nome).HasMaxLength(200).IsRequired();
        b.Property(x => x.Cpf).HasMaxLength(11).IsRequired();
        b.Property(x => x.Telefone).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Cpf).IsUnique();
        b.HasIndex(x => x.AmplimedId).IsUnique();
        b.HasIndex(x => x.Telefone);
    }
}

public class AgendamentoConfiguration : IEntityTypeConfiguration<Agendamento>
{
    public void Configure(EntityTypeBuilder<Agendamento> b)
    {
        b.ToTable("agendamentos");
        b.HasKey(x => x.Id);
        b.Property(x => x.AmplimedId).HasMaxLength(64).IsRequired();
        b.Property(x => x.ProfissionalId).HasMaxLength(64).IsRequired();
        b.Property(x => x.ProfissionalNome).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Origem).HasConversion<int>();
        b.HasIndex(x => x.AmplimedId).IsUnique();
        b.HasIndex(x => x.InicioEm);
        b.HasOne(x => x.Paciente)
            .WithMany()
            .HasForeignKey(x => x.PacienteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ConversaConfiguration : IEntityTypeConfiguration<Conversa>
{
    public void Configure(EntityTypeBuilder<Conversa> b)
    {
        b.ToTable("conversas");
        b.HasKey(x => x.Id);
        b.Property(x => x.Telefone).HasMaxLength(20).IsRequired();
        b.Property(x => x.SleekFlowContatoId).HasMaxLength(64);
        b.Property(x => x.Estado).HasConversion<int>();
        b.Property(x => x.NomeInformado).HasMaxLength(200);
        b.Property(x => x.CpfInformado).HasMaxLength(11);
        b.Property(x => x.ProfissionalId).HasMaxLength(64);
        b.Property(x => x.HorariosOfertadosJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.Telefone, x.Estado });
    }
}

public class MensagemLogConfiguration : IEntityTypeConfiguration<MensagemLog>
{
    public void Configure(EntityTypeBuilder<MensagemLog> b)
    {
        b.ToTable("mensagens_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Telefone).HasMaxLength(20).IsRequired();
        b.Property(x => x.Tipo).HasConversion<int>();
        b.Property(x => x.TemplateNome).HasMaxLength(120);
        b.Property(x => x.Conteudo).HasMaxLength(4000).IsRequired();
        b.Property(x => x.Erro).HasMaxLength(2000);
        b.HasIndex(x => x.CriadoEm);
        b.HasIndex(x => x.AgendamentoId);
    }
}

public class OptOutConfiguration : IEntityTypeConfiguration<OptOut>
{
    public void Configure(EntityTypeBuilder<OptOut> b)
    {
        b.ToTable("opt_out");
        b.HasKey(x => x.Id);
        b.Property(x => x.Telefone).HasMaxLength(20).IsRequired();
        b.Property(x => x.Motivo).HasMaxLength(500);
        b.HasIndex(x => x.Telefone).IsUnique();
    }
}

public class OutboxConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox");
        b.HasKey(x => x.Id);
        b.Property(x => x.Tipo).HasMaxLength(120).IsRequired();
        b.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.UltimoErro).HasMaxLength(2000);
        b.HasIndex(x => new { x.Status, x.ProximaTentativaEm });
    }
}
