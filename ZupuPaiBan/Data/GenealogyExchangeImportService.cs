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
    IReadOnlyList<GenealogyExchangeImportIssue> Issues)
{
    public bool CanImport => Exchange is not null && Issues.Count == 0;

    public string ToDisplayText()
    {
        if (!CanImport)
            return $"预检拒绝：未写入任何数据。\n{string.Join("\n", Issues.Select(issue => $"[{issue.Code}] {issue.Message}"))}";

        return $"预检通过：内部 JSON v{Exchange!.FormatVersion}，人物 {Exchange.People}，关系 {Exchange.Relationships}，" +
               $"婚配 {Exchange.Unions}，房支 {Exchange.Branches}，照片路径引用 {Exchange.PhotoPathReferences}。\n" +
               "确认导入后将新建此来源 ID 的族谱；不会覆盖、删除或自动合并现有数据。";
    }
}

public sealed record GenealogyExchangeImportPreflightResult(
    GenealogyExchangeDocument? Document,
    GenealogyExchangeImportPreflightReport Report);

public sealed record GenealogyExchangeImportResult(bool Imported, GenealogyExchangeImportPreflightReport Report,
    Guid? ImportRecordId);

/// <summary>
/// Insert-only persistence for the internal exchange format. Clone/remap import and conflict merging are deliberately unsupported.
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
    {
        try
        {
            var (document, _) = await GenealogyExchangeService.PreflightAsync(path, cancellationToken);
            return await PreflightAsync(document, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or FileNotFoundException or InvalidDataException)
        {
            return Denied("InvalidDocument", exception.Message);
        }
    }

    public async Task<GenealogyExchangeImportPreflightResult> PreflightAsync(
        GenealogyExchangeDocument document, CancellationToken cancellationToken = default)
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
        var sourceId = document.Genealogy.Id;
        if (await _context.Genealogies.AnyAsync(item => item.Id == sourceId, cancellationToken))
            issues.Add(new("ExistingGenealogyId", "来源族谱 ID 已存在；默认导入不会覆盖、删除或自动合并该族谱。"));

        await AddExistingIdIssueAsync(_context.FamilyBranches, document.Branches.Select(item => item.Id), "ExistingBranchIds", "房支", issues, cancellationToken);
        await AddExistingIdIssueAsync(_context.People, document.People.Select(item => item.Id), "ExistingPersonIds", "人物", issues, cancellationToken);
        await AddExistingIdIssueAsync(_context.PersonRelationships, document.Relationships.Select(item => item.Id), "ExistingRelationshipIds", "人物关系", issues, cancellationToken);
        await AddExistingIdIssueAsync(_context.FamilyUnions, document.Unions.Select(item => item.Id), "ExistingUnionIds", "婚配关系", issues, cancellationToken);

        return new(document, new(exchange, issues.AsReadOnly()));
    }

    public async Task<GenealogyExchangeImportResult> ImportAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var preflight = await PreflightAsync(path, cancellationToken);
        return await ImportPreflightedAsync(preflight, cancellationToken);
    }

    public async Task<GenealogyExchangeImportResult> ImportAsync(
        GenealogyExchangeDocument document, CancellationToken cancellationToken = default)
    {
        var preflight = await PreflightAsync(document, cancellationToken);
        return await ImportPreflightedAsync(preflight, cancellationToken);
    }

    private async Task<GenealogyExchangeImportResult> ImportPreflightedAsync(
        GenealogyExchangeImportPreflightResult preflight, CancellationToken cancellationToken)
    {
        if (!preflight.Report.CanImport || preflight.Document is null)
            return new(false, preflight.Report, null);

        var target = GenealogyExchangeService.MapToTarget(preflight.Document);
        var importRecord = new GenealogyImportRecord
        {
            GenealogyId = target.Genealogy.Id,
            SourceKind = "PuJiang.InternalJson",
            SourceFormatVersion = preflight.Document.FormatVersion,
            SourceGenealogyId = preflight.Document.Genealogy.Id,
            SourceDocumentFingerprint = GenealogyExchangeService.ComputeDocumentFingerprint(preflight.Document)
        };
        await _data.AddGenealogyAndGraphRecordsAsync(target.Genealogy, importRecord, target.Branches, target.People,
            target.Relationships, target.Unions, cancellationToken);
        return new(true, preflight.Report, importRecord.Id);
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
