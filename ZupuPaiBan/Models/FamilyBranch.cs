namespace ZupuPaiBan.Models;

/// <summary>同一族谱内的房支；它不是人物父母关系的替代品。</summary>
public class FamilyBranch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenealogyId { get; set; }
    public Guid? ParentBranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public Genealogy Genealogy { get; set; } = null!;
    public FamilyBranch? ParentBranch { get; set; }
    public List<FamilyBranch> ChildBranches { get; set; } = new();
}
