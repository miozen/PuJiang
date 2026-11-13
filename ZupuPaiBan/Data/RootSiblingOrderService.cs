using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public sealed record RootSiblingMember(Guid Id, PersonSex Sex, string? RecordedGeneration);
public sealed record RootSiblingCapture(Guid GenealogyId, Guid ChartId, Guid AnchorId, long Revision, long Generation,
    Guid? GroupId, IReadOnlyList<RootSiblingMember> Members, bool HasSavedOrder);

/// <summary>Chart-specific display order; membership does not imply kinship.</summary>
public sealed class RootSiblingOrderService(ZupuDbContext context)
{
    public async Task<RootSiblingCapture> CaptureAsync(Guid genealogy, Guid chart, Guid anchor, CancellationToken cancellationToken = default)
    {
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        var result = await ReadAsync(genealogy, chart, anchor, cancellationToken);
        await tx.CommitAsync(cancellationToken); return result;
    }

    internal async Task<RootSiblingCapture> ReadAsync(Guid genealogy, Guid chart, Guid anchor, CancellationToken cancellationToken)
    {
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        var chartRow = await context.LineageCharts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == chart && c.GenealogyId == genealogy, cancellationToken)
            ?? throw new InvalidOperationException("图表不属于当前族谱。");
        if (chartRow.Generation != generation) throw new InvalidOperationException("图表主线已失效，请先复核。");
        var groups = await context.LineageRootSiblingOrders.AsNoTracking().Include(g => g.Items)
            .Where(g => g.ChartId == chart && g.Items.Any(i => i.PersonId == anchor)).ToListAsync(cancellationToken);
        if (groups.Count > 1) throw new InvalidOperationException("多个历史排序组相交，请先复核。");
        HashSet<Guid> ids;
        if (groups.Count == 0)
        {
            var anchorPerson = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == anchor && p.GenealogyId == genealogy, cancellationToken)
                ?? throw new InvalidOperationException("人物不存在或跨谱。");
            if (!RecordedGenerationParser.TryParse(anchorPerson.RecordedGeneration, out var anchorGeneration))
                throw new InvalidOperationException("人物世代不明确，请先核实。");
            var parentless = await context.People.AsNoTracking().Where(p => p.GenealogyId == genealogy &&
                !context.PersonRelationships.Any(r => r.GenealogyId == genealogy && r.PersonId == p.Id &&
                    (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent)))
                .Select(p => new { p.Id, p.RecordedGeneration }).ToListAsync(cancellationToken);
            ids = parentless.Where(p => RecordedGenerationParser.TryParse(p.RecordedGeneration, out var n) && n == anchorGeneration).Select(p => p.Id).ToHashSet();
            if (!ids.Contains(anchor)) throw new InvalidOperationException("人物存在父母记录，请先处理主线。");
            if (ids.Count > 2000) throw new InvalidOperationException("同世排序组超出允许上限。");
        }
        else ids = groups[0].Items.Select(i => i.PersonId).ToHashSet();
        var people = new Dictionary<Guid, RootSiblingMember>(); int? groupGeneration = null;
        foreach (var id in ids)
        {
            var person = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.GenealogyId == genealogy, cancellationToken)
                ?? throw new InvalidOperationException("兄妹人物不存在或跨谱。");
            if (person.Sex is not (PersonSex.Male or PersonSex.Female)) throw new InvalidOperationException("请先更正人物性别，再参与排序。");
            if (!RecordedGenerationParser.TryParse(person.RecordedGeneration, out var current) || (groupGeneration is not null && current != groupGeneration))
                throw new InvalidOperationException("兄妹世代不明确或不一致，请先核实。");
            groupGeneration = current; people[id] = new(id, person.Sex, person.RecordedGeneration);
            if (await context.PersonRelationships.AsNoTracking().AnyAsync(r => r.GenealogyId == genealogy && r.PersonId == id &&
                (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent), cancellationToken))
                throw new InvalidOperationException("兄妹组存在父母记录，请先处理主线。");
        }
        if (groups.Count == 0) return new(genealogy, chart, anchor, chartRow.Revision, generation, null, people.Values.ToArray(), false);
        var group = groups[0]; var items = group.Items.OrderBy(i => i.Position).ToArray();
        if (group.EvidenceGeneration != generation || !items.Select(i => i.Position).SequenceEqual(Enumerable.Range(1, items.Length)) ||
            !items.Select(i => i.PersonId).ToHashSet().SetEquals(ids) || items.Any(i => !people.TryGetValue(i.PersonId, out var p) || p.Sex != i.Sex || p.RecordedGeneration != i.RecordedGeneration))
            throw new InvalidOperationException("图表兄妹顺序已失效，请先复核。");
        var ordered = items.Select(i => people[i.PersonId]).ToArray();
        WorkbenchOrderRules.Validate(ordered.Select(p => new WorkbenchOrderRules.Member(p.Id, p.Sex)).ToArray(), true);
        return new(genealogy, chart, anchor, chartRow.Revision, generation, group.Id, ordered, true);
    }

    public async Task SaveAsync(RootSiblingCapture capture, IReadOnlyList<Guid> order, CancellationToken cancellationToken = default)
    {
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        var current = await ReadAsync(capture.GenealogyId, capture.ChartId, capture.AnchorId, cancellationToken);
        if (current.Revision != capture.Revision || current.Generation != capture.Generation || current.GroupId != capture.GroupId ||
            !current.Members.OrderBy(m => m.Id).SequenceEqual(capture.Members.OrderBy(m => m.Id))) throw new InvalidOperationException("图表或兄妹组已变化，请重新读取。");
        if (order.Count != current.Members.Count || !order.ToHashSet().SetEquals(current.Members.Select(m => m.Id)) || order.Distinct().Count() != order.Count)
            throw new InvalidOperationException("必须完整指定且不能重复人物。");
        var byId = current.Members.ToDictionary(m => m.Id);
        WorkbenchOrderRules.Validate(order.Select(id => new WorkbenchOrderRules.Member(id, byId[id].Sex)).ToArray(), true);
        var changed = await context.LineageCharts.Where(c => c.Id == capture.ChartId && c.GenealogyId == capture.GenealogyId && c.Revision == capture.Revision && c.Revision < long.MaxValue)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, c => c.Revision + 1), cancellationToken);
        if (changed != 1) throw new InvalidOperationException("图表已变化，请重新读取。");
        var groupId = current.GroupId ?? Guid.NewGuid();
        if (current.GroupId is null)
            await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageRootSiblingOrders (Id,ChartId,EvidenceGeneration) VALUES (@id,@chart,@generation)",
                new object[] { Sql("@id", groupId), Sql("@chart", capture.ChartId), new SqliteParameter("@generation", capture.Generation) }, cancellationToken);
        else await context.Database.ExecuteSqlRawAsync("DELETE FROM LineageRootSiblingOrderItems WHERE GroupId=@id", new object[] { Sql("@id", groupId) }, cancellationToken);
        await InsertItemsAsync(context, groupId, order.Select(id => byId[id]).ToArray(), cancellationToken);
        await tx.CommitAsync(CancellationToken.None);
    }

    internal static async Task InsertItemsAsync(ZupuDbContext context, Guid groupId, IReadOnlyList<RootSiblingMember> members, CancellationToken cancellationToken)
    {
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageRootSiblingOrderItems (GroupId,PersonId,Position,Sex,RecordedGeneration) VALUES (@group,@person,@position,@sex,@generation)",
                new object[] { Sql("@group", groupId), Sql("@person", member.Id), new SqliteParameter("@position", i + 1), new SqliteParameter("@sex", (int)member.Sex),
                    new SqliteParameter("@generation", (object?)member.RecordedGeneration ?? DBNull.Value) }, cancellationToken);
        }
    }
    private static SqliteParameter Sql(string name, Guid value) => new(name, value.ToString().ToUpperInvariant());
}
