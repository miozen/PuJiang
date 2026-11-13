using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZupuPaiBan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLineageSpouseOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LineageSpouseOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EvidenceGeneration = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineageSpouseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineageSpouseOrders_LineageCharts_ChartId",
                        column: x => x.ChartId,
                        principalTable: "LineageCharts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LineageSpouseOrderItems",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UnionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartnerOneId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartnerTwoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineageSpouseOrderItems", x => new { x.GroupId, x.UnionId });
                    table.CheckConstraint("CK_LineageSpouseOrderItems_Position", "Position > 0");
                    table.ForeignKey(
                        name: "FK_LineageSpouseOrderItems_LineageSpouseOrders_GroupId",
                        column: x => x.GroupId,
                        principalTable: "LineageSpouseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LineageSpouseOrderItems_GroupId_Position",
                table: "LineageSpouseOrderItems",
                columns: new[] { "GroupId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineageSpouseOrders_ChartId_OwnerId",
                table: "LineageSpouseOrders",
                columns: new[] { "ChartId", "OwnerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LineageSpouseOrderItems");

            migrationBuilder.DropTable(
                name: "LineageSpouseOrders");
        }
    }
}
