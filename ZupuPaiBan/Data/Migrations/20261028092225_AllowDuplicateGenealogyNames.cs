using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZupuPaiBan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowDuplicateGenealogyNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Genealogies_Name",
                table: "Genealogies");

            migrationBuilder.CreateIndex(
                name: "IX_Genealogies_Name",
                table: "Genealogies",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Genealogies_Name",
                table: "Genealogies");

            migrationBuilder.CreateIndex(
                name: "IX_Genealogies_Name",
                table: "Genealogies",
                column: "Name",
                unique: true);
        }
    }
}
