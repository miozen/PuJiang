namespace ZupuPaiBan.Models;

// Union identities and endpoints are historical evidence; they are not cascading links.
public sealed class LineageSpouseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChartId { get; set; }
    public Guid OwnerId { get; set; }
    public long EvidenceGeneration { get; set; }
    public List<LineageSpouseOrderItem> Items { get; set; } = new();
}

public sealed class LineageSpouseOrderItem
{
    public Guid GroupId { get; set; }
    public Guid UnionId { get; set; }
    public Guid PartnerOneId { get; set; }
    public Guid? PartnerTwoId { get; set; }
    public FamilyUnionKind Kind { get; set; }
    public int Position { get; set; }
}
