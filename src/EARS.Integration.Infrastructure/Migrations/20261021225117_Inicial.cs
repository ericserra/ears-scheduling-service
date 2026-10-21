using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EARS.Integration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SleekFlowContatoId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SleekFlowConversaId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Motivo = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    UltimaInteracaoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EtiquetaAplicada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    NomeInformado = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CpfInformado = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    PacienteEhOContato = table.Column<bool>(type: "boolean", nullable: true),
                    IdadePaciente = table.Column<int>(type: "integer", nullable: true),
                    Convenio = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PreferenciaDiaTurno = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AgendamentoAlvoId = table.Column<Guid>(type: "uuid", nullable: true),
                    DataNascimentoInformada = table.Column<DateOnly>(type: "date", nullable: true),
                    PacienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    HorariosOfertadosJson = table.Column<string>(type: "jsonb", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mensagens_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    TemplateNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Conteudo = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AgendamentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sucesso = table.Column<bool>(type: "boolean", nullable: false),
                    Erro = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensagens_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "opt_out",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opt_out", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    ProximaTentativaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UltimoErro = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pacientes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodReferencia = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Cpf = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DataNascimento = table.Column<DateOnly>(type: "date", nullable: true),
                    CodReferenciaVinculado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pacientes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agendamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdIntegracao = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IdHorario = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Usuclin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IdAgendamentoAmplimed = table.Column<long>(type: "bigint", nullable: true),
                    PacienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfissionalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProfissionalNome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    InicioEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DuracaoMinutos = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Origem = table.Column<int>(type: "integer", nullable: false),
                    ConfirmacaoEnviadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LembreteEnviadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RespondidoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SincronizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agendamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agendamentos_pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_IdIntegracao",
                table: "agendamentos",
                column: "IdIntegracao",
                unique: true,
                filter: "\"IdIntegracao\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_InicioEm",
                table: "agendamentos",
                column: "InicioEm");

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_PacienteId",
                table: "agendamentos",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_Usuclin_IdAgendamentoAmplimed",
                table: "agendamentos",
                columns: new[] { "Usuclin", "IdAgendamentoAmplimed" },
                unique: true,
                filter: "\"IdAgendamentoAmplimed\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_conversas_Estado_UltimaInteracaoEm",
                table: "conversas",
                columns: new[] { "Estado", "UltimaInteracaoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_conversas_Telefone_Estado",
                table: "conversas",
                columns: new[] { "Telefone", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_mensagens_log_AgendamentoId",
                table: "mensagens_log",
                column: "AgendamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_mensagens_log_CriadoEm",
                table: "mensagens_log",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_opt_out_Telefone",
                table: "opt_out",
                column: "Telefone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_Status_ProximaTentativaEm",
                table: "outbox",
                columns: new[] { "Status", "ProximaTentativaEm" });

            migrationBuilder.CreateIndex(
                name: "IX_pacientes_CodReferencia",
                table: "pacientes",
                column: "CodReferencia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pacientes_Cpf",
                table: "pacientes",
                column: "Cpf",
                unique: true,
                filter: "\"Cpf\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_pacientes_Nome_DataNascimento",
                table: "pacientes",
                columns: new[] { "Nome", "DataNascimento" });

            migrationBuilder.CreateIndex(
                name: "IX_pacientes_Telefone",
                table: "pacientes",
                column: "Telefone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agendamentos");

            migrationBuilder.DropTable(
                name: "conversas");

            migrationBuilder.DropTable(
                name: "mensagens_log");

            migrationBuilder.DropTable(
                name: "opt_out");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "pacientes");
        }
    }
}
