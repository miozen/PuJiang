using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ZupuPaiBan.Data.Migrations;

[DbContext(typeof(ZupuDbContext))]
[Migration("20261030000200_AddScopedUnlinkGenerationV1")]
public sealed class AddScopedUnlinkGenerationV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ScopedUnlinkGenerationV1.ActiveTableSql);
        migrationBuilder.Sql(ScopedUnlinkGenerationV1.TombstoneTableSql);
        migrationBuilder.Sql("INSERT INTO ScopedUnlinkGenerationV1 (GenealogyId, Value) SELECT Id, 0 FROM Genealogies");
        migrationBuilder.Sql(ScopedUnlinkGenerationV1.GenealogyInsertTriggerSql);
        migrationBuilder.Sql(ScopedUnlinkGenerationV1.GenealogyDeleteTriggerSql);
        foreach (var (_, sql) in ScopedUnlinkGenerationV1.ExpectedTriggers().Skip(2))
            migrationBuilder.Sql(sql);
        migrationBuilder.Sql(ScopedUnlinkGenerationV1.InstallValidationSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var (name, _) in ScopedUnlinkGenerationV1.ExpectedTriggers().Reverse())
            migrationBuilder.Sql($"DROP TRIGGER {name}");
        migrationBuilder.Sql($"DROP TABLE {ScopedUnlinkGenerationV1.TombstoneTable}");
        migrationBuilder.Sql($"DROP TABLE {ScopedUnlinkGenerationV1.ActiveTable}");
    }
}
