namespace PuJiang.Domain;

internal static class Identity
{
    public static Guid Require(Guid value) => value != Guid.Empty ? value : throw new ArgumentException("Identity must not be empty.");
}
public sealed class Genealogy(Guid id, string name, string? surname)
{
    public Guid Id { get; private set; } = Identity.Require(id);
    public string Name { get; private set; } = name ?? throw new ArgumentNullException(nameof(name));
    public string? Surname { get; private set; } = surname;
}
public sealed class Lineage(Guid id, Guid genealogyId, string name)
{
    public Guid Id { get; private set; } = Identity.Require(id);
    public Guid GenealogyId { get; private set; } = Identity.Require(genealogyId);
    public string Name { get; private set; } = name ?? throw new ArgumentNullException(nameof(name));
}
public sealed class Person(Guid id, Guid lineageId, string name)
{
    public Guid Id { get; private set; } = Identity.Require(id);
    public Guid LineageId { get; private set; } = Identity.Require(lineageId);
    public string Name { get; private set; } = name ?? throw new ArgumentNullException(nameof(name));
}
// Independent roots only; parentage and succession remain unspecified.
public sealed class Placement(Guid id, Guid lineageId, Guid personId)
{
    public Guid Id { get; private set; } = Identity.Require(id);
    public Guid LineageId { get; private set; } = Identity.Require(lineageId);
    public Guid PersonId { get; private set; } = Identity.Require(personId);
}
