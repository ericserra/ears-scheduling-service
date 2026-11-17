using EARS.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EARS.Integration.Infrastructure.Migrations
{
    /// <summary>
    /// Conta as respostas seguidas que o bot não entendeu na confirmação: abaixo do
    /// limite o paciente fica no fluxo, acima dele a recepção assume. Escrita à mão,
    /// sem Designer; o snapshot foi atualizado junto.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261117233000_RespostasNaoEntendidas")]
    public partial class RespostasNaoEntendidas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
            migrationBuilder.DropColumn(
                name: "RespostasNaoEntendidas",
                table: "agendamentos");
        }
    }
}
