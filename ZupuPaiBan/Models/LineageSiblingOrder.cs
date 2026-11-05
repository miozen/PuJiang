namespace ZupuPaiBan.Models;

// Person and relationship references are historical evidence, not cascading foreign keys.
public sealed class LineageSiblingOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChartId { get; set; }
    public Guid ParentId { get; set; }
    public string ParentName { get; set; } = string.Empty;
    public string? ParentGeneration { get; set; }
    public long EvidenceGeneration { get; set; }
    public List<LineageSiblingOrderItem> Items { get; set; } = new();
}

public sealed class LineageSiblingOrderItem
{
    public Guid GroupId { get; set; }
    public Guid PersonId { get; set; }
    public int Position { get; set; }
    public Guid RelationshipId { get; set; }
    public PersonRelationshipKind Kind { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string? RecordedGeneration { get; set; }
    public string? RecordedBirthOrder { get; set; }
}
