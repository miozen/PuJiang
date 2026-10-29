using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public sealed record ExternalGenealogyExchangeIssue(string Code, string Message);
public sealed record ExternalGenealogyExchangePreflightReport(string FormatProfile, int People, int Relationships,
    int Unions, IReadOnlyList<ExternalGenealogyExchangeIssue> Losses, IReadOnlyList<ExternalGenealogyExchangeIssue> Errors)
{
    public bool CanMap => Errors.Count == 0;
}
public sealed record ExternalGenealogyExchangePreflightResult(GenealogyExchangeTarget? Target,
    ExternalGenealogyExchangePreflightReport Report);

/// <summary>Read-only compatibility profiles for legacy Excel and GEDCOM; they never write or merge data.</summary>
public static class ExternalGenealogyExchangePreflightService
{
    private const long MaxDocumentBytes = 16L * 1024 * 1024;
    private static readonly string[] ExcelHeaders = ["姓名", "性别", "出生日期", "逝世日期", "配偶姓名", "生平事迹", "辈分", "排行", "父亲ID"];

    public static async Task<ExternalGenealogyExchangePreflightResult> PreflightExcelAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidatePath(path);
            return MapExcel(path);
        }
        catch (Exception exception) when (exception is ArgumentException or FileNotFoundException or InvalidDataException or IOException)
        {
            return Denied("ExcelLegacyV1", "InvalidExcel", exception.Message);
        }
    }

    public static async Task<ExternalGenealogyExchangePreflightResult> PreflightGedcomAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidatePath(path);
            var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken);
            return MapGedcom(lines, Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception exception) when (exception is ArgumentException or FileNotFoundException or InvalidDataException or IOException)
        {
            return Denied("GEDCOM-5.5.x", "InvalidGedcom", exception.Message);
        }
    }

    private static ExternalGenealogyExchangePreflightResult MapExcel(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new InvalidDataException("Excel 文件没有工作表。");
        for (var column = 1; column <= ExcelHeaders.Length; column++)
            if (!string.Equals(sheet.Cell(1, column).GetString().Trim(), ExcelHeaders[column - 1], StringComparison.Ordinal))
                throw new InvalidDataException("Excel 表头不符合 ExcelLegacyV1 交换 profile。");
        var rows = sheet.RowsUsed().Skip(1).ToList();
        if (rows.Count == 0) throw new InvalidDataException("Excel 文件没有成员数据。");
        var genealogy = NewGenealogy(Path.GetFileNameWithoutExtension(path));
        var people = new List<Person>();
        var sourceIds = new Dictionary<int, Person>();
        var fatherReferences = new List<(Person Child, int Father, int Row)>();
        var losses = new List<ExternalGenealogyExchangeIssue>();
        foreach (var row in rows)
        {
            var rowNumber = row.RowNumber();
            var name = row.Cell(1).GetString().Trim();
            var sexText = row.Cell(2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException($"Excel 第 {rowNumber} 行姓名为空。");
            var sex = sexText switch { "男" => PersonSex.Male, "女" => PersonSex.Female, "未知" => PersonSex.Unknown, _ => throw new InvalidDataException($"Excel 第 {rowNumber} 行性别不受支持。") };
            var person = new Person { GenealogyId = genealogy.Id, Name = name, Sex = sex,
                BirthDateText = NullIfBlank(row.Cell(3).GetString()), DeathDateText = NullIfBlank(row.Cell(4).GetString()),
                Biography = NullIfBlank(row.Cell(6).GetString()), RecordedGeneration = NullIfBlank(row.Cell(7).GetString()),
                RecordedBirthOrder = NullIfBlank(row.Cell(8).GetString()) };
            people.Add(person);
            var sourceId = people.Count; // ExcelLegacyV1 defines parent references by data-row ordinal.
            sourceIds.Add(sourceId, person);
            var spouse = NullIfBlank(row.Cell(5).GetString());
            if (spouse is not null) losses.Add(new("SpouseNameText", $"Excel 第 {rowNumber} 行的配偶姓名文本“{spouse}”不能安全匹配为人物或婚配。"));
            var fatherText = NullIfBlank(row.Cell(9).GetString());
            if (fatherText is not null)
            {
                if (!int.TryParse(fatherText, NumberStyles.None, CultureInfo.InvariantCulture, out var father) || father <= 0)
                    throw new InvalidDataException($"Excel 第 {rowNumber} 行父亲 ID 无效。");
                fatherReferences.Add((person, father, rowNumber));
            }
        }
        var relationships = new List<PersonRelationship>();
        foreach (var (child, father, row) in fatherReferences)
        {
            if (!sourceIds.TryGetValue(father, out var parent) || parent.Id == child.Id)
                throw new InvalidDataException($"Excel 第 {row} 行父亲 ID 不引用同一文件中的有效人物。");
            relationships.Add(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id,
                Kind = PersonRelationshipKind.BiologicalParent, SourceCitation = $"ExcelLegacyV1 row {row}" });
        }
        return Success("ExcelLegacyV1", genealogy, people, relationships, [], [], losses);
    }

    private static ExternalGenealogyExchangePreflightResult MapGedcom(IReadOnlyList<string> lines, string suggestion)
    {
        if (!lines.Any(line => line.Trim() == "0 HEAD") || !lines.Any(line => line.Trim() == "0 TRLR") ||
            !lines.Any(line => line.Trim() is "2 VERS 5.5" or "2 VERS 5.5.1"))
            throw new InvalidDataException("GEDCOM 必须包含 HEAD、TRLR 和受支持的 5.5/5.5.1 版本声明。");
        var people = new Dictionary<string, Person>(StringComparer.Ordinal);
        var families = new Dictionary<string, (string? Husband, string? Wife, List<string> Children)>(StringComparer.Ordinal);
        var losses = new List<ExternalGenealogyExchangeIssue>();
        string? individual = null, family = null, eventTag = null;
        foreach (var raw in lines)
        {
            var parts = raw.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !int.TryParse(parts[0], out var level)) throw new InvalidDataException("GEDCOM 行格式无效。");
            var tag = parts[1]; var value = parts.Length == 3 ? parts[2] : null;
            if (level == 0)
            {
                individual = family = null; eventTag = null;
                if (parts.Length == 3 && value == "INDI") { individual = tag; if (!people.TryAdd(tag, new Person { Name = string.Empty })) throw new InvalidDataException("GEDCOM 人物 ID 重复。"); }
                else if (parts.Length == 3 && value == "FAM") { family = tag; if (!families.TryAdd(tag, (null, null, []))) throw new InvalidDataException("GEDCOM 家庭 ID 重复。"); }
                continue;
            }
            if (individual is not null && people.TryGetValue(individual, out var person))
            {
                if (level == 1 && tag is "NAME" or "SEX" or "NOTE") { eventTag = tag; if (tag == "NAME") person.Name = (value ?? "").Replace("/", "").Trim(); else if (tag == "SEX") person.Sex = value switch { "M" => PersonSex.Male, "F" => PersonSex.Female, _ => PersonSex.Unknown }; else person.Biography = NullIfBlank(value); }
                else if (level == 1 && tag is "BIRT" or "DEAT") eventTag = tag;
                else if (level == 2 && tag == "DATE" && eventTag == "BIRT") person.BirthDateText = NullIfBlank(value);
                else if (level == 2 && tag == "DATE" && eventTag == "DEAT") person.DeathDateText = NullIfBlank(value);
                else if (level == 1 && tag is not "FAMS" and not "FAMC") losses.Add(new("IgnoredGedcomTag", $"人物 {individual} 的 {tag} 未映射。"));
            }
            else if (family is not null && families.TryGetValue(family, out var record) && level == 1)
            {
                if (tag == "HUSB") record.Husband = value;
                else if (tag == "WIFE") record.Wife = value;
                else if (tag == "CHIL" && value is not null) record.Children.Add(value);
                else losses.Add(new("IgnoredGedcomTag", $"家庭 {family} 的 {tag} 未映射。"));
                families[family] = record;
            }
        }
        if (people.Count == 0 || people.Values.Any(person => string.IsNullOrWhiteSpace(person.Name))) throw new InvalidDataException("GEDCOM 缺少有效人物姓名。");
        var genealogy = NewGenealogy(suggestion);
        foreach (var person in people.Values) person.GenealogyId = genealogy.Id;
        var relationships = new List<PersonRelationship>(); var unions = new List<FamilyUnion>();
        foreach (var (id, record) in families)
        {
            if (record.Husband is not null && !people.TryGetValue(record.Husband, out var husband) || record.Wife is not null && !people.TryGetValue(record.Wife, out var wife))
                throw new InvalidDataException($"GEDCOM 家庭 {id} 引用了不存在的人物。");
            people.TryGetValue(record.Husband ?? string.Empty, out var first); people.TryGetValue(record.Wife ?? string.Empty, out var second);
            if (first is not null && second is not null) unions.Add(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = first.Id, PartnerTwoId = second.Id, Kind = FamilyUnionKind.Marriage, Note = $"GEDCOM {id}" });
            foreach (var childId in record.Children)
            {
                if (!people.TryGetValue(childId, out var child)) throw new InvalidDataException($"GEDCOM 家庭 {id} 的子女引用不存在。");
                foreach (var parent in new[] { first, second }.Where(person => person is not null).Cast<Person>())
                    relationships.Add(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent, SourceCitation = $"GEDCOM {id}" });
            }
        }
        return Success("GEDCOM-5.5.x", genealogy, people.Values.ToList(), relationships, unions, [], losses);
    }

    private static ExternalGenealogyExchangePreflightResult Success(string profile, Genealogy genealogy, List<Person> people,
        List<PersonRelationship> relationships, List<FamilyUnion> unions, List<FamilyBranch> branches, List<ExternalGenealogyExchangeIssue> losses) =>
        new(new(genealogy, people.AsReadOnly(), relationships.AsReadOnly(), unions.AsReadOnly(), branches.AsReadOnly()),
            new(profile, people.Count, relationships.Count, unions.Count, losses.AsReadOnly(), []));
    private static ExternalGenealogyExchangePreflightResult Denied(string profile, string code, string message) =>
        new(null, new(profile, 0, 0, 0, [], [new(code, message)]));
    private static Genealogy NewGenealogy(string suggestion) => new() { Name = string.IsNullOrWhiteSpace(suggestion) ? "外部导入族谱" : suggestion.Trim() };
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("交换文件必须使用绝对路径。", nameof(path));
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("交换文件不存在。", path);
        if (info.Length <= 0 || info.Length > MaxDocumentBytes) throw new InvalidDataException("交换文件大小超出支持范围。");
    }
}
