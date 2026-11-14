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
public enum PersonSex { Unknown = 0, Male = 1, Female = 2 }

public sealed class Person(Guid id, Guid lineageId, string name, PersonSex sex = PersonSex.Unknown)
{
    public Guid Id { get; private set; } = Identity.Require(id);
    public Guid LineageId { get; private set; } = Identity.Require(lineageId);
    public string Name { get; private set; } = name ?? throw new ArgumentNullException(nameof(name));
    public PersonSex Sex { get; private set; } = Enum.IsDefined(sex) ? sex : throw new ArgumentOutOfRangeException(nameof(sex));
    public bool MotherExplicitlyCleared { get; private set; }
    public void MarkMotherCleared() => MotherExplicitlyCleared = true;
    public void MarkMotherKnown() => MotherExplicitlyCleared = false;
    public void UpdateProfile(string name, PersonSex sex)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Sex = Enum.IsDefined(sex) ? sex : throw new ArgumentOutOfRangeException(nameof(sex));
    }
}
public sealed class Placement(Guid id, Guid lineageId, Guid personId, Guid? parentPlacementId = null, int? position = null)
{
    public Guid Id { get; private set; } = Identity.Require(id);
    public Guid LineageId { get; private set; } = Identity.Require(lineageId);
    public Guid PersonId { get; private set; } = Identity.Require(personId);
    public Guid? ParentPlacementId { get; private set; } = parentPlacementId is null ? null : Identity.Require(parentPlacementId.Value);
    public int? Position { get; private set; } = parentPlacementId is null && position is null || parentPlacementId is not null && position > 0
        ? position : throw new ArgumentException("根位置无排行，子位置必须有正排行。");
    public void SetPosition(int position)
    {
        if (ParentPlacementId is null || position <= 0) throw new ArgumentException("只有子位置能设置正排行。");
        Position = position;
    }
}

// A recorded biological parent fact; other kinship types are not inferred here.
public sealed class BiologicalParent(Guid id, Guid lineageId, Guid childId, Guid parentId, ParentRole role)
{
    public Guid Id { get; private set; } = Identity.Require(id);
    public Guid LineageId { get; private set; } = Identity.Require(lineageId);
    public Guid ChildId { get; private set; } = Identity.Require(childId);
    public Guid ParentId { get; private set; } = parentId != childId ? Identity.Require(parentId) : throw new ArgumentException("父母与子女不能是同一人。");
    public ParentRole Role { get; private set; } = Enum.IsDefined(role) ? role : throw new ArgumentOutOfRangeException(nameof(role));
    public void ChangeParent(Guid parentId)
    {
        if (parentId == ChildId) throw new ArgumentException("父母与子女不能是同一人。");
        ParentId = Identity.Require(parentId);
    }
}
public enum ParentRole { Father = 1, Mother = 2 }

// The first confirmed slice supports only an explicit father and a female spouse.
public sealed class SpouseUnion(Guid id, Guid lineageId, Guid fatherId, Guid spouseId)
{
    public Guid Id { get; private set; } = Identity.Require(id);
    public Guid LineageId { get; private set; } = Identity.Require(lineageId);
    public Guid FatherId { get; private set; } = Identity.Require(fatherId);
    public Guid SpouseId { get; private set; } = spouseId != fatherId ? Identity.Require(spouseId) : throw new ArgumentException("配偶不能是同一人物。");
}
