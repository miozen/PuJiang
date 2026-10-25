using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZupuPaiBan.Data.Migrations;

[DbContext(typeof(ZupuDbContext))]
[Migration("20261025000100_InitialPuJiang")]
public partial class InitialPuJiang : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Genealogies",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Surname = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Genealogies", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Members",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                Gender = table.Column<int>(type: "INTEGER", nullable: false),
                BirthDate = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                DeathDate = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                SpouseName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                Biography = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                PhotoPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                FatherId = table.Column<int>(type: "INTEGER", nullable: true),
                Generation = table.Column<int>(type: "INTEGER", nullable: false),
                BirthOrder = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Members", x => x.Id);
                table.ForeignKey("FK_Members_Members_FatherId", x => x.FatherId, "Members", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "FamilyBranches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                GenealogyId = table.Column<Guid>(type: "TEXT", nullable: false),
                ParentBranchId = table.Column<Guid>(type: "TEXT", nullable: true),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FamilyBranches", x => x.Id);
                table.UniqueConstraint("AK_FamilyBranches_Id_GenealogyId", x => new { x.Id, x.GenealogyId });
                table.ForeignKey("FK_FamilyBranches_Genealogies_GenealogyId", x => x.GenealogyId, "Genealogies", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_FamilyBranches_FamilyBranches_ParentBranchId_GenealogyId", x => new { x.ParentBranchId, x.GenealogyId }, "FamilyBranches", new[] { "Id", "GenealogyId" }, onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "People",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                GenealogyId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Sex = table.Column<int>(type: "INTEGER", nullable: false),
                BirthDateText = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                DeathDateText = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                Biography = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                PhotoPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                IsHistoricalRecord = table.Column<bool>(type: "INTEGER", nullable: false),
                RecordedGeneration = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                DerivedGeneration = table.Column<int>(type: "INTEGER", nullable: true),
                RecordedBirthOrder = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                DerivedBirthOrder = table.Column<int>(type: "INTEGER", nullable: true),
                BranchId = table.Column<Guid>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_People", x => x.Id);
                table.UniqueConstraint("AK_People_Id_GenealogyId", x => new { x.Id, x.GenealogyId });
                table.ForeignKey("FK_People_Genealogies_GenealogyId", x => x.GenealogyId, "Genealogies", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_People_FamilyBranches_BranchId_GenealogyId", x => new { x.BranchId, x.GenealogyId }, "FamilyBranches", new[] { "Id", "GenealogyId" }, onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "FamilyUnions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                GenealogyId = table.Column<Guid>(type: "TEXT", nullable: false),
                PartnerOneId = table.Column<Guid>(type: "TEXT", nullable: false),
                PartnerTwoId = table.Column<Guid>(type: "TEXT", nullable: true),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                StartDateText = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                EndDateText = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FamilyUnions", x => x.Id);
                table.CheckConstraint("CK_FamilyUnions_NotSamePartner", "PartnerTwoId IS NULL OR PartnerOneId <> PartnerTwoId");
                table.ForeignKey("FK_FamilyUnions_Genealogies_GenealogyId", x => x.GenealogyId, "Genealogies", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_FamilyUnions_People_PartnerOneId_GenealogyId", x => new { x.PartnerOneId, x.GenealogyId }, "People", new[] { "Id", "GenealogyId" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_FamilyUnions_People_PartnerTwoId_GenealogyId", x => new { x.PartnerTwoId, x.GenealogyId }, "People", new[] { "Id", "GenealogyId" }, onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PersonRelationships",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                GenealogyId = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                RelatedPersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                IsPreferredForLineage = table.Column<bool>(type: "INTEGER", nullable: false),
                Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                SourceCitation = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PersonRelationships", x => x.Id);
                table.CheckConstraint("CK_PersonRelationships_LostAncestry", "(Kind = 5 AND RelatedPersonId IS NULL) OR (Kind <> 5 AND RelatedPersonId IS NOT NULL)");
                table.CheckConstraint("CK_PersonRelationships_NotSelf", "RelatedPersonId IS NULL OR PersonId <> RelatedPersonId");
                table.ForeignKey("FK_PersonRelationships_Genealogies_GenealogyId", x => x.GenealogyId, "Genealogies", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_PersonRelationships_People_PersonId_GenealogyId", x => new { x.PersonId, x.GenealogyId }, "People", new[] { "Id", "GenealogyId" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_PersonRelationships_People_RelatedPersonId_GenealogyId", x => new { x.RelatedPersonId, x.GenealogyId }, "People", new[] { "Id", "GenealogyId" }, onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_Members_FatherId", "Members", "FatherId");
        migrationBuilder.CreateIndex("IX_Genealogies_Name", "Genealogies", "Name", unique: true);
        migrationBuilder.CreateIndex("IX_FamilyBranches_GenealogyId_ParentBranchId_Name", "FamilyBranches", new[] { "GenealogyId", "ParentBranchId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_People_BranchId_GenealogyId", "People", new[] { "BranchId", "GenealogyId" });
        migrationBuilder.CreateIndex("IX_People_GenealogyId_Name", "People", new[] { "GenealogyId", "Name" });
        migrationBuilder.CreateIndex("IX_FamilyUnions_GenealogyId_PartnerOneId_PartnerTwoId_Kind", "FamilyUnions", new[] { "GenealogyId", "PartnerOneId", "PartnerTwoId", "Kind" }, unique: true);
        migrationBuilder.CreateIndex("IX_FamilyUnions_PartnerOneId_GenealogyId", "FamilyUnions", new[] { "PartnerOneId", "GenealogyId" });
        migrationBuilder.CreateIndex("IX_FamilyUnions_PartnerTwoId_GenealogyId", "FamilyUnions", new[] { "PartnerTwoId", "GenealogyId" });
        migrationBuilder.CreateIndex("IX_PersonRelationships_GenealogyId_PersonId_RelatedPersonId_Kind", "PersonRelationships", new[] { "GenealogyId", "PersonId", "RelatedPersonId", "Kind" }, unique: true);
        migrationBuilder.CreateIndex("IX_PersonRelationships_PersonId_GenealogyId", "PersonRelationships", new[] { "PersonId", "GenealogyId" });
        migrationBuilder.CreateIndex("IX_PersonRelationships_RelatedPersonId_GenealogyId", "PersonRelationships", new[] { "RelatedPersonId", "GenealogyId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FamilyUnions");
        migrationBuilder.DropTable(name: "PersonRelationships");
        migrationBuilder.DropTable(name: "Members");
        migrationBuilder.DropTable(name: "People");
        migrationBuilder.DropTable(name: "FamilyBranches");
        migrationBuilder.DropTable(name: "Genealogies");
    }
}
