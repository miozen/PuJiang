using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZupuPaiBan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLineageSiblingOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LineageSiblingOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentGeneration = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EvidenceGeneration = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineageSiblingOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineageSiblingOrders_LineageCharts_ChartId",
                        column: x => x.ChartId,
                        principalTable: "LineageCharts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LineageSiblingOrderItems",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RecordedGeneration = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RecordedBirthOrder = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineageSiblingOrderItems", x => new { x.GroupId, x.PersonId });
                    table.CheckConstraint("CK_LineageSiblingOrderItems_Position", "Position > 0");
                    table.ForeignKey(
                        name: "FK_LineageSiblingOrderItems_LineageSiblingOrders_GroupId",
                        column: x => x.GroupId,
                        principalTable: "LineageSiblingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LineageSiblingOrderItems_GroupId_Position",
                table: "LineageSiblingOrderItems",
                columns: new[] { "GroupId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineageSiblingOrderItems_GroupId_RelationshipId",
                table: "LineageSiblingOrderItems",
                columns: new[] { "GroupId", "RelationshipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineageSiblingOrders_ChartId_ParentId",
                table: "LineageSiblingOrders",
                columns: new[] { "ChartId", "ParentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LineageSiblingOrderItems");

            migrationBuilder.DropTable(
                name: "LineageSiblingOrders");
        }
    }
}
