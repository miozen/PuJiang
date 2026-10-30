namespace ZupuPaiBan.ViewModels;

/// <summary>
/// Immutable presentation data for one explicit target-model unlink operation. The scope and selection version
/// bind a modal confirmation to the exact person and genealogy from which its candidate list was read.
/// </summary>
public sealed record TargetLinkRemovalCandidate(
    Guid Id,
    TargetLinkRemovalKind Kind,
    string Description,
    Guid GenealogyId,
    Guid ParticipantId,
    int SelectionVersion,
    int PersonSelectionVersion);

public enum TargetLinkRemovalKind
{
    Relationship,
    FamilyUnion
}
