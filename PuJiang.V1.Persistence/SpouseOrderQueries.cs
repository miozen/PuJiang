using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;

namespace PuJiang.V1.Persistence;

internal sealed record OrderedUnion(SpouseUnion Union, int Position);
internal static class SpouseOrderQueries
{
    internal static async Task<(SpouseOrderGroup? Group, List<OrderedUnion> Items)> Read(V1Context db, Guid lineage, Guid father, CancellationToken ct)
    {
        var group = await db.SpouseOrderGroups.SingleOrDefaultAsync(x => x.LineageId == lineage && x.FatherId == father, ct);
        var unions = await db.SpouseUnions.Where(x => x.LineageId == lineage && x.FatherId == father).ToListAsync(ct);
        var entries = await db.SpouseOrderEntries.Where(x => x.LineageId == lineage && x.FatherId == father).OrderBy(x => x.Position).ToListAsync(ct);
        if ((group is null && (unions.Count != 0 || entries.Count != 0)) || entries.Count != unions.Count ||
            !entries.Select(x => x.Position).SequenceEqual(Enumerable.Range(1, entries.Count)))
            throw new InvalidOperationException("配偶排序缺失或损坏，请先核实并重新确认排序。");
        var byId = unions.ToDictionary(x => x.Id);
        if (entries.Any(x => !byId.ContainsKey(x.UnionId))) throw new InvalidOperationException("配偶排序引用无效，请先核实。");
        return (group, entries.Select(x => new OrderedUnion(byId[x.UnionId], x.Position)).ToList());
    }
}
