using Microsoft.EntityFrameworkCore;
using System.IO;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

/// <summary>A specific reason why an internal exchange document cannot be imported unchanged.</summary>
public sealed record GenealogyExchangeImportIssue(string Code, string Message);

/// <summary>
/// Readable, fail-closed import decision. A denied report never changes the database.
/// </summary>
public sealed record GenealogyExchangeImportPreflightReport(
    GenealogyExchangePreflightReport? Exchange,
    IReadOnlyList<GenealogyExchangeImportIssue> Issues,
    bool IsCopyImport = false)
{
    public bool CanImport => Exchange is not null && Issues.Count == 0;

    public string ToDisplayText()
    {
        if (!CanImport)
            return $"预检拒绝：未写入任何数据。\n{string.Join("\n", Issues.Select(issue => $"[{issue.Code}] {issue.Message}"))}";

        var outcome = IsCopyImport
            ? "确认后将创建具有全新内部 ID 的独立族谱副本；不会覆盖、删除或自动合并现有数据。"
            : "确认导入后将新建此来源 ID 的族谱；不会覆盖、删除或自动合并现有数据。";
        return $"预检通过：内部 JSON v{Exchange!.FormatVersion}，人物 {Exchange.People}，关系 {Exchange.Relationships}，" +
               $"婚配 {Exchange.Unions}，房支 {Exchange.Branches}，照片路径引用 {Exchange.PhotoPathReferences}。\n" +
               outcome;
    }
}

public sealed record GenealogyExchangeImportPreflightResult(
    GenealogyExchangeDocument? Document,
    GenealogyExchangeImportPreflightReport Report);

public sealed record GenealogyExchangeImportResult(bool Imported, GenealogyExchangeImportPreflightReport Report,
    Guid? GenealogyId, Guid? ImportRecordId);

/// <summary>
/// Insert-only persistence for the internal exchange format. Copy import is explicit; conflict merging is deliberately unsupported.
/// </summary>
public sealed class GenealogyExchangeImportService
{
    private readonly ZupuDbContext _context;
    private readonly GenealogyDataService _data;

    public GenealogyExchangeImportService(ZupuDbContext context)
    {
        _context = context;
        _data = new GenealogyDataService(context);
    }

    public async Task<GenealogyExchangeImportPreflightResult> PreflightAsync(
        string path, CancellationToken cancellationToken = default)
        => await PreflightPathAsync(path, isCopyImport: false, cancellationToken);

    public async Task<GenealogyExchangeImportPreflightResult> PreflightCopyAsync(
        string path, CancellationToken cancellationToken = default)
        => await PreflightPathAsync(path, isCopyImport: true, cancellationToken);

