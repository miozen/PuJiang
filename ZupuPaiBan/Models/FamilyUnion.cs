namespace ZupuPaiBan.Models;

public enum FamilyUnionKind
{
    Marriage = 1,
    Partnership = 2,
    Unknown = 3
}

/// <summary>完整配偶档案之间的无向婚配/伴侣关系；完整配对按稳定 ID 顺序持久化，任一方资料可暂时不完整。</summary>
public class FamilyUnion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenealogyId { get; set; }
    public Guid PartnerOneId { get; set; }
    public Guid? PartnerTwoId { get; set; }
    public FamilyUnionKind Kind { get; set; } = FamilyUnionKind.Marriage;
    public string? StartDateText { get; set; }
    public string? EndDateText { get; set; }
    public string? Note { get; set; }

    public Genealogy Genealogy { get; set; } = null!;
    public Person PartnerOne { get; set; } = null!;
    public Person? PartnerTwo { get; set; }
}
