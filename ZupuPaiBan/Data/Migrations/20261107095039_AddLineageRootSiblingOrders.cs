using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZupuPaiBan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLineageRootSiblingOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LineageRootSiblingOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EvidenceGeneration = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineageRootSiblingOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineageRootSiblingOrders_LineageCharts_ChartId",
                        column: x => x.ChartId,
                        principalTable: "LineageCharts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LineageRootSiblingOrderItems",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Sex = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordedGeneration = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineageRootSiblingOrderItems", x => new { x.GroupId, x.PersonId });
                    table.CheckConstraint("CK_LineageRootSiblingOrderItems_Position", "Position > 0");
                    table.ForeignKey(
                        name: "FK_LineageRootSiblingOrderItems_LineageRootSiblingOrders_GroupId",
                        column: x => x.GroupId,
                        principalTable: "LineageRootSiblingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LineageRootSiblingOrderItems_GroupId_Position",
                table: "LineageRootSiblingOrderItems",
                columns: new[] { "GroupId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineageRootSiblingOrders_ChartId",
                table: "LineageRootSiblingOrders",
                column: "ChartId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LineageRootSiblingOrderItems");

            migrationBuilder.DropTable(
                name: "LineageRootSiblingOrders");
        }
    }
}
