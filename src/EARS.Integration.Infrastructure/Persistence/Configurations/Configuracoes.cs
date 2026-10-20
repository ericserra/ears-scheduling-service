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
        // codReferencia = idUnico na API de Agendamento, codRef na listagem da agenda.
        b.Property(x => x.CodReferencia).HasMaxLength(64).IsRequired();
        b.Property(x => x.Nome).HasMaxLength(200).IsRequired();
        // CPF é opcional: a ordem de coleta da clínica pede nome e data de
        // nascimento, e o CPF só entra para desempatar homônimo (ADR 0004).
        b.Property(x => x.Cpf).HasMaxLength(11);
        b.Property(x => x.Telefone).HasMaxLength(20).IsRequired();
        b.Property(x => x.DataNascimento);
        b.Property(x => x.CodReferenciaVinculado).HasDefaultValue(false);

        // Único quando existe: dois pacientes não podem dividir CPF, mas
        // muitos podem não ter nenhum.
        b.HasIndex(x => x.Cpf).IsUnique().HasFilter("\"Cpf\" IS NOT NULL");
        b.HasIndex(x => new { x.Nome, x.DataNascimento });
        b.HasIndex(x => x.CodReferencia).IsUnique();
        b.HasIndex(x => x.Telefone);
    }
}

public class AgendamentoConfiguration : IEntityTypeConfiguration<Agendamento>
{
    public void Configure(EntityTypeBuilder<Agendamento> b)
    {
        b.ToTable("agendamentos");
        b.HasKey(x => x.Id);

        // Os dois espaços de identificador da Amplimed (ADR 0003). Ambos são
        // opcionais isoladamente: o que nasce no WhatsApp começa só com o UUID,
        // o que vem da agenda da clínica só com o par inteiro.
        b.Property(x => x.IdIntegracao).HasMaxLength(64);
        b.Property(x => x.Usuclin).HasMaxLength(32);
        b.Property(x => x.IdAgendamentoAmplimed);
        b.Property(x => x.IdHorario).HasMaxLength(200);

        b.Property(x => x.ProfissionalId).HasMaxLength(64);
        b.Property(x => x.ProfissionalNome).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Origem).HasConversion<int>();

        b.HasIndex(x => x.IdIntegracao).IsUnique().HasFilter("\"IdIntegracao\" IS NOT NULL");
        b.HasIndex(x => new { x.Usuclin, x.IdAgendamentoAmplimed })
            .IsUnique()
            .HasFilter("\"IdAgendamentoAmplimed\" IS NOT NULL");
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
        b.Property(x => x.SleekFlowConversaId).HasMaxLength(64);
        b.Property(x => x.Motivo).HasConversion<int>();
        b.Property(x => x.Estado).HasConversion<int>();

        // Espelha a etiqueta agendamento-em-curso no contato do SleekFlow.
        // O job de expiração lê esta coluna para saber de quem precisa tirar.
        b.Property(x => x.EtiquetaAplicada).HasDefaultValue(false);
        b.Property(x => x.PacienteEhOContato);
        b.Property(x => x.IdadePaciente);
        b.Property(x => x.Convenio).HasMaxLength(120);
        b.Property(x => x.PreferenciaDiaTurno).HasMaxLength(200);
        b.Property(x => x.AgendamentoAlvoId);
        b.Property(x => x.NomeInformado).HasMaxLength(200);
        b.Property(x => x.CpfInformado).HasMaxLength(11);
        b.Property(x => x.DataNascimentoInformada);
        b.Property(x => x.HorariosOfertadosJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.Telefone, x.Estado });

        // Varredura do job de expiração: conversas paradas, mais antigas primeiro.
        b.HasIndex(x => new { x.Estado, x.UltimaInteracaoEm });
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
