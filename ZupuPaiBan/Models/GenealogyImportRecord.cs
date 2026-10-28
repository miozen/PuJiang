namespace ZupuPaiBan.Models;

/// <summary>
/// Immutable provenance for one successful genealogy import. It records document identity rather than a machine-local file path.
/// </summary>
public class GenealogyImportRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenealogyId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public int SourceFormatVersion { get; set; }
    public Guid SourceGenealogyId { get; set; }
    public string SourceDocumentFingerprint { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;

    public Genealogy Genealogy { get; set; } = null!;
}
