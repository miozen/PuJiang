using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ZupuPaiBan.Data.Migrations;

[DbContext(typeof(ZupuDbContext))]
[Migration("20261030000100_AddUnlinkGeneration")]
public sealed class AddUnlinkGeneration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(UnlinkGenerationV1.TableSql);
        migrationBuilder.Sql("INSERT INTO UnlinkGeneration (Id, Value) VALUES (1, 0)");
        foreach (var table in UnlinkGenerationV1.Tables)
        foreach (var operation in UnlinkGenerationV1.Operations)
            migrationBuilder.Sql(UnlinkGenerationV1.TriggerSql(table, operation));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in UnlinkGenerationV1.Tables)
        foreach (var operation in UnlinkGenerationV1.Operations)
            migrationBuilder.Sql($"DROP TRIGGER UnlinkGeneration{table}{operation}");
        migrationBuilder.Sql("DROP TABLE UnlinkGeneration");
    }
}
