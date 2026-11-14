using EARS.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EARS.Integration.Infrastructure.Migrations
{
    /// <summary>
    /// Coluna que espelha a etiqueta aplicada no envio da confirmação D-1.
    /// Escrita à mão, sem Designer: o runtime só precisa dos dois atributos, e o
    /// snapshot já foi atualizado junto.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261114190000_EtiquetaConfirmacao")]
    public partial class EtiquetaConfirmacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EtiquetaConfirmacaoAplicada",
                table: "agendamentos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EtiquetaConfirmacaoAplicada",
                table: "agendamentos");
        }
    }
}
