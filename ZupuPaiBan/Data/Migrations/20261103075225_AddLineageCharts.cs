using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZupuPaiBan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLineageCharts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LineageCharts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GenealogyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Generation = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineageCharts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineageCharts_Genealogies_GenealogyId",
                        column: x => x.GenealogyId,
                        principalTable: "Genealogies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LineageChartChoices",
                columns: table => new
                {
                    ChartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineageChartChoices", x => new { x.ChartId, x.PersonId });
                    table.ForeignKey(
                        name: "FK_LineageChartChoices_LineageCharts_ChartId",
                        column: x => x.ChartId,
                        principalTable: "LineageCharts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LineageCharts_GenealogyId",
                table: "LineageCharts",
                column: "GenealogyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LineageChartChoices");

            migrationBuilder.DropTable(
                name: "LineageCharts");
        }
    }
}
