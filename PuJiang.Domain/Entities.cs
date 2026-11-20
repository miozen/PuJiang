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
    public string Category { get; private set; } = "";
    public string Region { get; private set; } = "";
    public string Topic { get; private set; } = "";
    public string Hall { get; private set; } = "";
    public string Notes { get; private set; } = "";
    public void SetInformation(string category, string region, string topic, string hall, string notes)
    { Category = category; Region = region; Topic = topic; Hall = hall; Notes = notes; }
    public void Rename(string name) { Name = name ?? throw new ArgumentNullException(nameof(name)); }
}
public enum PersonSex { Unknown = 0, Male = 1, Female = 2 }

public sealed class Person(Guid id, Guid lineageId, string name, PersonSex sex = PersonSex.Unknown)
{
    public Guid Id { get; private set; } = Identity.Require(id);
    public Guid LineageId { get; private set; } = Identity.Require(lineageId);
    public string Name { get; private set; } = !string.IsNullOrWhiteSpace(name) ? name : throw new ArgumentException("人物姓名不能为空。", nameof(name));
    public PersonSex Sex { get; private set; } = Enum.IsDefined(sex) ? sex : throw new ArgumentOutOfRangeException(nameof(sex));
    public bool MotherExplicitlyCleared { get; private set; }
    public int? ExplicitGeneration { get; private set; }
    public string Title { get; private set; } = "";
    public string Notes { get; private set; } = "";
    public void SetRootInformation(int? generation, string title, string notes)
    { if (generation is <= 0) throw new ArgumentException("世代必须为正数。"); ExplicitGeneration = generation; Title = title; Notes = notes; }
    public void SetGeneration(int generation)
    { if (generation <= 0) throw new ArgumentException("世代必须为正数。"); ExplicitGeneration = generation; }
    public void MarkMotherCleared() => MotherExplicitlyCleared = true;
    public void MarkMotherKnown() => MotherExplicitlyCleared = false;
    public void UpdateProfile(string name, PersonSex sex)
    {
        Name = !string.IsNullOrWhiteSpace(name) ? name : throw new ArgumentException("人物姓名不能为空。", nameof(name));
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
    public void Detach()
    {
        if (ParentPlacementId is null) throw new InvalidOperationException("当前位置已经是独立分支。");
        ParentPlacementId = null; Position = null;
    }
    public void Attach(Guid parentPlacementId, int position)
    {
        if (ParentPlacementId is not null || parentPlacementId == Id || position <= 0) throw new InvalidOperationException("挂接位置无效。");
        ParentPlacementId = Identity.Require(parentPlacementId); Position = position;
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