    private async Task<GenealogyExchangeImportPreflightResult> PreflightPathAsync(
        string path, bool isCopyImport, CancellationToken cancellationToken)
    {
        try
        {
            var (document, _) = await GenealogyExchangeService.PreflightAsync(path, cancellationToken);
            return await PreflightDocumentAsync(document, isCopyImport, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or FileNotFoundException or InvalidDataException)
        {
            return Denied("InvalidDocument", exception.Message);
        }
    }

    public async Task<GenealogyExchangeImportPreflightResult> PreflightAsync(
        GenealogyExchangeDocument document, CancellationToken cancellationToken = default)
        => await PreflightDocumentAsync(document, isCopyImport: false, cancellationToken);

    public async Task<GenealogyExchangeImportPreflightResult> PreflightCopyAsync(
        GenealogyExchangeDocument document, CancellationToken cancellationToken = default)
        => await PreflightDocumentAsync(document, isCopyImport: true, cancellationToken);

    private async Task<GenealogyExchangeImportPreflightResult> PreflightDocumentAsync(
        GenealogyExchangeDocument document, bool isCopyImport, CancellationToken cancellationToken)
    {
        GenealogyExchangePreflightReport exchange;
        try
        {
            exchange = GenealogyExchangeService.Preflight(document);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return Denied("InvalidDocument", exception.Message);
        }

        var issues = new List<GenealogyExchangeImportIssue>();
        if (!isCopyImport)
        {
            var sourceId = document.Genealogy.Id;
            if (await _context.Genealogies.AnyAsync(item => item.Id == sourceId, cancellationToken))
                issues.Add(new("ExistingGenealogyId", "来源族谱 ID 已存在；默认导入不会覆盖、删除或自动合并该族谱。"));

            await AddExistingIdIssueAsync(_context.FamilyBranches, document.Branches.Select(item => item.Id), "ExistingBranchIds", "房支", issues, cancellationToken);
            await AddExistingIdIssueAsync(_context.People, document.People.Select(item => item.Id), "ExistingPersonIds", "人物", issues, cancellationToken);
            await AddExistingIdIssueAsync(_context.PersonRelationships, document.Relationships.Select(item => item.Id), "ExistingRelationshipIds", "人物关系", issues, cancellationToken);
            await AddExistingIdIssueAsync(_context.FamilyUnions, document.Unions.Select(item => item.Id), "ExistingUnionIds", "婚配关系", issues, cancellationToken);
        }

        return new(document, new(exchange, issues.AsReadOnly(), isCopyImport));
    }

    public async Task<GenealogyExchangeImportResult> ImportAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var preflight = await PreflightAsync(path, cancellationToken);
        return await ImportPreflightedAsync(preflight, copyName: null, cancellationToken);
    }

    public async Task<GenealogyExchangeImportResult> ImportAsync(
        GenealogyExchangeDocument document, CancellationToken cancellationToken = default)
    {
        var preflight = await PreflightAsync(document, cancellationToken);
        return await ImportPreflightedAsync(preflight, copyName: null, cancellationToken);
    }

