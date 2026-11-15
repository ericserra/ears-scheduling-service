using EARS.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EARS.Integration.Infrastructure.Migrations
{
    /// <summary>
    /// Sexo biológico coletado no chatbot, exigido pelo cadastrar-agendamento da
    /// Amplimed. Escrita à mão, sem Designer, como a anterior; o snapshot foi
    /// atualizado junto.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261115150000_SexoBiologicoNaConversa")]
    public partial class SexoBiologicoNaConversa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SexoBiologicoInformado",
                table: "conversas",
                type: "character varying(1)",
                maxLength: 1,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SexoBiologicoInformado",
                table: "conversas");
        }
    }
}
