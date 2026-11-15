using PuJiang.Domain;
namespace PuJiang.V1.Application;

public sealed record CreateLineage(Guid OperationId, Guid GenealogyId, string Name, string Category, string Region, string Topic, string Hall, string Notes);
public sealed record LineageSummary(Guid Id, string Name) { public string Label => Name + " · " + Id.ToString("N")[..8]; }
public sealed record UpdateLineage(Guid OperationId, Guid GenealogyId, Guid LineageId, string Name, string Category, string Region, string Topic, string Hall, string Notes, string ExpectedEvidence);
public sealed record LineageActivityView(string Kind, string Detail, DateTimeOffset OccurredAt);
public sealed record LineageHome(Guid Id, string Name, string Category, string Region, string Topic, string Hall, string Notes, string Surname, int Total, int Male, int Female, int Unknown, IReadOnlyList<RootNode> Roots, IReadOnlyList<LineageActivityView> Activities);
public interface ILineageStore
{
    Task<Guid> CreateAsync(CreateLineage command, CancellationToken ct);
    Task<IReadOnlyList<LineageSummary>> ListAsync(Guid genealogy, CancellationToken ct);
    Task<LineageHome> ReadHomeAsync(Guid genealogy, Guid lineage, CancellationToken ct);
    Task<bool> HasReceiptAsync(Guid operation, CancellationToken ct);
    Task UpdateAsync(UpdateLineage command, CancellationToken ct);
    Task<bool> HasUpdateReceiptAsync(Guid operation, CancellationToken ct);
}
public sealed class LineageApplication(ILineageStore store)
{
    public Task<Guid> CreateAsync(CreateLineage command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || string.IsNullOrWhiteSpace(command.Category) || string.IsNullOrWhiteSpace(command.Region) || string.IsNullOrWhiteSpace(command.Topic) || command.Hall is null || command.Notes is null) throw new ArgumentException("请填写谱系名称、所属分类、地区和主题名。");
        return store.CreateAsync(command, ct);
    }
    public Task<IReadOnlyList<LineageSummary>> ListAsync(Guid genealogy, CancellationToken ct = default) => store.ListAsync(genealogy, ct);
    public Task<LineageHome> ReadHomeAsync(Guid genealogy, Guid lineage, CancellationToken ct = default) => store.ReadHomeAsync(genealogy, lineage, ct);
    public Task<bool> HasReceiptAsync(Guid operation, CancellationToken ct = default) => store.HasReceiptAsync(operation, ct);
    public Task<bool> HasUpdateReceiptAsync(Guid operation, CancellationToken ct = default) => store.HasUpdateReceiptAsync(operation, ct);
    public Task UpdateAsync(UpdateLineage command, CancellationToken ct = default)
    { ArgumentNullException.ThrowIfNull(command); if(command.OperationId==Guid.Empty || command.GenealogyId==Guid.Empty || command.LineageId==Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || string.IsNullOrWhiteSpace(command.Category) || string.IsNullOrWhiteSpace(command.Region) || string.IsNullOrWhiteSpace(command.Topic) || command.Hall is null || command.Notes is null || command.ExpectedEvidence is null) throw new ArgumentException("请填写必填的谱系资料。"); return store.UpdateAsync(command,ct); }
    public static string Evidence(LineageHome home) => System.Text.Json.JsonSerializer.Serialize(new[]{home.Name,home.Category,home.Region,home.Topic,home.Hall,home.Notes});
}
