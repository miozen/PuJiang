using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.Data;

public sealed record ChartChoiceReview(LineageChartChoice Choice, string? Problem)
{
    public string Label => $"{Choice.PersonName}（{Choice.PersonId}）→ {Choice.ParentName}（{Choice.ParentId}）；关系 {Choice.RelationshipId}；类型 {Choice.Kind}" +
        (Problem is null ? "；有效" : $"；{Problem}");
}
public sealed record ChartReview(LineageChart Chart, bool GenerationChanged, IReadOnlyList<ChartChoiceReview> Choices);

public sealed class LineageChartService(ZupuDbContext context)
{
    public Task<List<LineageChart>> ListAsync(Guid genealogyId) =>
        context.LineageCharts.AsNoTracking().Where(c => c.GenealogyId == genealogyId).OrderBy(c => c.Name).ThenBy(c => c.Id).ToListAsync();

    public async Task<ChartReview> ReviewAsync(Guid genealogyId, Guid chartId, GenealogyGraphSnapshot snapshot)
    {
        if (snapshot.Genealogy.Id != genealogyId) throw new InvalidOperationException("族谱不匹配。");
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync();
        if (generation != snapshot.UnlinkGeneration) throw new InvalidOperationException("谱系已变化，请关闭并重新打开。");
        var chart = await context.LineageCharts.AsNoTracking().Include(c => c.Choices)
            .SingleAsync(c => c.Id == chartId && c.GenealogyId == genealogyId);
        var people = snapshot.People.ToDictionary(p => p.Id);
        var relationships = snapshot.Relationships.ToDictionary(r => r.Id);
        var choices = chart.Choices.Select(c =>
        {
            string? problem = null;
            if (!people.ContainsKey(c.PersonId) || !people.ContainsKey(c.ParentId)) problem = "人物已不存在";
            else if (!relationships.TryGetValue(c.RelationshipId, out var relation)) problem = "关系已不存在";
            else if (relation.PersonId != c.PersonId || relation.RelatedPersonId != c.ParentId || relation.Kind != c.Kind)
                problem = "关系双方或类型已改变";
            else if (!snapshot.Relationships.Any(r => r.PersonId == c.PersonId && r.Id == c.RelationshipId &&
                r.Kind is PersonRelationshipKind.BiologicalParent or PersonRelationshipKind.AdoptiveParent or PersonRelationshipKind.HeirParent))
                problem = "不再是可选父母关系";
            return new ChartChoiceReview(c, problem);
        }).ToList();
        return new ChartReview(chart, chart.Generation != generation, choices);
    }

    public async Task<LineageChart> SaveAsync(Guid genealogyId, Guid? chartId, string name, long? expectedRevision,
        GenealogyGraphSnapshot snapshot, IReadOnlyDictionary<Guid, Guid> selections)
    {
        name = name.Trim();
        if (name.Length is 0 or > 200) throw new InvalidOperationException("图表名称须为 1 至 200 字。");
        if (snapshot.Genealogy.Id != genealogyId) throw new InvalidOperationException("族谱不匹配。");
        var session = new LineageProjectionSession(snapshot);
        foreach (var (person, relation) in selections) session.Choose(person, relation);
        var rows = session.Project().ToDictionary(r => r.Person.Id);
        var choices = selections.Select(entry =>
        {
            var row = rows[entry.Key];
            var option = row.ParentCandidates.Single(c => c.Relationship.Id == entry.Value);
            return new LineageChartChoice { PersonId = row.Person.Id, PersonName = row.Person.Name,
                ParentId = option.Parent.Id, ParentName = option.Parent.Name, RelationshipId = entry.Value, Kind = option.Relationship.Kind };
        }).ToList();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync();
        if (generation != snapshot.UnlinkGeneration) throw new InvalidOperationException("谱系已变化，请重新打开后复核。");
        LineageChart chart;
        if (chartId is null)
        {
            chart = new LineageChart { GenealogyId = genealogyId, Name = name, Generation = generation, Revision = 1 };
            context.LineageCharts.Add(chart);
        }
        else
        {
            if (expectedRevision is null || expectedRevision == long.MaxValue) throw new InvalidOperationException("图表配置版本无效。");
            var changed = await context.LineageCharts.Where(c => c.Id == chartId && c.GenealogyId == genealogyId && c.Revision == expectedRevision)
                .Where(c => context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).Any(v => v == snapshot.UnlinkGeneration))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, c => c.Revision + 1));
            if (changed != 1) throw new InvalidOperationException("图表配置或谱系已变化，请重新打开。");
            chart = await context.LineageCharts.Include(c => c.Choices).SingleAsync(c => c.Id == chartId && c.GenealogyId == genealogyId);
            context.LineageChartChoices.RemoveRange(chart.Choices);
            chart.Choices.Clear();
            chart.Name = name;
            chart.Generation = generation;
            chart.Revision = checked(expectedRevision.Value + 1);
            await context.SaveChangesAsync();
        }
        foreach (var choice in choices) chart.Choices.Add(choice);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return chart;
    }
}
