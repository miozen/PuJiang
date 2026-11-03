namespace ZupuPaiBan.Models;

public sealed class LineageChart
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenealogyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Revision { get; set; }
    public long Generation { get; set; }
    public List<LineageChartChoice> Choices { get; set; } = new();
}

// Person and relationship IDs are historical references, intentionally not foreign keys.
public sealed class LineageChartChoice
{
    public Guid ChartId { get; set; }
    public Guid PersonId { get; set; }
    public Guid ParentId { get; set; }
    public Guid RelationshipId { get; set; }
    public PersonRelationshipKind Kind { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string ParentName { get; set; } = string.Empty;
}
