using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;

namespace PuJiang.V1.Persistence;

internal sealed class PersonProfileReceipt
{
    public Guid OperationId { get; set; } public Guid GenealogyId { get; set; } public Guid LineageId { get; set; }
    public Guid PersonId { get; set; } public Guid PlacementId { get; set; } public string Name { get; set; } = "";
    public PersonSex Sex { get; set; } public string ExpectedName { get; set; } = ""; public PersonSex ExpectedSex { get; set; }
}

public sealed class IsolatedPersonProfileStore(IsolatedTestDatabase database) : IPersonProfileStore
{
    public async Task<bool> HasReceiptAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext();
        return await db.PersonProfileReceipts.AsNoTracking().AnyAsync(x => x.OperationId == operationId, cancellationToken);
    }
    private static async Task<(Person Person, Placement Place)> Load(V1Context db, Guid g, Guid l, Guid id, Guid placeId, CancellationToken ct)
    {
        if (!await db.Lineages.AnyAsync(x => x.Id == l && x.GenealogyId == g, ct)) throw new InvalidOperationException("目标谱系不存在。");
        var person = await db.People.SingleOrDefaultAsync(x => x.Id == id && x.LineageId == l, ct) ?? throw new InvalidOperationException("人物不存在。");
        var place = await db.Placements.SingleOrDefaultAsync(x => x.Id == placeId && x.LineageId == l, ct) ?? throw new InvalidOperationException("人物位置不匹配。");
        if (place.PersonId != id && !await db.SpouseUnions.AnyAsync(x => x.LineageId == l && x.FatherId == place.PersonId && x.SpouseId == id, ct))
            throw new InvalidOperationException("人物与当前位置无明确的配偶关系。");
        return (person, place);
    }
    public async Task<PersonProfile> ReadAsync(Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext();
        var (p, _) = await Load(db, genealogyId, lineageId, personId, placementId, cancellationToken);
        return new(p.Id, placementId, p.Name, p.Sex);
    }
    public async Task UpdateAsync(UpdatePersonProfile command, CancellationToken cancellationToken)
    {
        // Validate even when the store is called directly.
        if (command.OperationId == Guid.Empty || command.Name is null || command.ExpectedName is null || command.Sex is not (PersonSex.Male or PersonSex.Female))
            throw new ArgumentException("人物资料无效。");
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var receipt = await db.PersonProfileReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);
        if (receipt is not null)
        {
            if (receipt.GenealogyId != command.GenealogyId || receipt.LineageId != command.LineageId || receipt.PersonId != command.PersonId ||
                receipt.PlacementId != command.PlacementId || receipt.Name != command.Name || receipt.Sex != command.Sex ||
                receipt.ExpectedName != command.ExpectedName || receipt.ExpectedSex != command.ExpectedSex)
                throw new InvalidOperationException("操作身份已用于不同输入。");
            return;
        }
        var (person, place) = await Load(db, command.GenealogyId, command.LineageId, command.PersonId, command.PlacementId, cancellationToken);
        if (person.Name != command.ExpectedName || person.Sex != command.ExpectedSex) throw new InvalidOperationException("人物资料已变化，请重新读取。");
        if (person.Sex != command.Sex)
        {
            var positionCount = await db.Placements.CountAsync(x => x.LineageId == command.LineageId && x.PersonId == person.Id, cancellationToken);
            if (positionCount > 1 || place.PersonId == person.Id && positionCount != 1 || place.PersonId != person.Id && positionCount != 0)
                throw new InvalidOperationException("多位置排行尚未接入，请先核实。");
            if (await db.BiologicalParents.AnyAsync(x => x.LineageId == command.LineageId && x.ParentId == person.Id &&
                (x.Role == ParentRole.Father && command.Sex != PersonSex.Male || x.Role == ParentRole.Mother && command.Sex != PersonSex.Female), cancellationToken))
                throw new InvalidOperationException("性别与已有父母角色不一致，请先核实。");
            if (place.PersonId == person.Id && place.ParentPlacementId is Guid parent)
            {
                var siblings = await (from p in db.Placements where p.LineageId == command.LineageId && p.ParentPlacementId == parent
                    join sibling in db.People on new { p.LineageId, Id = p.PersonId } equals new { sibling.LineageId, sibling.Id }
                    select new { Place = p, Person = sibling }).ToListAsync(cancellationToken);
                var ordered = siblings.OrderBy(x => x.Place.Position).ToList();
                var otherSexes = ordered.Where(x => x.Person.Id != person.Id).Select(x => x.Person.Sex).ToArray();
                if (otherSexes.Any(x => x == PersonSex.Unknown) || ordered.Any(x => x.Place.Position is null) ||
                    !ordered.Select(x => x.Place.Position!.Value).SequenceEqual(Enumerable.Range(1, ordered.Count)) ||
                    !otherSexes.SequenceEqual(otherSexes.Order()))
                    throw new InvalidOperationException("子女排序或性别尚不能安全确认，请先核实。");
                var selected = ordered.Single(x => x.Person.Id == person.Id); ordered.Remove(selected);
                var target = command.Sex == PersonSex.Male ? ordered.Count(x => x.Person.Sex == PersonSex.Male) : ordered.Count;
                ordered.Insert(target, selected);
                for (int i = 0; i < ordered.Count; i++) ordered[i].Place.SetPosition(i + 1);
            }
        }
        person.UpdateProfile(command.Name, command.Sex);
        db.PersonProfileReceipts.Add(new() { OperationId=command.OperationId, GenealogyId=command.GenealogyId, LineageId=command.LineageId,
            PersonId=command.PersonId, PlacementId=command.PlacementId, Name=command.Name, Sex=command.Sex, ExpectedName=command.ExpectedName, ExpectedSex=command.ExpectedSex });
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
    }
}
