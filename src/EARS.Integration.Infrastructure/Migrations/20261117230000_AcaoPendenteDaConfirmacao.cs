using System;
using EARS.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EARS.Integration.Infrastructure.Migrations
{
    /// <summary>
    /// Guarda a ação (confirmar, cancelar ou remarcar) que espera o "Sim" da segunda
    /// confirmação, para um clique errado no template não agir sozinho. Escrita à mão,
    /// sem Designer; o snapshot foi atualizado junto.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261117230000_AcaoPendenteDaConfirmacao")]
    public partial class AcaoPendenteDaConfirmacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcaoPendenteConfirmacao",
                table: "agendamentos",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcaoPendenteEm",
                table: "agendamentos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RespostasNaoEntendidas",
                table: "agendamentos",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AcaoPendenteConfirmacao", table: "agendamentos");
            migrationBuilder.DropColumn(name: "AcaoPendenteEm", table: "agendamentos");
            migrationBuilder.DropColumn(name: "RespostasNaoEntendidas", table: "agendamentos");
        }
    }
}
