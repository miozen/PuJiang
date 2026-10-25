namespace ZupuPaiBan.Models;

public enum PersonSex
{
    Unknown = 0,
    Male = 1,
    Female = 2
}

/// <summary>
/// 族谱中的人物档案。谱载值与系统推导值刻意分开，避免自动计算覆盖原始记录。
/// </summary>
public class Person
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenealogyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public PersonSex Sex { get; set; } = PersonSex.Unknown;
    public string? BirthDateText { get; set; }
    public string? DeathDateText { get; set; }
    public string? Biography { get; set; }
    public string? PhotoPath { get; set; }
    public bool IsHistoricalRecord { get; set; }

    public string? RecordedGeneration { get; set; }
    public int? DerivedGeneration { get; set; }
    public string? RecordedBirthOrder { get; set; }
    public int? DerivedBirthOrder { get; set; }

    public Guid? BranchId { get; set; }
    public Genealogy Genealogy { get; set; } = null!;
    public FamilyBranch? Branch { get; set; }
}
