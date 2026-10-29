namespace ZupuPaiBan.ViewModels;

/// <summary>Immutable, scoped presentation data for one explicit target-model unlink operation.</summary>
public sealed record TargetLinkRemovalCandidate(Guid Id, TargetLinkRemovalKind Kind, string Description);

public enum TargetLinkRemovalKind
{
    Relationship,
    FamilyUnion
}
