using ClosedXML.Excel;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class ExternalGenealogyExchangePreflightTests
{
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
}
