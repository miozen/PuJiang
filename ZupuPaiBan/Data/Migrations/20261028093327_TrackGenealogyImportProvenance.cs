using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZupuPaiBan.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackGenealogyImportProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GenealogyImportRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GenealogyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceFormatVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceGenealogyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceDocumentFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenealogyImportRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GenealogyImportRecords_Genealogies_GenealogyId",
                        column: x => x.GenealogyId,
                        principalTable: "Genealogies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GenealogyImportRecords_GenealogyId_ImportedAt",
                table: "GenealogyImportRecords",
                columns: new[] { "GenealogyId", "ImportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GenealogyImportRecords_SourceKind_SourceFormatVersion_SourceGenealogyId",
                table: "GenealogyImportRecords",
                columns: new[] { "SourceKind", "SourceFormatVersion", "SourceGenealogyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GenealogyImportRecords");
        }
    }
}