    public async Task<GenealogyExchangeImportResult> ImportCopyAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var preflight = await PreflightCopyAsync(path, cancellationToken);
        return await ImportPreflightedAsync(preflight, copyName: null, cancellationToken);
    }

    public async Task<GenealogyExchangeImportResult> ImportCopyAsync(
        string path, string copyName, CancellationToken cancellationToken = default)
    {
        var preflight = await PreflightCopyAsync(path, cancellationToken);
        return await ImportPreflightedAsync(preflight, copyName, cancellationToken);
    }

    public async Task<GenealogyExchangeImportResult> ImportCopyAsync(
        GenealogyExchangeDocument document, CancellationToken cancellationToken = default)
    {
        var preflight = await PreflightCopyAsync(document, cancellationToken);
        return await ImportPreflightedAsync(preflight, copyName: null, cancellationToken);
    }

    public async Task<GenealogyExchangeImportResult> ImportCopyAsync(
        GenealogyExchangeDocument document, string copyName, CancellationToken cancellationToken = default)
    {
        var preflight = await PreflightCopyAsync(document, cancellationToken);
        return await ImportPreflightedAsync(preflight, copyName, cancellationToken);
    }

    private async Task<GenealogyExchangeImportResult> ImportPreflightedAsync(
        GenealogyExchangeImportPreflightResult preflight, string? copyName, CancellationToken cancellationToken)
    {
        if (!preflight.Report.CanImport || preflight.Document is null)
            return new(false, preflight.Report, null, null);

        var target = preflight.Report.IsCopyImport
            ? MapCopyTarget(preflight.Document, NormalizeCopyName(copyName, preflight.Document.Genealogy.Name))
            : GenealogyExchangeService.MapToTarget(preflight.Document);
        var importRecord = new GenealogyImportRecord
        {
            GenealogyId = target.Genealogy.Id,
            SourceKind = "PuJiang.InternalJson",
            ImportMode = preflight.Report.IsCopyImport ? "Copy" : "Default",
            SourceFormatVersion = preflight.Document.FormatVersion,
            SourceGenealogyId = preflight.Document.Genealogy.Id,
            SourceDocumentFingerprint = GenealogyExchangeService.ComputeDocumentFingerprint(preflight.Document)
        };
        await _data.AddGenealogyAndGraphRecordsAsync(target.Genealogy, importRecord, target.Branches, target.People,
            target.Relationships, target.Unions, cancellationToken);
        return new(true, preflight.Report, target.Genealogy.Id, importRecord.Id);
    }

    private static GenealogyExchangeTarget MapCopyTarget(GenealogyExchangeDocument document, string copyName)
    {
        var source = GenealogyExchangeService.MapToTarget(document);
        var genealogyId = Guid.NewGuid();
        var branchIds = source.Branches.ToDictionary(branch => branch.Id, _ => Guid.NewGuid());
        var personIds = source.People.ToDictionary(person => person.Id, _ => Guid.NewGuid());
        var branches = source.Branches.Select(branch => new FamilyBranch
        {
            Id = branchIds[branch.Id], GenealogyId = genealogyId,
            ParentBranchId = branch.ParentBranchId is Guid parent ? branchIds[parent] : null,
            Name = branch.Name, SortOrder = branch.SortOrder
        }).ToList();
        var people = source.People.Select(person => new Person
        {
            Id = personIds[person.Id], GenealogyId = genealogyId, Name = person.Name, Sex = person.Sex,
            BirthDateText = person.BirthDateText, DeathDateText = person.DeathDateText, Biography = person.Biography,
            PhotoPath = person.PhotoPath, IsHistoricalRecord = person.IsHistoricalRecord,
            RecordedGeneration = person.RecordedGeneration, DerivedGeneration = person.DerivedGeneration,
            RecordedBirthOrder = person.RecordedBirthOrder, DerivedBirthOrder = person.DerivedBirthOrder,
            BranchId = person.BranchId is Guid branch ? branchIds[branch] : null
        }).ToList();
        var relationships = source.Relationships.Select(relationship => new PersonRelationship
        {
            Id = Guid.NewGuid(), GenealogyId = genealogyId, PersonId = personIds[relationship.PersonId],
            RelatedPersonId = relationship.RelatedPersonId is Guid related ? personIds[related] : null,
            Kind = relationship.Kind, IsPreferredForLineage = relationship.IsPreferredForLineage,
            Note = relationship.Note, SourceCitation = relationship.SourceCitation
        }).ToList();
        var unions = source.Unions.Select(union => new FamilyUnion
        {
            Id = Guid.NewGuid(), GenealogyId = genealogyId, PartnerOneId = personIds[union.PartnerOneId],
            PartnerTwoId = union.PartnerTwoId is Guid partner ? personIds[partner] : null,
            Kind = union.Kind, StartDateText = union.StartDateText, EndDateText = union.EndDateText, Note = union.Note
        }).ToList();
        var genealogy = new Genealogy
        {
            Id = genealogyId, Name = copyName, Surname = source.Genealogy.Surname,
            Description = source.Genealogy.Description, CreatedAt = source.Genealogy.CreatedAt
        };
        return new(genealogy, people.AsReadOnly(), relationships.AsReadOnly(), unions.AsReadOnly(), branches.AsReadOnly());
    }

    private static string NormalizeCopyName(string? value, string sourceName)
    {
        var name = (value ?? sourceName).Trim();
        if (name.Length is < 1 or > 200) throw new InvalidOperationException("副本族谱名称须为 1 至 200 个字符。");
        return name;
    }

    private static async Task AddExistingIdIssueAsync<TEntity>(
        DbSet<TEntity> records,
        IEnumerable<Guid> sourceIds,
        string code,
        string label,
        ICollection<GenealogyExchangeImportIssue> issues,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var ids = sourceIds.ToList();
        if (ids.Count == 0) return;
        var existing = await records.Where(item => ids.Contains(EF.Property<Guid>(item, "Id")))
            .CountAsync(cancellationToken);
        if (existing > 0)
            issues.Add(new(code, $"{existing} 条{label} ID 已存在；默认导入拒绝覆盖或自动合并。"));
    }

    private static GenealogyExchangeImportPreflightResult Denied(string code, string message) =>
        new(null, new(null, [new(code, message)]));
}
