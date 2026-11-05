namespace ZupuPaiBan.Models;

public enum PersonRelationshipKind
{
    BiologicalParent = 1,
    AdoptiveParent = 2,
    HeirParent = 3,
    Spouse = 4,
    LostAncestry = 5,
    Sibling = 6
}

/// <summary>
/// 有方向的记录：Person 是谱载主体；兄妹关系以一条记录表达两人的明确关系。
/// LostAncestry 用于记录主体的上系失考，因此没有对方人物。
/// </summary>
public class PersonRelationship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenealogyId { get; set; }
    public Guid PersonId { get; set; }
    public Guid? RelatedPersonId { get; set; }
    public PersonRelationshipKind Kind { get; set; }
    public bool IsPreferredForLineage { get; set; }
    public string? Note { get; set; }
    public string? SourceCitation { get; set; }

    public Genealogy Genealogy { get; set; } = null!;
    public Person Person { get; set; } = null!;
    public Person? RelatedPerson { get; set; }
}
