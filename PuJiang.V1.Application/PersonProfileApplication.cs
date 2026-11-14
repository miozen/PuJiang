using PuJiang.Domain;

namespace PuJiang.V1.Application;

public sealed record PersonProfile(Guid PersonId, Guid PlacementId, string Name, PersonSex Sex);
public sealed record UpdatePersonProfile(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid PersonId, Guid PlacementId, string Name, PersonSex Sex, string ExpectedName, PersonSex ExpectedSex);
public interface IPersonProfileStore
{
    Task<PersonProfile> ReadAsync(Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, CancellationToken cancellationToken);
    Task UpdateAsync(UpdatePersonProfile command, CancellationToken cancellationToken);
    Task<bool> HasReceiptAsync(Guid operationId, CancellationToken cancellationToken);
}
public sealed class PersonProfileApplication(IPersonProfileStore store)
{
    public Task<PersonProfile> ReadAsync(Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, CancellationToken cancellationToken = default)
        => store.ReadAsync(genealogyId, lineageId, personId, placementId, cancellationToken);
    public Task<bool> HasReceiptAsync(Guid operationId, CancellationToken cancellationToken = default) => store.HasReceiptAsync(operationId, cancellationToken);
    public Task UpdateAsync(UpdatePersonProfile command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty || command.PersonId == Guid.Empty || command.PlacementId == Guid.Empty ||
            command.Name is null || command.ExpectedName is null || command.Sex is not (PersonSex.Male or PersonSex.Female) || !Enum.IsDefined(command.ExpectedSex))
            throw new ArgumentException("人物资料或目标身份无效。");
        return store.UpdateAsync(command, cancellationToken);
    }
}
