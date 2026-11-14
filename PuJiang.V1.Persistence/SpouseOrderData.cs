namespace PuJiang.V1.Persistence;

public sealed class SpouseOrderGroup
{
    public Guid LineageId { get; set; }
    public Guid FatherId { get; set; }
    public long Revision { get; set; }
}
public sealed class SpouseOrderEntry
{
    public Guid LineageId { get; set; }
    public Guid FatherId { get; set; }
    public Guid UnionId { get; set; }
    public int Position { get; set; }
}
internal sealed class SpouseOrderReceipt
{
    public Guid OperationId { get; set; }
    public Guid GenealogyId { get; set; }
    public Guid LineageId { get; set; }
    public Guid FatherId { get; set; }
    public Guid PlacementId { get; set; }
    public Guid SourceUnionId { get; set; }
    public Guid TargetUnionId { get; set; }
    public long ExpectedRevision { get; set; }
}
public sealed class SpouseOrderRepairReceipt
{
    public Guid OperationId { get; set; }
    public Guid GenealogyId { get; set; }
    public Guid LineageId { get; set; }
    public Guid FatherId { get; set; }
    public Guid PlacementId { get; set; }
    public string OriginalSnapshot { get; set; } = "";
    public string OrderedUnionIds { get; set; } = "";
    public long NewRevision { get; set; }
}
