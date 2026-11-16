using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;

namespace PuJiang.V1.Persistence;

internal sealed class GenerationReceipt { public Guid OperationId { get; set; } public string Payload { get; set; } = ""; }

// This isolated slice only supports a single-position ordinary father/child tree.
public sealed class IsolatedGenerationStore(IsolatedTestDatabase database) : IGenerationStore
{
    private sealed record State(List<Person> People, List<Placement> Places, List<BiologicalParent> Relations, Person Target, Placement Place, string Evidence);

    private static async Task<State> Load(V1Context db, Guid genealogy, Guid lineage, Guid personId, Guid placeId, CancellationToken ct)
    {
        if (!await db.Lineages.AnyAsync(x => x.Id == lineage && x.GenealogyId == genealogy, ct)) throw new InvalidOperationException("谱系不存在。");
        var people = await db.People.Where(x => x.LineageId == lineage).ToListAsync(ct);
        var places = await db.Placements.Where(x => x.LineageId == lineage).ToListAsync(ct);
        var relations = await db.BiologicalParents.Where(x => x.LineageId == lineage).ToListAsync(ct);
        var target = people.SingleOrDefault(x => x.Id == personId) ?? throw new InvalidOperationException("人物不存在。");
        var place = places.SingleOrDefault(x => x.Id == placeId && x.PersonId == personId) ?? throw new InvalidOperationException("人物位置失效。");
        if (target.ExplicitGeneration is null or <= 0) throw new InvalidOperationException("人物世代不明确，请先核实。");
        var data = new {
            People = people.Where(x => places.Any(p => p.PersonId == x.Id)).OrderBy(x => x.Id).Select(x => new { x.Id, x.ExplicitGeneration }).ToArray(),
            Places = places.OrderBy(x => x.Id).Select(x => new { x.Id, x.PersonId, x.ParentPlacementId }).ToArray(),
            Relations = relations.OrderBy(x => x.Id).Select(x => new { x.Id, x.ChildId, x.ParentId, x.Role }).ToArray()
        };
        var evidence = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
        return new(people, places, relations, target, place, evidence);
    }

    public async Task<GenerationSnapshot> ReadAsync(Guid g, Guid l, Guid person, Guid place, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var state = await Load(db, g, l, person, place, ct); await tx.CommitAsync(ct);
        return new(person, place, state.Target.ExplicitGeneration!.Value, state.Evidence);
    }
    public async Task<bool> HasReceiptAsync(Guid operationId, CancellationToken ct)
    { await using var db = database.OpenContext(); return await db.GenerationReceipts.AsNoTracking().AnyAsync(x => x.OperationId == operationId, ct); }

    public async Task UpdateAsync(UpdateGeneration command, CancellationToken ct)
    {
        if (command.OperationId == Guid.Empty || command.Generation <= 0 || string.IsNullOrEmpty(command.ExpectedEvidence)) throw new ArgumentException("世代操作无效。");
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payload = JsonSerializer.Serialize(command); var receipt = await db.GenerationReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, ct);
        if (receipt is not null) { if (receipt.Payload != payload) throw new InvalidOperationException("操作身份已用于不同输入。"); return; }
        var state = await Load(db, command.GenealogyId, command.LineageId, command.PersonId, command.PlacementId, ct);
        if (state.Evidence != command.ExpectedEvidence) throw new InvalidOperationException("谱系或世代已变化，请重新读取；草稿已保留。");
        var positions = state.Places.GroupBy(x => x.PersonId).ToDictionary(x => x.Key, x => x.Count());
        var queue = new Queue<Placement>(); var seen = new HashSet<Guid>(); var descendants = new List<Person>(); queue.Enqueue(state.Place);
        while (queue.TryDequeue(out var current))
        {
            if (!seen.Add(current.Id) || positions[current.PersonId] != 1) throw new InvalidOperationException("多位置或循环关系尚不能安全处理。");
            var person = state.People.Single(x => x.Id == current.PersonId);
            if (person.ExplicitGeneration is null or <= 0) throw new InvalidOperationException("后代世代不明确，请先核实。");
            var fathers = state.Relations.Where(x => x.ChildId == person.Id && x.Role == ParentRole.Father).ToArray();
            if (current.ParentPlacementId is Guid parentPlaceId)
            {
                var parentPlace = state.Places.SingleOrDefault(x => x.Id == parentPlaceId) ?? throw new InvalidOperationException("父位置不存在。");
                var father = state.People.SingleOrDefault(x => x.Id == parentPlace.PersonId) ?? throw new InvalidOperationException("父人物不存在。");
                if (fathers.Length != 1 || fathers[0].ParentId != father.Id || father.ExplicitGeneration is null or <= 0 || person.ExplicitGeneration <= father.ExplicitGeneration)
                    throw new InvalidOperationException("父子关系或世代不一致，请先核实。");
            }
            else if (fathers.Length != 0) throw new InvalidOperationException("独立位置与生父关系不一致，请先核实。");
            if (state.Relations.Any(x => x.ChildId == person.Id && x.Role == ParentRole.Mother && state.Places.Any(p => p.PersonId == x.ParentId)))
                throw new InvalidOperationException("主线位置同时涉及生母关系，暂不能安全同步。");
            if (current.Id != state.Place.Id) descendants.Add(person);
            foreach (var child in state.Places.Where(x => x.ParentPlacementId == current.Id))
            {
                if (!state.Relations.Any(x => x.ChildId == child.PersonId && x.ParentId == current.PersonId && x.Role == ParentRole.Father)) throw new InvalidOperationException("父子关系与位置不一致。");
                queue.Enqueue(child);
            }
            if (state.Relations.Any(x => x.ParentId == person.Id && x.Role == ParentRole.Father && !state.Places.Any(p => p.PersonId == x.ChildId && p.ParentPlacementId == current.Id)))
                throw new InvalidOperationException("父子关系与位置不一致，暂不能安全同步。");
        }
        var delta = (long)command.Generation - state.Target.ExplicitGeneration!.Value;
        foreach (var child in descendants)
        {
            var shifted = child.ExplicitGeneration!.Value + delta;
            if (shifted <= 0 || shifted > int.MaxValue) throw new InvalidOperationException("后代世代调整超出有效范围。");
        }
        if (state.Place.ParentPlacementId is Guid parentId)
        {
            var parentPlace = state.Places.Single(x => x.Id == parentId);
            var parent = state.People.Single(x => x.Id == parentPlace.PersonId);
            if (parent.ExplicitGeneration is null || (long)command.Generation != (long)parent.ExplicitGeneration + 1) throw new InvalidOperationException("普通子女世代必须为父亲的下一世，请更正首节点世代。");
        }
        state.Target.SetGeneration(command.Generation);
        foreach (var child in descendants) child.SetGeneration((int)(child.ExplicitGeneration!.Value + delta));
        db.GenerationReceipts.Add(new() { OperationId = command.OperationId, Payload = payload });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
