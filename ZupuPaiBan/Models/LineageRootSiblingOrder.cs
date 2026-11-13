namespace ZupuPaiBan.Models;

// Members are historical evidence; no artificial parent or cascading person link is created.
public sealed class LineageRootSiblingOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChartId { get; set; }
    public long EvidenceGeneration { get; set; }
    public List<LineageRootSiblingOrderItem> Items { get; set; } = new();
}

public sealed class LineageRootSiblingOrderItem
{
    public Guid GroupId { get; set; }
    public Guid PersonId { get; set; }
    public int Position { get; set; }
    public PersonSex Sex { get; set; }
    public string? RecordedGeneration { get; set; }
}
