using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

/// <summary>单次一致性读取的不可变原始谱系数据，不选择或推导展示主线。</summary>
public sealed record GenealogyGraphSnapshot(
    GenealogyGraphInfo Genealogy,
    IReadOnlyList<GenealogyPersonData> People,
    IReadOnlyList<GenealogyRelationshipData> Relationships,
    IReadOnlyList<GenealogyUnionData> Unions,
    IReadOnlyList<GenealogyBranchData> Branches,
    long UnlinkGeneration = 0);

public sealed record GenealogyGraphInfo(Guid Id, string Name, string? Surname, string? Description, DateTimeOffset CreatedAt);
public sealed record GenealogyPersonData(Guid Id, string Name, PersonSex Sex, string? BirthDateText,
    string? DeathDateText, string? Biography, string? PhotoPath, bool IsHistoricalRecord,
    string? RecordedGeneration, int? DerivedGeneration, string? RecordedBirthOrder, int? DerivedBirthOrder, Guid? BranchId);
public sealed record GenealogyRelationshipData(Guid Id, Guid PersonId, Guid? RelatedPersonId,
    PersonRelationshipKind Kind, bool IsPreferredForLineage, string? Note, string? SourceCitation);
public sealed record GenealogyUnionData(Guid Id, Guid PartnerOneId, Guid? PartnerTwoId,
    FamilyUnionKind Kind, string? StartDateText, string? EndDateText, string? Note);
public sealed record GenealogyBranchData(Guid Id, Guid? ParentBranchId, string Name, int SortOrder);
