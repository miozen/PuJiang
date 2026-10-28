namespace ZupuPaiBan.Models;

/// <summary>一个独立管理、独立隔离的族谱。</summary>
public class Genealogy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Surname { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Person> People { get; set; } = new();
    public List<FamilyBranch> Branches { get; set; } = new();
    public List<GenealogyImportRecord> ImportRecords { get; set; } = new();
}
