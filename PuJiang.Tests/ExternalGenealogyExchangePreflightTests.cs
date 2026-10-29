using ClosedXML.Excel;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class ExternalGenealogyExchangePreflightTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Gedcom_replacement_after_preflight_is_rejected_without_graph_or_provenance_records()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var path = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "changed-after-preflight.ged");
        await File.WriteAllTextAsync(path, "0 HEAD\n1 GEDC\n2 VERS 5.5.1\n0 @I1@ INDI\n1 NAME 原始 /氏/\n1 SEX M\n0 TRLR\n");
        await using var context = database.CreateContext();
        var importer = new ExternalGenealogyExchangeImportService(context)
        {
            AfterPreflightForTestingAsync = token => File.WriteAllTextAsync(path,
                "0 HEAD\n1 GEDC\n2 VERS 5.5.1\n0 @I1@ INDI\n1 NAME 替换 /氏/\n1 SEX M\n0 TRLR\n", token)
        };

        var result = await importer.ImportGedcomAsync(path, "不得导入", acceptLosses: true);

        Assert.False(result.Imported);
        Assert.Contains(result.Report.Errors, error => error.Code == "SourceChangedAfterPreflight");
        await AssertNoExternalImportRecordsAsync(context);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Excel_tampering_after_preflight_is_rejected_without_graph_or_provenance_records()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var path = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "changed-after-preflight.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("族谱成员");
            var headers = new[] { "姓名", "性别", "出生日期", "逝世日期", "配偶姓名", "生平事迹", "辈分", "排行", "父亲ID" };
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "原始"; sheet.Cell(2, 2).Value = "男";
            workbook.SaveAs(path);
        }
        await using var context = database.CreateContext();
        var importer = new ExternalGenealogyExchangeImportService(context)
        {
            AfterPreflightForTestingAsync = token => File.WriteAllTextAsync(path, "篡改", token)
        };

        var result = await importer.ImportExcelAsync(path, "不得导入", acceptLosses: true);

        Assert.False(result.Imported);
        Assert.Contains(result.Report.Errors, error => error.Code == "SourceChangedAfterPreflight");
        await AssertNoExternalImportRecordsAsync(context);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Gedcom_write_failure_rolls_back_graph_and_provenance_record()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var path = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "write-failure.ged");
        await File.WriteAllTextAsync(path, "0 HEAD\n1 GEDC\n2 VERS 5.5.1\n0 @I1@ INDI\n1 NAME 父 /氏/\n1 SEX M\n0 @I2@ INDI\n1 NAME 母 /氏/\n1 SEX F\n0 @F1@ FAM\n1 HUSB @I1@\n1 WIFE @I2@\n0 TRLR\n");
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(new FailFamilyUnionInsertInterceptor()).Options;
        await using (var failing = new ZupuDbContext(options))
        {
            var importer = new ExternalGenealogyExchangeImportService(failing);
            await Assert.ThrowsAsync<DbUpdateException>(() => importer.ImportGedcomAsync(path, "失败谱", acceptLosses: true));
        }
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.Genealogies.ToListAsync());
        Assert.Empty(await verify.People.ToListAsync());
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
        Assert.Empty(await verify.FamilyUnions.ToListAsync());
        Assert.Empty(await verify.GenealogyImportRecords.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Lossy_gedcom_requires_explicit_acceptance_before_atomic_new_genealogy_import()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var path = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "lossy.ged");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "0 HEAD\n1 GEDC\n2 VERS 5.5.1\n0 @I1@ INDI\n1 NAME 甲 /氏/\n1 SEX M\n1 OCCU 农\n0 TRLR\n");
        await using var context = database.CreateContext();
        var importer = new ExternalGenealogyExchangeImportService(context);

        var denied = await importer.ImportGedcomAsync(path, "拒绝谱", acceptLosses: false);
        Assert.False(denied.Imported);
        Assert.Empty(await context.Genealogies.ToListAsync());

        var accepted = await importer.ImportGedcomAsync(path, "确认谱", acceptLosses: true);
        Assert.True(accepted.Imported);
        Assert.NotNull(accepted.GenealogyId);
        var record = Assert.Single(await new GenealogyDataService(context).GetImportRecordsAsync(accepted.GenealogyId!.Value));
        Assert.Equal("PuJiang.GEDCOM-5.5.x", record.SourceKind);
        Assert.Equal(64, record.SourceDocumentFingerprint.Length);
    }

    private sealed class FailFamilyUnionInsertInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"FamilyUnions\"", StringComparison.Ordinal))
                throw new IOException("模拟外部导入婚配写入失败。");
            return ValueTask.FromResult(result);
        }
    }

    private static async Task AssertNoExternalImportRecordsAsync(ZupuDbContext context)
    {
        Assert.Empty(await context.Genealogies.ToListAsync());
        Assert.Empty(await context.People.ToListAsync());
        Assert.Empty(await context.PersonRelationships.ToListAsync());
        Assert.Empty(await context.FamilyUnions.ToListAsync());
        Assert.Empty(await context.GenealogyImportRecords.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Excel_profile_maps_people_and_parent_but_reports_unmappable_spouse_text()
    {
        await using var database = new TestDatabase();
        var path = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "legacy.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("族谱成员");
            var headers = new[] { "姓名", "性别", "出生日期", "逝世日期", "配偶姓名", "生平事迹", "辈分", "排行", "父亲ID" };
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "父"; sheet.Cell(2, 2).Value = "男"; sheet.Cell(2, 7).Value = "一世";
            sheet.Cell(3, 1).Value = "子"; sheet.Cell(3, 2).Value = "女"; sheet.Cell(3, 5).Value = "未建档配偶"; sheet.Cell(3, 9).Value = "1";
            workbook.SaveAs(path);
        }

        var result = await ExternalGenealogyExchangePreflightService.PreflightExcelAsync(path);

        Assert.True(result.Report.CanMap);
        Assert.Equal("ExcelLegacyV1", result.Report.FormatProfile);
        Assert.Equal(2, result.Report.People);
        Assert.Single(result.Target!.Relationships);
        Assert.Contains(result.Report.Losses, loss => loss.Code == "SpouseNameText");
        Assert.All(result.Target.People, person => Assert.NotEqual(Guid.Empty, person.GenealogyId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Excel_profile_rejects_invalid_parent_reference_without_target_mapping()
    {
        await using var database = new TestDatabase();
        var path = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "invalid.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("族谱成员");
            var headers = new[] { "姓名", "性别", "出生日期", "逝世日期", "配偶姓名", "生平事迹", "辈分", "排行", "父亲ID" };
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "孤"; sheet.Cell(2, 2).Value = "男"; sheet.Cell(2, 9).Value = "9";
            workbook.SaveAs(path);
        }

        var result = await ExternalGenealogyExchangePreflightService.PreflightExcelAsync(path);

        Assert.False(result.Report.CanMap);
        Assert.Null(result.Target);
        Assert.Contains(result.Report.Errors, error => error.Code == "InvalidExcel");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Gedcom_profile_maps_family_graph_and_reports_ignored_tags()
    {
        await using var database = new TestDatabase();
        var path = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "family.ged");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            0 HEAD
            1 GEDC
            2 VERS 5.5.1
            0 @I1@ INDI
            1 NAME 父 /甲/
            1 SEX M
            1 OCCU 农
            0 @I2@ INDI
            1 NAME 母 /乙/
            1 SEX F
            0 @I3@ INDI
            1 NAME 子 /丙/
            1 SEX M
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 CHIL @I3@
            0 TRLR
            """);

        var result = await ExternalGenealogyExchangePreflightService.PreflightGedcomAsync(path);

        Assert.True(result.Report.CanMap);
        Assert.Equal(3, result.Report.People);
        Assert.Equal(2, result.Report.Relationships);
        Assert.Single(result.Target!.Unions);
        Assert.Contains(result.Report.Losses, loss => loss.Code == "IgnoredGedcomTag");
        Assert.All(result.Target.Relationships, relationship => Assert.Equal(PersonRelationshipKind.BiologicalParent, relationship.Kind));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Gedcom_profile_rejects_reversed_duplicate_family_unions_without_target_mapping()
    {
        await using var database = new TestDatabase();
        var path = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "duplicate-family.ged");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            0 HEAD
            1 GEDC
            2 VERS 5.5.1
            0 @I1@ INDI
            1 NAME 甲 /氏/
            0 @I2@ INDI
            1 NAME 乙 /氏/
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            0 @F2@ FAM
            1 HUSB @I2@
            1 WIFE @I1@
            0 TRLR
            """);

        var result = await ExternalGenealogyExchangePreflightService.PreflightGedcomAsync(path);

        Assert.False(result.Report.CanMap);
        Assert.Null(result.Target);
        Assert.Contains(result.Report.Errors, error => error.Code == "InvalidGedcom");
    }
}
