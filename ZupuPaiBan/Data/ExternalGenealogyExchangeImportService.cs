using System.Security.Cryptography;
using System.IO;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public sealed record ExternalGenealogyExchangeImportResult(bool Imported, ExternalGenealogyExchangePreflightReport Report,
    Guid? GenealogyId, Guid? ImportRecordId);

/// <summary>Explicit, insert-only persistence for a preflighted external compatibility profile.</summary>
public sealed class ExternalGenealogyExchangeImportService
{
    private readonly ZupuDbContext _context;
    public ExternalGenealogyExchangeImportService(ZupuDbContext context) => _context = context;

    // Keeps the preflight-to-commit boundary deterministically testable without exposing a production callback.
    internal Func<CancellationToken, Task>? AfterPreflightForTestingAsync { get; set; }

    public async Task<ExternalGenealogyExchangeImportResult> ImportExcelAsync(string path, string genealogyName,
        bool acceptLosses, CancellationToken cancellationToken = default) =>
        await ImportAsync(path, genealogyName, acceptLosses, ExternalGenealogyExchangePreflightService.PreflightExcelAsync, cancellationToken);

    public async Task<ExternalGenealogyExchangeImportResult> ImportGedcomAsync(string path, string genealogyName,
        bool acceptLosses, CancellationToken cancellationToken = default) =>
        await ImportAsync(path, genealogyName, acceptLosses, ExternalGenealogyExchangePreflightService.PreflightGedcomAsync, cancellationToken);

    private async Task<ExternalGenealogyExchangeImportResult> ImportAsync(string path, string genealogyName, bool acceptLosses,
        Func<string, CancellationToken, Task<ExternalGenealogyExchangePreflightResult>> preflight, CancellationToken cancellationToken)
    {
        var result = await preflight(path, cancellationToken);
        if (!result.Report.CanMap || result.Target is null || (result.Report.Losses.Count > 0 && !acceptLosses))
            return new(false, result.Report, null, null);
        var name = genealogyName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200) throw new InvalidOperationException("族谱名称须为 1 至 200 个字符。");
        var fingerprint = await TryFingerprintAsync(path, cancellationToken);
        if (fingerprint is null) return SourceChanged(result.Report);
        if (AfterPreflightForTestingAsync is not null) await AfterPreflightForTestingAsync(cancellationToken);
        var verifiedFingerprint = await TryFingerprintAsync(path, cancellationToken);
        if (!string.Equals(fingerprint, verifiedFingerprint, StringComparison.Ordinal)) return SourceChanged(result.Report);
        var target = CloneWithNewIds(result.Target, name);
        var commitFingerprint = await TryFingerprintAsync(path, cancellationToken);
        if (!string.Equals(fingerprint, commitFingerprint, StringComparison.Ordinal)) return SourceChanged(result.Report);
        var record = new GenealogyImportRecord
        {
            GenealogyId = target.Genealogy.Id, SourceKind = $"PuJiang.{result.Report.FormatProfile}", ImportMode = "Copy",
            SourceFormatVersion = 1, SourceGenealogyId = SourceId(fingerprint), SourceDocumentFingerprint = fingerprint
        };
        await new GenealogyDataService(_context).AddGenealogyAndGraphRecordsAsync(target.Genealogy, record, target.Branches,
            target.People, target.Relationships, target.Unions, cancellationToken);
        return new(true, result.Report, target.Genealogy.Id, record.Id);
    }

    private static async Task<string?> TryFingerprintAsync(string path, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
    private static ExternalGenealogyExchangeImportResult SourceChanged(ExternalGenealogyExchangePreflightReport report) =>
        new(false, report with { Errors = [.. report.Errors,
            new("SourceChangedAfterPreflight", "预检完成后无法确认源文件未被替换、删除或篡改，已拒绝写入。") ] }, null, null);
    private static Guid SourceId(string hash)
    {
        var bytes = Convert.FromHexString(hash)[..16];
        return new Guid(bytes);
    }
    private static GenealogyExchangeTarget CloneWithNewIds(GenealogyExchangeTarget source, string name)
    {
        var genealogyId = Guid.NewGuid();
        var branches = source.Branches.ToDictionary(item => item.Id, _ => Guid.NewGuid());
        var people = source.People.ToDictionary(item => item.Id, _ => Guid.NewGuid());
        var targetBranches = source.Branches.Select(item => new FamilyBranch { Id = branches[item.Id], GenealogyId = genealogyId, Name = item.Name, SortOrder = item.SortOrder, ParentBranchId = item.ParentBranchId is Guid parent ? branches[parent] : null }).ToList();
        var targetPeople = source.People.Select(item => new Person { Id = people[item.Id], GenealogyId = genealogyId, Name = item.Name, Sex = item.Sex, BirthDateText = item.BirthDateText, DeathDateText = item.DeathDateText, Biography = item.Biography, RecordedGeneration = item.RecordedGeneration, RecordedBirthOrder = item.RecordedBirthOrder, BranchId = item.BranchId is Guid branch ? branches[branch] : null }).ToList();
        var relationships = source.Relationships.Select(item => new PersonRelationship { Id = Guid.NewGuid(), GenealogyId = genealogyId, PersonId = people[item.PersonId], RelatedPersonId = item.RelatedPersonId is Guid related ? people[related] : null, Kind = item.Kind, SourceCitation = item.SourceCitation }).ToList();
        var unions = source.Unions.Select(item => new FamilyUnion { Id = Guid.NewGuid(), GenealogyId = genealogyId, PartnerOneId = people[item.PartnerOneId], PartnerTwoId = item.PartnerTwoId is Guid partner ? people[partner] : null, Kind = item.Kind, Note = item.Note }).ToList();
        return new(new Genealogy { Id = genealogyId, Name = name }, targetPeople, relationships, unions, targetBranches);
    }
}
