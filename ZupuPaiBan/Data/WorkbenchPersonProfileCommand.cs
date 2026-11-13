using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public sealed record WorkbenchProfileValues(string Name, PersonSex Sex, string? BirthDate, string? DeathDate, string? Biography, string? RecordedGeneration = null);
public sealed record WorkbenchParentInfo(Guid PersonId, string Name, PersonSex Sex, PersonRelationshipKind Kind);
public sealed record WorkbenchUnionValues(FamilyUnionKind Kind, string? StartDateText, string? EndDateText, string? Note);
public sealed record WorkbenchUnionInfo(Guid Id, Guid? PartnerId, string PartnerName, WorkbenchUnionValues Values, PersonSex PartnerSex = PersonSex.Unknown);
public sealed record WorkbenchChildInfo(Guid PersonId, string Name, Guid? OtherParentId, string OtherParentName, string? BlockReason);
public sealed record ChartChildOrderMember(Guid PersonId, PersonSex Sex, Guid RelationshipId, PersonRelationshipKind Kind, string Name, string? RecordedGeneration, string? RecordedBirthOrder);
public sealed record WorkbenchProfileSnapshot(Guid GenealogyId, Guid PersonId, long Generation, WorkbenchProfileValues Values,
    bool IsNew = false, Guid? ParentId = null, PersonRelationshipKind? ParentKind = null,
    Guid? PartnerId = null, FamilyUnionKind? UnionKind = null, Guid? SiblingId = null,
    Guid? SecondParentId = null, bool IsOrdinaryChild = false, PersonSex? ParentSex = null,
    IReadOnlyList<WorkbenchParentInfo>? Parents = null, bool IsOrdinarySibling = false, IReadOnlyList<WorkbenchUnionInfo>? Unions = null,
    IReadOnlyList<WorkbenchChildInfo>? Children = null, Guid? ChartId = null, long? ChartRevision = null,
    bool IsChartSpouse = false, Guid? ChartSpouseGroupId = null, IReadOnlyList<SpouseOrderEvidence>? ChartSpouseOrder = null,
    Guid? ChartChildGroupId = null, IReadOnlyList<ChartChildOrderMember>? ChartChildOrder = null, bool IsChartAdoptedChild = false,
    bool IsChartSibling = false, RootSiblingCapture? ChartRootSiblings = null, bool IsChartIndependent = false);

/// <summary>Single-person command boundary. No UI or view state is used as data authority.</summary>
public sealed class WorkbenchPersonProfileCommand(ZupuDbContext context)
{
    public async Task<WorkbenchProfileSnapshot> BeginOrdinarySiblingAsync(Guid genealogyId, Guid siblingId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        var sibling = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == siblingId, cancellationToken)
            ?? throw new InvalidOperationException("所选兄妹不属于当前族谱。");
        var parents = await ReadOrdinarySiblingParentsAsync(genealogyId, siblingId, cancellationToken);
        var recorded = RecordedGenerationParser.TryParse(sibling.RecordedGeneration, out var number) ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        await transaction.CommitAsync(cancellationToken);
        return new(genealogyId, Guid.NewGuid(), generation, new("", PersonSex.Male, null, null, null, recorded), IsNew: true,
            ParentId: parents.FirstOrDefault()?.PersonId, ParentKind: parents.Count > 0 ? PersonRelationshipKind.BiologicalParent : null,
            SiblingId: siblingId, SecondParentId: parents.Skip(1).FirstOrDefault()?.PersonId, Parents: parents, IsOrdinarySibling: true);
    }

    public async Task<WorkbenchProfileSnapshot> BeginOrdinarySiblingForChartAsync(Guid genealogyId, Guid siblingId, Guid chartId, CancellationToken cancellationToken = default)
    {
        var original = await BeginOrdinarySiblingAsync(genealogyId, siblingId, cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        if (generation != original.Generation) throw new InvalidOperationException("谱系数据已变化，请重新开始新增兄妹。");
        var parents = original.Parents ?? [];
        if (parents.Count == 0)
        {
            var root = await new RootSiblingOrderService(context).ReadAsync(genealogyId, chartId, siblingId, cancellationToken);
            if (!root.HasSavedOrder && root.Members.Count > 1) throw new InvalidOperationException("当前图表尚未保存兄妹顺序，请先确认排序。");
            if (root.Members.Count >= 2000) throw new InvalidOperationException("兄妹组已达允许上限。");
            await transaction.CommitAsync(cancellationToken);
            return original with { ChartId = chartId, ChartRevision = root.Revision, IsChartSibling = true, ChartRootSiblings = root };
        }
        var selected = parents[0].PersonId;
        if (parents.Count > 1)
        {
            var choice = await context.LineageChartChoices.AsNoTracking().SingleOrDefaultAsync(c => c.ChartId == chartId && c.PersonId == siblingId, cancellationToken)
                ?? throw new InvalidOperationException("当前图表尚未明确兄妹的主线父母。");
            if (choice.Kind != PersonRelationshipKind.BiologicalParent || !parents.Any(p => p.PersonId == choice.ParentId) ||
                !await context.PersonRelationships.AsNoTracking().AnyAsync(r => r.Id == choice.RelationshipId && r.PersonId == siblingId && r.RelatedPersonId == choice.ParentId && r.Kind == choice.Kind, cancellationToken))
                throw new InvalidOperationException("图表主线与生父母不一致，请先复核。");
            selected = choice.ParentId;
        }
        var parent = await context.People.AsNoTracking().SingleAsync(p => p.Id == selected && p.GenealogyId == genealogyId, cancellationToken);
        var children = await ReadChartChildOrderAsync(genealogyId, parent, chartId, generation, cancellationToken);
        var spouses = await ReadChartSpouseOrderAsync(genealogyId, selected, chartId, generation, cancellationToken);
        if (children.Revision != spouses.Revision || children.Items.Count >= 2000 || children.Items.All(m => m.PersonId != siblingId))
            throw new InvalidOperationException("图表顺序已变化或兄妹不属于当前主线组。");
        await transaction.CommitAsync(cancellationToken);
        return original with { ParentId = selected, SecondParentId = parents.Where(p => p.PersonId != selected).Select(p => (Guid?)p.PersonId).SingleOrDefault(),
            ChartId = chartId, ChartRevision = children.Revision, ChartChildGroupId = children.GroupId, ChartChildOrder = children.Items,
            ChartSpouseGroupId = spouses.GroupId, ChartSpouseOrder = spouses.Items, IsChartSibling = true };
    }

    private async Task<IReadOnlyList<WorkbenchParentInfo>> ReadOrdinarySiblingParentsAsync(Guid genealogyId, Guid siblingId, CancellationToken cancellationToken)
    {
        var relationships = await context.PersonRelationships.AsNoTracking().Where(r => r.GenealogyId == genealogyId && r.PersonId == siblingId && r.Kind == PersonRelationshipKind.BiologicalParent).ToListAsync(cancellationToken);
        var parents = new List<WorkbenchParentInfo>();
        foreach (var relationship in relationships)
        {
            var person = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == relationship.RelatedPersonId, cancellationToken)
                ?? throw new InvalidOperationException("生父母记录不完整或跨谱，请先核实。");
            if (person.Id == siblingId || person.Sex is not (PersonSex.Male or PersonSex.Female) || parents.Any(p => p.Sex == person.Sex))
                throw new InvalidOperationException("生父母角色不明确或存在重复候选，请先核实。");
            parents.Add(new(person.Id, person.Name, person.Sex, PersonRelationshipKind.BiologicalParent));
        }
        return parents.OrderBy(p => p.Sex).ToArray();
    }
    /// <summary>Reads the complete marriage set under the same version as the new-person draft.</summary>
    public async Task<WorkbenchProfileSnapshot> BeginOrdinaryChildAsync(Guid genealogyId, Guid parentId, CancellationToken cancellationToken = default)
        => await BeginOrdinaryChildCoreAsync(genealogyId, parentId, null, cancellationToken);

    public async Task<WorkbenchProfileSnapshot> BeginOrdinaryChildForChartAsync(Guid genealogyId, Guid parentId, Guid chartId, CancellationToken cancellationToken = default)
        => await BeginOrdinaryChildCoreAsync(genealogyId, parentId, chartId, cancellationToken);

    public async Task<WorkbenchProfileSnapshot> BeginAdoptedChildForChartAsync(Guid genealogyId, Guid parentId, Guid chartId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        var parent = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == parentId, cancellationToken)
            ?? throw new InvalidOperationException("所选父母不属于当前族谱。");
        var children = await ReadChartChildOrderAsync(genealogyId, parent, chartId, generation, cancellationToken);
        var spouses = await ReadChartSpouseOrderAsync(genealogyId, parentId, chartId, generation, cancellationToken);
        if (children.Revision != spouses.Revision || children.Items.Count >= 2000) throw new InvalidOperationException("图表已变化或子女组已达允许上限。");
        var recorded = RecordedGenerationParser.TryParse(parent.RecordedGeneration, out var number) && number < 1000 ? (number + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        await transaction.CommitAsync(cancellationToken);
        return new(genealogyId, Guid.NewGuid(), generation, new("", PersonSex.Male, null, null, null, recorded), IsNew: true,
            ParentId: parentId, ParentKind: PersonRelationshipKind.AdoptiveParent, ChartId: chartId, ChartRevision: children.Revision,
            ChartChildGroupId: children.GroupId, ChartChildOrder: children.Items, ChartSpouseGroupId: spouses.GroupId, ChartSpouseOrder: spouses.Items, IsChartAdoptedChild: true);
    }

    private async Task<WorkbenchProfileSnapshot> BeginOrdinaryChildCoreAsync(Guid genealogyId, Guid parentId, Guid? chartId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        var parent = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == parentId, cancellationToken)
            ?? throw new InvalidOperationException("所选父母不属于当前族谱。");
        var chart = chartId is Guid id ? await ResolveChartSecondParentAsync(genealogyId, parent, id, generation, cancellationToken) : ((Guid? Parent, long Revision)?)null;
        var second = chart is { } selected ? selected.Parent : await ResolveOrdinarySecondParentAsync(genealogyId, parent, cancellationToken);
        var childOrder = chartId is Guid childChart ? await ReadChartChildOrderAsync(genealogyId, parent, childChart, generation, cancellationToken) : null;
        var spouseOrder = chartId is Guid spouseChart ? await ReadChartSpouseOrderAsync(genealogyId, parent.Id, spouseChart, generation, cancellationToken) : null;
        if (chart is not null && (chart.Value.Revision != childOrder!.Revision || chart.Value.Revision != spouseOrder!.Revision))
            throw new InvalidOperationException("图表已变化，请重新开始新增子女。");
        if (childOrder?.Items.Count >= 2000) throw new InvalidOperationException("子女组已达允许上限。");
        var recorded = RecordedGenerationParser.TryParse(parent.RecordedGeneration, out var number) && number < 1000
            ? (number + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        await transaction.CommitAsync(cancellationToken);
        return new(genealogyId, Guid.NewGuid(), generation, new("", PersonSex.Male, null, null, null, recorded),
            IsNew: true, ParentId: parentId, ParentKind: PersonRelationshipKind.BiologicalParent, SecondParentId: second, IsOrdinaryChild: true, ParentSex: parent.Sex,
            ChartId: chartId, ChartRevision: chart?.Revision, ChartChildGroupId: childOrder?.GroupId, ChartChildOrder: childOrder?.Items,
            ChartSpouseGroupId: spouseOrder?.GroupId, ChartSpouseOrder: spouseOrder?.Items);
    }

    private async Task<(Guid? Parent, long Revision)> ResolveChartSecondParentAsync(Guid genealogyId, Person parent, Guid chartId, long generation, CancellationToken cancellationToken)
    {
        if (parent.Sex is not (PersonSex.Male or PersonSex.Female)) throw new InvalidOperationException("当前人物性别未明确，请先完善资料。");
        var revision = await context.LineageCharts.AsNoTracking().Where(c => c.Id == chartId && c.GenealogyId == genealogyId)
            .Select(c => (long?)c.Revision).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("图表不属于当前族谱。");
        var unions = await context.FamilyUnions.AsNoTracking().Where(u => u.GenealogyId == genealogyId && (u.PartnerOneId == parent.Id || u.PartnerTwoId == parent.Id)).ToListAsync(cancellationToken);
        if (unions.Count > 2000) throw new InvalidOperationException("配偶记录超出允许范围。");
        var marriages = unions.Where(u => u.Kind == FamilyUnionKind.Marriage).ToArray();
        if (marriages.Any(u => u.PartnerTwoId is null)) throw new InvalidOperationException("存在对方未详的婚配，无法安全确定默认生父母。");
        var group = await context.LineageSpouseOrders.AsNoTracking().Include(g => g.Items)
            .SingleOrDefaultAsync(g => g.ChartId == chartId && g.OwnerId == parent.Id, cancellationToken);
        FamilyUnion? first;
        if (group is null)
        {
            if (marriages.Length > 1) throw new InvalidOperationException("当前图表尚未保存配偶顺序，请先确认排序。");
            first = marriages.SingleOrDefault();
        }
        else
        {
            var items = group.Items.OrderBy(i => i.Position).ToArray();
            var actual = unions.ToDictionary(u => u.Id);
            if (group.EvidenceGeneration != generation || items.Length != unions.Count ||
                !items.Select(i => i.Position).SequenceEqual(Enumerable.Range(1, items.Length)) ||
                items.Any(i => !actual.TryGetValue(i.UnionId, out var u) || u.PartnerOneId != i.PartnerOneId || u.PartnerTwoId != i.PartnerTwoId || u.Kind != i.Kind))
                throw new InvalidOperationException("当前图表配偶顺序已失效，请先复核。");
            first = items.Select(i => actual[i.UnionId]).FirstOrDefault(u => u.Kind == FamilyUnionKind.Marriage);
        }
        if (first is null) return (null, revision);
        var other = first.PartnerOneId == parent.Id ? first.PartnerTwoId : first.PartnerOneId;
        var spouse = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == other && p.GenealogyId == genealogyId, cancellationToken)
            ?? throw new InvalidOperationException("婚配对方不存在或不属于当前族谱。");
        if (spouse.Id == parent.Id || spouse.Sex is not (PersonSex.Male or PersonSex.Female) || spouse.Sex == parent.Sex)
            throw new InvalidOperationException("默认配偶的生父母角色无法确定，请先核实性别与婚配资料。");
        return (spouse.Id, revision);
    }

    private async Task<Guid?> ResolveOrdinarySecondParentAsync(Guid genealogyId, Person parent, CancellationToken cancellationToken)
    {
        if (parent.Sex is not (PersonSex.Male or PersonSex.Female))
            throw new InvalidOperationException("当前人物性别未明确，无法确定生父母角色。请先完善资料。");
        var parentId = parent.Id;
        var unions = await context.FamilyUnions.AsNoTracking().Where(u => u.GenealogyId == genealogyId && u.Kind == FamilyUnionKind.Marriage &&
            (u.PartnerOneId == parentId || u.PartnerTwoId == parentId)).ToListAsync(cancellationToken);
        // The view uses the same ordering; this is not a claim about historical marriage order.
        var ordered = unions.OrderBy(u => u.Id.ToString("D"), StringComparer.Ordinal).ToArray();
        if (ordered.Any(u => u.PartnerTwoId is null))
            throw new InvalidOperationException("存在对方未详的婚配，无法安全确定默认生父母，请先完善婚配资料。");
        Guid? second = null;
        if (ordered.Length > 0)
        {
            var union = ordered[0];
            second = union.PartnerOneId == parentId ? union.PartnerTwoId : union.PartnerOneId;
            var spouse = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == second, cancellationToken)
                ?? throw new InvalidOperationException("婚配对方不属于当前族谱或已不存在。");
            if (spouse.Id == parent.Id || spouse.Sex is not (PersonSex.Male or PersonSex.Female) || spouse.Sex == parent.Sex)
                throw new InvalidOperationException("默认配偶的生父母角色无法确定，请先核实性别与婚配资料。");
        }
        return second;
    }
    public async Task<WorkbenchProfileSnapshot> ReadAsync(Guid genealogyId, Guid personId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        var person = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == personId, cancellationToken)
            ?? throw new InvalidOperationException("当前族谱中不存在该人物。");
        var parents = await (from relationship in context.PersonRelationships.AsNoTracking()
            join related in context.People.AsNoTracking() on relationship.RelatedPersonId equals (Guid?)related.Id
            where relationship.GenealogyId == genealogyId && relationship.PersonId == personId && related.GenealogyId == genealogyId &&
                (relationship.Kind == PersonRelationshipKind.BiologicalParent || relationship.Kind == PersonRelationshipKind.AdoptiveParent || relationship.Kind == PersonRelationshipKind.HeirParent)
            orderby relationship.Id
            select new WorkbenchParentInfo(related.Id, related.Name, related.Sex, relationship.Kind)).ToListAsync(cancellationToken);
        var records = await context.FamilyUnions.AsNoTracking().Where(u => u.GenealogyId == genealogyId && (u.PartnerOneId == personId || u.PartnerTwoId == personId)).ToListAsync(cancellationToken);
        var unions = new List<WorkbenchUnionInfo>();
        foreach (var union in records.OrderBy(u => u.Kind == FamilyUnionKind.Marriage && u.PartnerTwoId is not null ? 0 : 1).ThenBy(u => u.Id.ToString("D"), StringComparer.Ordinal))
        {
            var other = union.PartnerOneId == personId ? union.PartnerTwoId : union.PartnerOneId;
            var partner = other is Guid id ? await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.GenealogyId == genealogyId, cancellationToken) : null;
            if (other is not null && partner is null) throw new InvalidOperationException("婚配对方不存在或不属于当前族谱。");
            unions.Add(new(union.Id, other, partner?.Name ?? "未详", new(union.Kind, union.StartDateText, union.EndDateText, union.Note), partner?.Sex ?? PersonSex.Unknown));
        }
        var children = await ReadChildrenForCorrectionAsync(genealogyId, person, cancellationToken);
        var snapshot = new WorkbenchProfileSnapshot(genealogyId, personId, generation,
            new(person.Name, person.Sex, person.BirthDateText, person.DeathDateText, person.Biography, person.RecordedGeneration), Parents: parents.AsReadOnly(), Unions: unions.AsReadOnly(), Children: children);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    private async Task<IReadOnlyList<WorkbenchChildInfo>> ReadChildrenForCorrectionAsync(Guid genealogyId, Person parent, CancellationToken cancellationToken)
    {
        var childIds = await context.PersonRelationships.AsNoTracking().Where(r => r.GenealogyId == genealogyId && r.RelatedPersonId == parent.Id && r.Kind == PersonRelationshipKind.BiologicalParent)
            .Select(r => r.PersonId).Distinct().ToListAsync(cancellationToken);
        var result = new List<WorkbenchChildInfo>();
        foreach (var childId in childIds)
        {
            var child = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == childId, cancellationToken)
                ?? throw new InvalidOperationException("子女不属于当前族谱或已不存在。");
            var relationships = await context.PersonRelationships.AsNoTracking().Where(r => r.GenealogyId == genealogyId && r.PersonId == childId && r.Kind == PersonRelationshipKind.BiologicalParent).ToListAsync(cancellationToken);
            var others = new List<(PersonRelationship Link, Person Person)>(); string? reason = null;
            if (parent.Sex is not (PersonSex.Male or PersonSex.Female)) reason = "当前父母性别未明确，不能判断角色。";
            foreach (var link in relationships.Where(r => r.RelatedPersonId != parent.Id))
            {
                var related = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == link.RelatedPersonId, cancellationToken);
                if (related is null || related.Sex is not (PersonSex.Male or PersonSex.Female) || related.Sex == parent.Sex) { reason = "生父母角色不明确或存在同角色候选，请先核实。"; continue; }
                others.Add((link, related));
            }
            if (others.Count > 1) reason = "另一位生父母存在多个候选，请先核实。";
            var other = others.Count == 1 ? others[0] : default;
            if (other.Link is not null && (!string.IsNullOrEmpty(other.Link.Note) || !string.IsNullOrEmpty(other.Link.SourceCitation) || other.Link.IsPreferredForLineage))
                reason = "现有生父母关系带有备注、来源或主线标记，需先在关系视图核实。";
            result.Add(new(childId, child.Name, other.Person?.Id, other.Person?.Name ?? "未确定", reason));
        }
        return result.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.PersonId).ToArray();
    }

    public Task<WorkbenchProfileSnapshot> BeginNewAsync(Guid genealogyId, Guid? parentId = null,
        PersonRelationshipKind? parentKind = null, Guid? partnerId = null, FamilyUnionKind? unionKind = null, Guid? siblingId = null, CancellationToken cancellationToken = default) =>
        BeginNewCoreAsync(genealogyId, parentId, parentKind, partnerId, unionKind, siblingId, null, cancellationToken);

    public Task<WorkbenchProfileSnapshot> BeginNewSpouseForChartAsync(Guid genealogyId, Guid partnerId, Guid chartId, CancellationToken cancellationToken = default) =>
        BeginNewCoreAsync(genealogyId, null, null, partnerId, FamilyUnionKind.Marriage, null, chartId, cancellationToken);

    private async Task<WorkbenchProfileSnapshot> BeginNewCoreAsync(Guid genealogyId, Guid? parentId,
        PersonRelationshipKind? parentKind, Guid? partnerId, FamilyUnionKind? unionKind, Guid? siblingId, Guid? chartId, CancellationToken cancellationToken)
    {
        if ((parentId is null) != (parentKind is null) || (parentKind is not null && parentKind is not (PersonRelationshipKind.BiologicalParent or PersonRelationshipKind.AdoptiveParent or PersonRelationshipKind.HeirParent)))
            throw new InvalidOperationException("父母关系类型无效。");
        if ((partnerId is null) != (unionKind is null) || (unionKind is not null && !Enum.IsDefined(unionKind.Value)) || (partnerId is not null && (parentId is not null || siblingId is not null)))
            throw new InvalidOperationException("婚配关系类型无效。");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        if (!await context.Genealogies.AsNoTracking().AnyAsync(g => g.Id == genealogyId, cancellationToken))
            throw new InvalidOperationException("目标族谱不存在。");
        string? recorded = null;
        if (parentId is Guid id)
        {
            var parent = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("所选父母不属于当前族谱。");
            if (RecordedGenerationParser.TryParse(parent.RecordedGeneration, out var number) && number < 1000)
                recorded = (number + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var defaultSex = parentId is null && partnerId is null && siblingId is null ? PersonSex.Male : PersonSex.Unknown;
        if (partnerId is Guid partner)
        {
            var existingPartner = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == partner, cancellationToken)
                ?? throw new InvalidOperationException("所选配偶不属于当前族谱。");
            defaultSex = existingPartner.Sex switch { PersonSex.Male => PersonSex.Female, PersonSex.Female => PersonSex.Male, _ => PersonSex.Unknown };
        }
        if (siblingId is Guid sibling)
        {
            var existing = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == sibling, cancellationToken)
                ?? throw new InvalidOperationException("所选兄妹不属于当前族谱。");
            if (parentId is Guid sharedParent && !await context.PersonRelationships.AsNoTracking().AnyAsync(r => r.GenealogyId == genealogyId && r.PersonId == sibling && r.RelatedPersonId == sharedParent &&
                (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent), cancellationToken))
                throw new InvalidOperationException("所选共同父母关系已变化。");
            recorded = RecordedGenerationParser.TryParse(existing.RecordedGeneration, out var siblingGeneration) ? siblingGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        }
        var chartOrder = chartId is Guid selectedChart && partnerId is Guid selectedPartner
            ? await ReadChartSpouseOrderAsync(genealogyId, selectedPartner, selectedChart, generation, cancellationToken) : null;
        if (chartId is not null && defaultSex is not (PersonSex.Male or PersonSex.Female)) throw new InvalidOperationException("请先更正人物性别，再新增配偶。");
        if (chartOrder?.Items.Count >= 2000) throw new InvalidOperationException("配偶记录已达允许上限。");
        await transaction.CommitAsync(cancellationToken);
        return new(genealogyId, Guid.NewGuid(), generation, new("", defaultSex, null, null, null, recorded), true, parentId, parentKind, partnerId, unionKind, siblingId,
            ChartId: chartId, ChartRevision: chartOrder?.Revision, IsChartSpouse: chartId is not null,
            ChartSpouseGroupId: chartOrder?.GroupId, ChartSpouseOrder: chartOrder?.Items);
    }

    private sealed record ChartSpouseSequence(long Revision, Guid? GroupId, IReadOnlyList<SpouseOrderEvidence> Items);
    private sealed record ChartChildSequence(long Revision, Guid? GroupId, IReadOnlyList<ChartChildOrderMember> Items);

    private async Task<ChartChildSequence> ReadChartChildOrderAsync(Guid genealogy, Person parent, Guid chartId, long generation, CancellationToken cancellationToken)
    {
        var chart = await context.LineageCharts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == chartId && c.GenealogyId == genealogy, cancellationToken)
            ?? throw new InvalidOperationException("图表不属于当前族谱。");
        if (chart.Generation != generation) throw new InvalidOperationException("图表主线已失效，请先复核。");
        if (!RecordedGenerationParser.TryParse(parent.RecordedGeneration, out var parentGeneration)) throw new InvalidOperationException("父母世代未识别。");
        var links = await context.PersonRelationships.AsNoTracking().Where(r => r.GenealogyId == genealogy && r.RelatedPersonId == parent.Id &&
            (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent))
            .Take(2001).ToListAsync(cancellationToken);
        if (links.Count > 2000 || links.Select(r => r.PersonId).Distinct().Count() != links.Count) throw new InvalidOperationException("子女组超限或关系有歧义。");
        var members = new List<ChartChildOrderMember>();
        foreach (var link in links)
        {
            var candidates = await context.PersonRelationships.AsNoTracking().Where(r => r.GenealogyId == genealogy && r.PersonId == link.PersonId &&
                (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent)).ToListAsync(cancellationToken);
            if (candidates.Count > 1)
            {
                var choice = await context.LineageChartChoices.AsNoTracking().SingleOrDefaultAsync(c => c.ChartId == chartId && c.PersonId == link.PersonId, cancellationToken)
                    ?? throw new InvalidOperationException("子女存在多个父母候选，请先确认图表主线。");
                var selected = candidates.SingleOrDefault(r => r.Id == choice.RelationshipId && r.RelatedPersonId == choice.ParentId && r.Kind == choice.Kind)
                    ?? throw new InvalidOperationException("图表主线关系已失效，请先复核。");
                if (selected.Id != link.Id) continue;
            }
            var person = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == link.PersonId && p.GenealogyId == genealogy, cancellationToken)
                ?? throw new InvalidOperationException("子女不存在或不属于当前族谱。");
            if (person.Sex is not (PersonSex.Male or PersonSex.Female)) throw new InvalidOperationException("请先更正子女性别，再参与排序。");
            if (!RecordedGenerationParser.TryParse(person.RecordedGeneration, out var childGeneration) || childGeneration <= parentGeneration)
                throw new InvalidOperationException("子女世代未识别或与父母冲突。");
            members.Add(new(person.Id, person.Sex, link.Id, link.Kind, person.Name, person.RecordedGeneration, person.RecordedBirthOrder));
        }
        var group = await context.LineageSiblingOrders.AsNoTracking().Include(g => g.Items)
            .SingleOrDefaultAsync(g => g.ChartId == chartId && g.ParentId == parent.Id, cancellationToken);
        if (group is null)
        {
            if (members.Count > 1) throw new InvalidOperationException("当前图表尚未保存子女顺序，请先确认排序。");
            return new(chart.Revision, null, members.AsReadOnly());
        }
        var byId = members.ToDictionary(m => m.PersonId); var items = group.Items.OrderBy(i => i.Position).ToArray();
        if (group.EvidenceGeneration != generation || group.ParentGeneration != parent.RecordedGeneration || items.Length != members.Count ||
            !items.Select(i => i.Position).SequenceEqual(Enumerable.Range(1, items.Length)) ||
            items.Any(i => !byId.TryGetValue(i.PersonId, out var member) || member.RelationshipId != i.RelationshipId || member.Kind != i.Kind ||
                member.RecordedGeneration != i.RecordedGeneration || member.RecordedBirthOrder != i.RecordedBirthOrder))
            throw new InvalidOperationException("当前图表子女顺序已失效，请先复核。");
        var ordered = items.Select(i => byId[i.PersonId]).ToArray();
        WorkbenchOrderRules.Validate(ordered.Select(m => new WorkbenchOrderRules.Member(m.PersonId, m.Sex)).ToArray(), true);
        return new(chart.Revision, group.Id, Array.AsReadOnly(ordered));
    }

    private async Task<ChartSpouseSequence> ReadChartSpouseOrderAsync(Guid genealogy, Guid owner, Guid chart, long generation, CancellationToken cancellationToken)
    {
        var revision = await context.LineageCharts.AsNoTracking().Where(c => c.Id == chart && c.GenealogyId == genealogy)
            .Select(c => (long?)c.Revision).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("图表不属于当前族谱。");
        var unions = await context.FamilyUnions.AsNoTracking().Where(u => u.GenealogyId == genealogy && (u.PartnerOneId == owner || u.PartnerTwoId == owner))
            .Select(u => new SpouseOrderEvidence(u.Id, u.PartnerOneId, u.PartnerTwoId, u.Kind)).ToListAsync(cancellationToken);
        if (unions.Count > 2000) throw new InvalidOperationException("配偶记录超出允许范围。");
        var group = await context.LineageSpouseOrders.AsNoTracking().Include(g => g.Items)
            .SingleOrDefaultAsync(g => g.ChartId == chart && g.OwnerId == owner, cancellationToken);
        if (group is null)
        {
            if (unions.Count > 1) throw new InvalidOperationException("当前图表尚未保存配偶顺序，请先确认排序。");
            return new(revision, null, unions.AsReadOnly());
        }
        var items = group.Items.OrderBy(i => i.Position).ToArray();
        var ordered = items.Select(i => new SpouseOrderEvidence(i.UnionId, i.PartnerOneId, i.PartnerTwoId, i.Kind)).ToArray();
        if (group.EvidenceGeneration != generation || !items.Select(i => i.Position).SequenceEqual(Enumerable.Range(1, items.Length)) ||
            !ordered.OrderBy(i => i.UnionId).SequenceEqual(unions.OrderBy(i => i.UnionId)))
            throw new InvalidOperationException("当前图表配偶顺序已失效，请先复核。");
        return new(revision, group.Id, Array.AsReadOnly(ordered));
    }

    public async Task SaveAsync(WorkbenchProfileSnapshot original, WorkbenchProfileValues values, CancellationToken cancellationToken = default, IReadOnlyDictionary<Guid, WorkbenchUnionValues>? unionChanges = null, IReadOnlyDictionary<Guid, Guid?>? childParentChanges = null)
    {
        var name = PersonProfileRules.Validate(values.Name, values.Sex, values.BirthDate, values.DeathDate, values.Biography);
        if (values.RecordedGeneration?.Length > 100) throw new InvalidOperationException("谱载世代不能超过 100 个字符。");
        if (((original.IsNew && original.PartnerId is null) || values.RecordedGeneration != original.Values.RecordedGeneration) &&
            !RecordedGenerationParser.TryParse(values.RecordedGeneration, out _))
            throw new InvalidOperationException("请选择 1 至 1000 的世代。");
        if (original.IsNew)
        {
            if (unionChanges?.Count > 0 || childParentChanges?.Count > 0) throw new InvalidOperationException("新人物尚无可修改的既有关系。");
            if ((original.ChartId is null) != (original.ChartRevision is null) || (!original.IsOrdinaryChild && !original.IsChartSpouse && !original.IsChartAdoptedChild && !original.IsChartSibling && !original.IsChartIndependent && original.ChartId is not null) ||
                (original.IsChartSpouse && (original.ChartId is null || original.PartnerId is null || original.ChartSpouseOrder is null || original.IsOrdinaryChild)))
                throw new InvalidOperationException("图表上下文无效。");
            if (original.IsChartIndependent && (original.ChartId is null || original.ChartRevision is null || original.ParentId is not null || original.PartnerId is not null || original.SiblingId is not null || values.Sex is not (PersonSex.Male or PersonSex.Female)))
                throw new InvalidOperationException("图表独立人物上下文无效或性别未明确。");
            if (original.IsChartSibling && (!original.IsOrdinarySibling || original.ChartId is null ||
                (original.ParentId is null ? original.ChartRootSiblings is null : original.ChartChildOrder is null || original.ChartSpouseOrder is null) ||
                values.Sex is not (PersonSex.Male or PersonSex.Female)))
                throw new InvalidOperationException("图表兄妹的顺序上下文无效或性别未明确。");
            if (original.IsChartAdoptedChild && (original.ChartId is null || original.ParentId is null || original.ParentKind != PersonRelationshipKind.AdoptiveParent ||
                original.IsOrdinaryChild || original.IsOrdinarySibling || original.PartnerId is not null || original.SecondParentId is not null || original.SiblingId is not null ||
                original.ChartChildOrder is null || original.ChartSpouseOrder is null || values.Sex is not (PersonSex.Male or PersonSex.Female)))
                throw new InvalidOperationException("图表收养子女的顺序上下文无效或性别未明确。");
            if (original.IsChartSpouse && values.Sex is not (PersonSex.Male or PersonSex.Female))
                throw new InvalidOperationException("请先确定新配偶性别。");
            if (original.IsOrdinaryChild && original.ChartId is not null && (original.ChartChildOrder is null || original.ChartSpouseOrder is null || values.Sex is not (PersonSex.Male or PersonSex.Female)))
                throw new InvalidOperationException("图表子女顺序上下文无效或性别未明确。");
            if (original.IsOrdinaryChild && (original.ParentId is null || original.ParentKind != PersonRelationshipKind.BiologicalParent ||
                original.SiblingId is not null || original.PartnerId is not null || original.SecondParentId == original.ParentId))
                throw new InvalidOperationException("普通子女草稿的父母上下文无效。");
            if (original.IsOrdinarySibling && (original.SiblingId is null || original.PartnerId is not null || original.IsOrdinaryChild ||
                (original.ParentId is null && original.SecondParentId is not null) || (original.ParentId is not null && original.ParentKind != PersonRelationshipKind.BiologicalParent) ||
                (original.ParentId is not null && original.ParentId == original.SecondParentId)))
                throw new InvalidOperationException("普通兄妹草稿的父母上下文无效。");
            if (!original.IsOrdinaryChild && !original.IsOrdinarySibling && original.SecondParentId is not null)
                throw new InvalidOperationException("第二位父母只允许用于普通子女草稿。");
            if ((original.ParentId is null) != (original.ParentKind is null) || (original.ParentKind is not null && original.ParentKind is not (PersonRelationshipKind.BiologicalParent or PersonRelationshipKind.AdoptiveParent or PersonRelationshipKind.HeirParent)))
                throw new InvalidOperationException("父母关系类型无效。");
            if ((original.PartnerId is null) != (original.UnionKind is null) || (original.UnionKind is not null && !Enum.IsDefined(original.UnionKind.Value)) || (original.PartnerId is not null && (original.ParentId is not null || original.SiblingId is not null)))
                throw new InvalidOperationException("婚配关系类型无效。");
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            if (original.IsChartIndependent && !await context.LineageCharts.AsNoTracking().AnyAsync(c => c.Id == original.ChartId && c.GenealogyId == original.GenealogyId && c.Revision == original.ChartRevision && c.Generation == original.Generation, cancellationToken))
                throw new InvalidOperationException("图表已变化，请重新开始新增人物。");
            if (original.IsChartSpouse)
            {
                var current = await ReadChartSpouseOrderAsync(original.GenealogyId, original.PartnerId!.Value, original.ChartId!.Value, original.Generation, cancellationToken);
                if (current.Revision != original.ChartRevision || current.GroupId != original.ChartSpouseGroupId || !current.Items.SequenceEqual(original.ChartSpouseOrder!))
                    throw new InvalidOperationException("图表或配偶顺序已变化，请重新开始新增配偶。");
            }
            if (original.IsOrdinarySibling)
            {
                var parents = await ReadOrdinarySiblingParentsAsync(original.GenealogyId, original.SiblingId!.Value, cancellationToken);
                if (!parents.Select(p => p.PersonId).SequenceEqual((original.Parents ?? []).Select(p => p.PersonId)) ||
                    !parents.Select(p => p.PersonId).ToHashSet().SetEquals(new Guid?[] { original.ParentId, original.SecondParentId }.Where(p => p is not null).Select(p => p!.Value)))
                    throw new InvalidOperationException("生父母候选已变化，请重新开始新增兄妹。");
            }
            if (original.IsChartSibling && original.ParentId is null)
            {
                var saved = original.ChartRootSiblings!;
                var current = await new RootSiblingOrderService(context).ReadAsync(original.GenealogyId, original.ChartId!.Value, original.SiblingId!.Value, cancellationToken);
                if (saved.GenealogyId != original.GenealogyId || saved.ChartId != original.ChartId || saved.AnchorId != original.SiblingId ||
                    saved.Revision != original.ChartRevision || current.Revision != saved.Revision || current.Generation != original.Generation ||
                    current.GroupId != saved.GroupId || current.HasSavedOrder != saved.HasSavedOrder || !current.Members.SequenceEqual(saved.Members))
                    throw new InvalidOperationException("图表或兄妹顺序已变化，请重新开始新增兄妹。");
            }
            if (original.IsChartSibling && original.ParentId is not null)
            {
                var parent = await context.People.AsNoTracking().SingleAsync(p => p.GenealogyId == original.GenealogyId && p.Id == original.ParentId, cancellationToken);
                if (!RecordedGenerationParser.TryParse(parent.RecordedGeneration, out var parentGeneration) || !RecordedGenerationParser.TryParse(values.RecordedGeneration, out var siblingGeneration) || siblingGeneration <= parentGeneration)
                    throw new InvalidOperationException("兄妹世代须晚于所选父母，请先核实。");
                var children = await ReadChartChildOrderAsync(original.GenealogyId, parent, original.ChartId!.Value, original.Generation, cancellationToken);
                var spouses = await ReadChartSpouseOrderAsync(original.GenealogyId, parent.Id, original.ChartId.Value, original.Generation, cancellationToken);
                if (children.Revision != original.ChartRevision || children.GroupId != original.ChartChildGroupId || !children.Items.SequenceEqual(original.ChartChildOrder!) ||
                    spouses.Revision != original.ChartRevision || spouses.GroupId != original.ChartSpouseGroupId || !spouses.Items.SequenceEqual(original.ChartSpouseOrder!))
                    throw new InvalidOperationException("图表或排序已变化，请重新开始新增兄妹。");
            }
            if (original.IsChartAdoptedChild)
            {
                var parent = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == original.GenealogyId && p.Id == original.ParentId, cancellationToken)
                    ?? throw new InvalidOperationException("所选父母不属于当前族谱。");
                if (!RecordedGenerationParser.TryParse(parent.RecordedGeneration, out var parentGeneration) ||
                    !RecordedGenerationParser.TryParse(values.RecordedGeneration, out var childGeneration) || childGeneration <= parentGeneration)
                    throw new InvalidOperationException("子女世代须晚于所选父母，请先核实。");
                var children = await ReadChartChildOrderAsync(original.GenealogyId, parent, original.ChartId!.Value, original.Generation, cancellationToken);
                var spouses = await ReadChartSpouseOrderAsync(original.GenealogyId, parent.Id, original.ChartId.Value, original.Generation, cancellationToken);
                if (children.Revision != original.ChartRevision || children.GroupId != original.ChartChildGroupId || !children.Items.SequenceEqual(original.ChartChildOrder!) ||
                    spouses.Revision != original.ChartRevision || spouses.GroupId != original.ChartSpouseGroupId || !spouses.Items.SequenceEqual(original.ChartSpouseOrder!))
                    throw new InvalidOperationException("图表或排序已变化，请重新开始新增收养子女。");
            }
            if (original.IsOrdinaryChild)
            {
                var parent = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == original.GenealogyId && p.Id == original.ParentId, cancellationToken)
                    ?? throw new InvalidOperationException("所选父母不属于当前族谱。");
                if (parent.Sex != original.ParentSex) throw new InvalidOperationException("当前父母角色已变化，请重新开始新增子女。");
                if (original.ChartId is Guid chartId)
                {
                    if (!RecordedGenerationParser.TryParse(parent.RecordedGeneration, out var parentGeneration) ||
                        !RecordedGenerationParser.TryParse(values.RecordedGeneration, out var childGeneration) || childGeneration <= parentGeneration)
                        throw new InvalidOperationException("子女世代须晚于所选父母，请先核实。");
                    var current = await ResolveChartSecondParentAsync(original.GenealogyId, parent, chartId, original.Generation, cancellationToken);
                    if (current.Revision != original.ChartRevision || current.Parent != original.SecondParentId)
                        throw new InvalidOperationException("图表或婚配候选已变化，请重新开始新增子女。");
                    var childOrder = await ReadChartChildOrderAsync(original.GenealogyId, parent, chartId, original.Generation, cancellationToken);
                    var spouseOrder = await ReadChartSpouseOrderAsync(original.GenealogyId, parent.Id, chartId, original.Generation, cancellationToken);
                    if (childOrder.Revision != original.ChartRevision || childOrder.GroupId != original.ChartChildGroupId ||
                        !childOrder.Items.SequenceEqual(original.ChartChildOrder!) || spouseOrder.Revision != original.ChartRevision ||
                        spouseOrder.GroupId != original.ChartSpouseGroupId || !spouseOrder.Items.SequenceEqual(original.ChartSpouseOrder!))
                        throw new InvalidOperationException("图表或排序已变化，请重新开始新增子女。");
                }
                else if (await ResolveOrdinarySecondParentAsync(original.GenealogyId, parent, cancellationToken) != original.SecondParentId)
                    throw new InvalidOperationException("婚配候选已变化，请重新开始新增子女。");
            }
            var sql = $"INSERT INTO People (Id, GenealogyId, Name, Sex, BirthDateText, DeathDateText, Biography, IsHistoricalRecord, RecordedGeneration) " +
                $"SELECT @id, @genealogy, @name, @sex, @birth, @death, @biography, 0, @recorded WHERE EXISTS (SELECT 1 FROM Genealogies WHERE Id=@genealogy) " +
                $"AND (@parent IS NULL OR EXISTS (SELECT 1 FROM People WHERE Id=@parent AND GenealogyId=@genealogy)) " +
                $"AND (@sibling IS NULL OR EXISTS (SELECT 1 FROM People WHERE Id=@sibling AND GenealogyId=@genealogy)) " +
                $"AND (@sibling IS NULL OR @parent IS NULL OR EXISTS (SELECT 1 FROM PersonRelationships WHERE GenealogyId=@genealogy AND PersonId=@sibling AND RelatedPersonId=@parent AND Kind IN (1,2,3))) " +
                $"AND EXISTS (SELECT 1 FROM ({UnlinkGenerationV1.GuardSql}) AS guard WHERE guard.Value=@generation)";
            var parameters = new object[] { new SqliteParameter("@id", original.PersonId.ToString().ToUpperInvariant()),
                new SqliteParameter("@genealogy", original.GenealogyId.ToString().ToUpperInvariant()), new SqliteParameter("@name", name),
                new SqliteParameter("@sex", (int)values.Sex), new SqliteParameter("@birth", (object?)values.BirthDate ?? DBNull.Value),
                new SqliteParameter("@death", (object?)values.DeathDate ?? DBNull.Value), new SqliteParameter("@biography", (object?)values.Biography ?? DBNull.Value),
                new SqliteParameter("@recorded", (object?)values.RecordedGeneration ?? DBNull.Value), new SqliteParameter("@generation", original.Generation),
                new SqliteParameter("@sibling", original.SiblingId is Guid sibling ? sibling.ToString().ToUpperInvariant() : DBNull.Value),
                new SqliteParameter("@parent", (original.ParentId ?? original.PartnerId) is Guid related ? related.ToString().ToUpperInvariant() : DBNull.Value) };
            var inserted = await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
            if (inserted != 1) throw new InvalidOperationException("族谱或数据版本已变化，请重新读取后确认。");
            PersonRelationship? createdParentLink = null;
            if (original.ParentId is Guid parentId)
            {
                var relationship = new PersonRelationship { GenealogyId = original.GenealogyId, PersonId = original.PersonId,
                    RelatedPersonId = parentId, Kind = original.ParentKind!.Value };
                if (original.IsOrdinarySibling || original.IsChartAdoptedChild) await new PersonRelationshipValidator(context).ValidateForCreateAsync(relationship, cancellationToken);
                try { context.PersonRelationships.Add(relationship); await context.SaveChangesAsync(cancellationToken); }
                catch { context.Entry(relationship).State = EntityState.Detached; throw; }
                createdParentLink = relationship;
            }
            if (original.SecondParentId is Guid secondParent)
            {
                var relationship = new PersonRelationship { GenealogyId = original.GenealogyId, PersonId = original.PersonId,
                    RelatedPersonId = secondParent, Kind = PersonRelationshipKind.BiologicalParent };
                await new PersonRelationshipValidator(context).ValidateForCreateAsync(relationship, cancellationToken);
                try { context.PersonRelationships.Add(relationship); await context.SaveChangesAsync(cancellationToken); }
                catch { context.Entry(relationship).State = EntityState.Detached; throw; }
            }
            if (original.SiblingId is Guid siblingId && original.ParentId is not null)
            {
                var relationship = new PersonRelationship { GenealogyId = original.GenealogyId, PersonId = original.PersonId,
                    RelatedPersonId = siblingId, Kind = PersonRelationshipKind.Sibling };
                try { context.PersonRelationships.Add(relationship); await context.SaveChangesAsync(cancellationToken); }
                catch { context.Entry(relationship).State = EntityState.Detached; throw; }
            }
            FamilyUnion? createdUnion = null;
            if (original.PartnerId is Guid partnerId)
            {
                var union = new FamilyUnion { GenealogyId = original.GenealogyId, PartnerOneId = partnerId,
                    PartnerTwoId = original.PersonId, Kind = original.UnionKind!.Value };
                await new FamilyUnionValidator(context).ValidateForCreateAsync(union, cancellationToken);
                try { context.FamilyUnions.Add(union); await context.SaveChangesAsync(cancellationToken); }
                catch { context.Entry(union).State = EntityState.Detached; throw; }
                createdUnion = union;
            }
            if ((original.IsOrdinaryChild && original.ChartId is not null) || original.IsChartAdoptedChild || (original.IsChartSibling && original.ParentId is not null))
                await AppendChartChildAsync(original, values, name, createdParentLink ?? throw new InvalidOperationException("新子女父母关系缺失。"), cancellationToken);
            if (original.IsChartIndependent)
                await AppendIndependentPersonAsync(original, values, cancellationToken);
            if (original.IsChartSibling && original.ParentId is null)
            {
                var saved = original.ChartRootSiblings!; var chartId = original.ChartId!.Value;
                var newGeneration = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
                var updated = await context.LineageCharts.Where(c => c.Id == chartId && c.GenealogyId == original.GenealogyId && c.Revision == saved.Revision && c.Generation == saved.Generation && c.Revision < long.MaxValue)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, c => c.Revision + 1).SetProperty(c => c.Generation, newGeneration), cancellationToken);
                if (updated != 1) throw new InvalidOperationException("图表已变化，请重新开始新增兄妹。");
                var sameGeneration = RecordedGenerationParser.TryParse(values.RecordedGeneration, out var newPersonGeneration) &&
                    RecordedGenerationParser.TryParse(saved.Members[0].RecordedGeneration, out var anchorGeneration) && newPersonGeneration == anchorGeneration;
                if (!sameGeneration)
                {
                    var candidates = await context.LineageRootSiblingOrders.AsNoTracking().Include(g => g.Items).Where(g => g.ChartId == chartId).ToListAsync(cancellationToken);
                    var destination = candidates.Where(g => g.Items.Count > 0 && RecordedGenerationParser.TryParse(g.Items[0].RecordedGeneration, out var n) && n == newPersonGeneration).ToArray();
                    if (destination.Length > 1 || destination.Any(g => g.EvidenceGeneration != saved.Generation))
                        throw new InvalidOperationException("目标世代排序已变化，请重新读取。");
                    if (destination.Length == 1)
                    {
                        var items = destination[0].Items.OrderBy(i => i.Position).ToArray();
                        if (!items.Select(i => i.Position).SequenceEqual(Enumerable.Range(1, items.Length)) ||
                            items.Any(i => !RecordedGenerationParser.TryParse(i.RecordedGeneration, out var n) || n != newPersonGeneration))
                            throw new InvalidOperationException("目标世代排序已失效，请先复核。");
                        var existing = items.Select(i => new RootSiblingMember(i.PersonId, i.Sex, i.RecordedGeneration)).ToArray();
                        var addedPerson = new RootSiblingMember(original.PersonId, values.Sex, values.RecordedGeneration);
                        var byPerson = existing.Append(addedPerson).ToDictionary(m => m.Id);
                        var order = WorkbenchOrderRules.Append(existing.Select(m => new WorkbenchOrderRules.Member(m.Id, m.Sex)).ToArray(), new(addedPerson.Id, addedPerson.Sex), true);
                        await context.LineageRootSiblingOrderItems.Where(i => i.GroupId == destination[0].Id).ExecuteDeleteAsync(cancellationToken);
                        await RootSiblingOrderService.InsertItemsAsync(context, destination[0].Id, order.Select(m => byPerson[m.Id]).ToArray(), cancellationToken);
                    }
                    await RenewUnaffectedChartOrdersAsync(chartId, saved.Generation, newGeneration, null, null, null, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
                var added = new RootSiblingMember(original.PersonId, values.Sex, values.RecordedGeneration);
                var byId = saved.Members.Append(added).ToDictionary(m => m.Id);
                var sequence = WorkbenchOrderRules.Append(saved.Members.Select(m => new WorkbenchOrderRules.Member(m.Id, m.Sex)).ToArray(), new(added.Id, added.Sex), true);
                var groupId = saved.GroupId ?? Guid.NewGuid();
                if (saved.GroupId is null)
                    await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageRootSiblingOrders (Id,ChartId,EvidenceGeneration) VALUES (@id,@chart,@generation)",
                        new object[] { Sql("@id", groupId), Sql("@chart", chartId), new SqliteParameter("@generation", newGeneration) }, cancellationToken);
                else
                {
                    var renewed = await context.LineageRootSiblingOrders.Where(g => g.Id == groupId && g.ChartId == chartId && g.EvidenceGeneration == saved.Generation)
                        .ExecuteUpdateAsync(s => s.SetProperty(g => g.EvidenceGeneration, newGeneration), cancellationToken);
                    if (renewed != 1) throw new InvalidOperationException("图表兄妹顺序已变化，请重新读取。");
                    await context.Database.ExecuteSqlRawAsync("DELETE FROM LineageRootSiblingOrderItems WHERE GroupId=@id", new object[] { Sql("@id", groupId) }, cancellationToken);
                }
                await RootSiblingOrderService.InsertItemsAsync(context, groupId, sequence.Select(m => byId[m.Id]).ToArray(), cancellationToken);
                await RenewUnaffectedChartOrdersAsync(chartId, saved.Generation, newGeneration, null, null, groupId, cancellationToken);
            }
            if (original.IsChartSpouse)
            {
                var chartId = original.ChartId!.Value; var owner = original.PartnerId!.Value;
                var newGeneration = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
                var revisionChanged = await context.LineageCharts.Where(c => c.Id == chartId && c.GenealogyId == original.GenealogyId && c.Revision == original.ChartRevision && c.Revision < long.MaxValue)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, c => c.Revision + 1)
                        .SetProperty(c => c.Generation, c => c.Generation == original.Generation ? newGeneration : c.Generation), cancellationToken);
                if (revisionChanged != 1) throw new InvalidOperationException("图表已变化，请重新开始新增配偶。");
                var groupId = original.ChartSpouseGroupId ?? Guid.NewGuid();
                if (original.ChartSpouseGroupId is null)
                {
                    await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageSpouseOrders (Id,ChartId,OwnerId,EvidenceGeneration) VALUES (@id,@chart,@owner,@generation)",
                        new object[] { Sql("@id", groupId), Sql("@chart", chartId), Sql("@owner", owner), new SqliteParameter("@generation", newGeneration) }, cancellationToken);
                    var prior = original.ChartSpouseOrder!;
                    for (var i = 0; i < prior.Count; i++) await InsertSpouseOrderItemAsync(groupId, prior[i], i + 1, cancellationToken);
                }
                else
                {
                    var updated = await context.LineageSpouseOrders.Where(g => g.Id == groupId && g.ChartId == chartId && g.OwnerId == owner && g.EvidenceGeneration == original.Generation)
                        .ExecuteUpdateAsync(s => s.SetProperty(g => g.EvidenceGeneration, newGeneration), cancellationToken);
                    if (updated != 1) throw new InvalidOperationException("配偶顺序已变化，请重新读取。");
                }
                var created = createdUnion ?? throw new InvalidOperationException("新配偶婚配记录缺失。");
                await InsertSpouseOrderItemAsync(groupId, new(created.Id, created.PartnerOneId, created.PartnerTwoId, created.Kind), original.ChartSpouseOrder!.Count + 1, cancellationToken);
                if (await context.LineageCharts.AsNoTracking().AnyAsync(c => c.Id == chartId && c.Generation == newGeneration, cancellationToken))
                    await RenewUnaffectedChartOrdersAsync(chartId, original.Generation, newGeneration, null, owner, null, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        await using var updateTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
        List<LineageRootSiblingOrder>? independentGroups = null;
        var generationChanged = values.RecordedGeneration != original.Values.RecordedGeneration;
        var affectedChildGroups = new List<LineageSiblingOrder>();
        var affectedRootGroups = new List<LineageRootSiblingOrder>();
        var hasParents = false;
        if (original.ChartId is Guid editChart)
        {
            if (original.ChartRevision is not long editRevision ||
                !await context.LineageCharts.AsNoTracking().AnyAsync(c => c.Id == editChart && c.GenealogyId == original.GenealogyId && c.Revision == editRevision && c.Generation == original.Generation, cancellationToken))
                throw new InvalidOperationException("图表已变化，请重新读取后更正世代。");
            if (!generationChanged)
            {
                if (unionChanges?.Count > 0) throw new InvalidOperationException("请分开保存婚配资料和图表关系更正。");
                var choices = await context.LineageChartChoices.AsNoTracking().Where(c => c.ChartId == editChart).ToListAsync(cancellationToken);
                foreach (var choice in choices)
                    if (!await context.PersonRelationships.AsNoTracking().AnyAsync(r => r.Id == choice.RelationshipId && r.GenealogyId == original.GenealogyId && r.PersonId == choice.PersonId && r.RelatedPersonId == choice.ParentId && r.Kind == choice.Kind, cancellationToken))
                        throw new InvalidOperationException("图表主线已失效，请先复核。");
                var chartGroups = await context.LineageSiblingOrders.AsNoTracking().Where(g => g.ChartId == editChart).ToListAsync(cancellationToken);
                foreach (var group in chartGroups)
                {
                    var parent = await context.People.AsNoTracking().SingleAsync(p => p.Id == group.ParentId && p.GenealogyId == original.GenealogyId, cancellationToken);
                    var verified = await ReadChartChildOrderAsync(original.GenealogyId, parent, editChart, original.Generation, cancellationToken);
                    if (verified.GroupId != group.Id) throw new InvalidOperationException("图表同胞顺序已变化，请先复核。");
                }
                var rootGroups = await context.LineageRootSiblingOrders.AsNoTracking().Include(g => g.Items).Where(g => g.ChartId == editChart).ToListAsync(cancellationToken);
                foreach (var group in rootGroups.Where(g => g.Items.Count > 0))
                {
                    var verified = await new RootSiblingOrderService(context).ReadAsync(original.GenealogyId, editChart, group.Items[0].PersonId, cancellationToken);
                    if (verified.GroupId != group.Id || !verified.HasSavedOrder) throw new InvalidOperationException("图表同世顺序已变化，请先复核。");
                }
                var spouseGroups = await context.LineageSpouseOrders.AsNoTracking().Where(g => g.ChartId == editChart).ToListAsync(cancellationToken);
                foreach (var group in spouseGroups)
                {
                    var verified = await ReadChartSpouseOrderAsync(original.GenealogyId, group.OwnerId, editChart, original.Generation, cancellationToken);
                    if (verified.GroupId != group.Id) throw new InvalidOperationException("图表配偶顺序已变化，请先复核。");
                }
                if (values.Sex != original.Values.Sex)
                {
                    affectedChildGroups = await context.LineageSiblingOrders.AsNoTracking().Include(g => g.Items)
                        .Where(g => g.ChartId == editChart && g.Items.Any(i => i.PersonId == original.PersonId)).ToListAsync(cancellationToken);
                    foreach (var group in affectedChildGroups)
                    {
                        var parent = await context.People.AsNoTracking().SingleAsync(p => p.Id == group.ParentId && p.GenealogyId == original.GenealogyId, cancellationToken);
                        var capture = await ReadChartChildOrderAsync(original.GenealogyId, parent, editChart, original.Generation, cancellationToken);
                        if (capture.GroupId != group.Id) throw new InvalidOperationException("图表同胞顺序已变化，请先复核。");
                    }
                    affectedRootGroups = await context.LineageRootSiblingOrders.AsNoTracking().Include(g => g.Items)
                        .Where(g => g.ChartId == editChart && g.Items.Any(i => i.PersonId == original.PersonId)).ToListAsync(cancellationToken);
                    foreach (var group in affectedRootGroups)
                    {
                        var capture = await new RootSiblingOrderService(context).ReadAsync(original.GenealogyId, editChart, original.PersonId, cancellationToken);
                        if (capture.GroupId != group.Id || !capture.HasSavedOrder) throw new InvalidOperationException("图表同世顺序已变化，请先复核。");
                    }
                }
            }
            else
            {
            if (unionChanges?.Count > 0 || childParentChanges?.Count > 0 || values.Sex != original.Values.Sex || values.Name != original.Values.Name ||
                await context.PersonRelationships.AsNoTracking().AnyAsync(r => r.GenealogyId == original.GenealogyId &&
                    (r.PersonId == original.PersonId || r.RelatedPersonId == original.PersonId) &&
                    r.Kind != PersonRelationshipKind.BiologicalParent && r.Kind != PersonRelationshipKind.AdoptiveParent && r.Kind != PersonRelationshipKind.HeirParent && r.Kind != PersonRelationshipKind.Sibling, cancellationToken))
                throw new InvalidOperationException("人物的其他关系需要核实，或其他资料有变更；请分开保存世代。");
            hasParents = await context.PersonRelationships.AsNoTracking().AnyAsync(r => r.GenealogyId == original.GenealogyId && r.PersonId == original.PersonId &&
                (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent), cancellationToken);
            independentGroups = await context.LineageRootSiblingOrders.AsNoTracking().Include(g => g.Items).Where(g => g.ChartId == editChart).ToListAsync(cancellationToken);
            if (independentGroups.Any(g => g.EvidenceGeneration != original.Generation))
                throw new InvalidOperationException("图表顺序已失效，请先复核。");
            }
        }
        var generationShift = await CaptureDescendantGenerationShiftAsync(original, values, cancellationToken);
        var count = await context.People.Where(p => p.GenealogyId == original.GenealogyId && p.Id == original.PersonId)
            .Where(p => context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).Any(v => v == original.Generation))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, name).SetProperty(p => p.Sex, values.Sex)
                .SetProperty(p => p.BirthDateText, values.BirthDate).SetProperty(p => p.DeathDateText, values.DeathDate)
                .SetProperty(p => p.Biography, values.Biography).SetProperty(p => p.RecordedGeneration, values.RecordedGeneration), cancellationToken);
        if (count != 1) throw new InvalidOperationException("人物或数据版本已变化，请重新读取后确认。");
        foreach (var (personId, shift) in generationShift)
        {
            var changed = await context.People.Where(p => p.GenealogyId == original.GenealogyId && p.Id == personId && p.RecordedGeneration == shift.Before)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.RecordedGeneration, shift.After), cancellationToken);
            if (changed != 1) throw new InvalidOperationException("后代世代已变化，请重新读取后保存。");
            if (original.ChartId is Guid changedChart)
            {
                var groups = context.LineageSiblingOrders.Where(g => g.ChartId == changedChart && g.EvidenceGeneration == original.Generation).Select(g => g.Id);
                if (await context.LineageSiblingOrders.AsNoTracking().AnyAsync(g => g.ChartId == changedChart && g.ParentId == personId &&
                        (g.EvidenceGeneration != original.Generation || g.ParentGeneration != shift.Before), cancellationToken) ||
                    await context.LineageSiblingOrderItems.AsNoTracking().AnyAsync(i => groups.Contains(i.GroupId) && i.PersonId == personId && i.RecordedGeneration != shift.Before, cancellationToken))
                    throw new InvalidOperationException("后代排序证据已变化，请先复核。");
                await context.LineageSiblingOrders.Where(g => g.ChartId == changedChart && g.ParentId == personId && g.EvidenceGeneration == original.Generation && g.ParentGeneration == shift.Before)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.ParentGeneration, shift.After), cancellationToken);
                await context.LineageSiblingOrderItems.Where(i => groups.Contains(i.GroupId) && i.PersonId == personId && i.RecordedGeneration == shift.Before)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.RecordedGeneration, shift.After), cancellationToken);
            }
        }
        if (independentGroups is not null && original.ChartId is Guid editChartId)
        {
            var after = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
            var source = independentGroups.Where(g => g.Items.Any(i => i.PersonId == original.PersonId)).ToArray();
            if (source.Length > 1) throw new InvalidOperationException("人物同时属于多个排序组，请先复核。");
            if (hasParents && source.Length > 0) throw new InvalidOperationException("人物的同世排序与父母关系不一致，请先复核。");
            if (source.Length == 1)
            {
                var members = source[0].Items.OrderBy(i => i.Position).Where(i => i.PersonId != original.PersonId)
                    .Select(i => new RootSiblingMember(i.PersonId, i.Sex, i.RecordedGeneration)).ToArray();
                await context.LineageRootSiblingOrderItems.Where(i => i.GroupId == source[0].Id).ExecuteDeleteAsync(cancellationToken);
                await RootSiblingOrderService.InsertItemsAsync(context, source[0].Id, members, cancellationToken);
            }
            var destination = independentGroups.Where(g => !hasParents && (source.Length == 0 || g.Id != source[0].Id) && g.Items.Count > 0 &&
                RecordedGenerationParser.TryParse(g.Items[0].RecordedGeneration, out var oldGeneration) &&
                RecordedGenerationParser.TryParse(values.RecordedGeneration, out var newGeneration) && oldGeneration == newGeneration).ToArray();
            if (destination.Length > 1) throw new InvalidOperationException("目标世代存在多个排序组，请先复核。");
            if (destination.Length == 1)
            {
                var existing = destination[0].Items.OrderBy(i => i.Position).Select(i => new RootSiblingMember(i.PersonId, i.Sex, i.RecordedGeneration)).ToArray();
                if (!destination[0].Items.OrderBy(i => i.Position).Select(i => i.Position).SequenceEqual(Enumerable.Range(1, existing.Length)) ||
                    existing.Any(m => !RecordedGenerationParser.TryParse(m.RecordedGeneration, out var n) ||
                        !RecordedGenerationParser.TryParse(values.RecordedGeneration, out var expected) || n != expected))
                    throw new InvalidOperationException("目标世代排序已失效，请先复核。");
                var added = new RootSiblingMember(original.PersonId, values.Sex, values.RecordedGeneration);
                var byId = existing.Append(added).ToDictionary(m => m.Id);
                var order = WorkbenchOrderRules.Append(existing.Select(m => new WorkbenchOrderRules.Member(m.Id, m.Sex)).ToArray(), new(added.Id, added.Sex), true);
                await context.LineageRootSiblingOrderItems.Where(i => i.GroupId == destination[0].Id).ExecuteDeleteAsync(cancellationToken);
                await RootSiblingOrderService.InsertItemsAsync(context, destination[0].Id, order.Select(m => byId[m.Id]).ToArray(), cancellationToken);
            }
            await context.LineageSiblingOrders.Where(g => g.ChartId == editChartId && g.ParentId == original.PersonId && g.EvidenceGeneration == original.Generation && g.ParentGeneration == original.Values.RecordedGeneration)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.ParentGeneration, values.RecordedGeneration), cancellationToken);
            if (await context.LineageSiblingOrders.AsNoTracking().AnyAsync(g => g.ChartId == editChartId && g.ParentId == original.PersonId &&
                (g.EvidenceGeneration != original.Generation || g.ParentGeneration != values.RecordedGeneration), cancellationToken))
                throw new InvalidOperationException("子女排序证据已变化，请先复核。");
            var chartGroups = context.LineageSiblingOrders.Where(g => g.ChartId == editChartId && g.EvidenceGeneration == original.Generation).Select(g => g.Id);
            if (await context.LineageSiblingOrderItems.AsNoTracking().AnyAsync(i => chartGroups.Contains(i.GroupId) && i.PersonId == original.PersonId && i.RecordedGeneration != original.Values.RecordedGeneration, cancellationToken))
                throw new InvalidOperationException("人物排序证据已变化，请先复核。");
            await context.LineageSiblingOrderItems.Where(i => chartGroups.Contains(i.GroupId) && i.PersonId == original.PersonId && i.RecordedGeneration == original.Values.RecordedGeneration)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.RecordedGeneration, values.RecordedGeneration), cancellationToken);
            await RenewUnaffectedChartOrdersAsync(editChartId, original.Generation, after, null, null, null, cancellationToken);
            var updated = await context.LineageCharts.Where(c => c.Id == editChartId && c.GenealogyId == original.GenealogyId && c.Revision == original.ChartRevision && c.Generation == original.Generation && c.Revision < long.MaxValue)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, c => c.Revision + 1).SetProperty(c => c.Generation, after), cancellationToken);
            if (updated != 1) throw new InvalidOperationException("图表已变化，请重新读取后更正世代。");
        }
        foreach (var (id, changed) in unionChanges ?? new Dictionary<Guid, WorkbenchUnionValues>())
        {
            if ((original.Unions ?? []).All(u => u.Id != id)) throw new InvalidOperationException("婚配不属于当前人物草稿。");
            if (!Enum.IsDefined(changed.Kind) || changed.StartDateText?.Length > 100 || changed.EndDateText?.Length > 100 || changed.Note?.Length > 2000)
                throw new InvalidOperationException("婚配类型无效或资料超过允许长度。");
            var union = await context.FamilyUnions.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.GenealogyId == original.GenealogyId &&
                (u.PartnerOneId == original.PersonId || u.PartnerTwoId == original.PersonId), cancellationToken)
                ?? throw new InvalidOperationException("婚配已变化或不属于当前人物。");
            if (await context.FamilyUnions.AsNoTracking().AnyAsync(u => u.Id != id && u.GenealogyId == original.GenealogyId && u.PartnerOneId == union.PartnerOneId && u.PartnerTwoId == union.PartnerTwoId && u.Kind == changed.Kind, cancellationToken))
                throw new InvalidOperationException("相同婚配关系已经存在。");
            var updated = await context.FamilyUnions.Where(u => u.Id == id && u.GenealogyId == original.GenealogyId && (u.PartnerOneId == original.PersonId || u.PartnerTwoId == original.PersonId))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Kind, changed.Kind).SetProperty(u => u.StartDateText, changed.StartDateText)
                    .SetProperty(u => u.EndDateText, changed.EndDateText).SetProperty(u => u.Note, changed.Note), cancellationToken);
            if (updated != 1) throw new InvalidOperationException("婚配已变化，请重新读取。");
        }
        if (childParentChanges?.Count > 0)
        {
            if (values.Sex != original.Values.Sex || values.Sex is not (PersonSex.Male or PersonSex.Female))
                throw new InvalidOperationException("请先保存当前人物的明确性别，再更正子女父母。");
            var parent = await context.People.AsNoTracking().SingleAsync(p => p.Id == original.PersonId && p.GenealogyId == original.GenealogyId, cancellationToken);
            var currentChildren = await ReadChildrenForCorrectionAsync(original.GenealogyId, parent, cancellationToken);
            foreach (var (childId, targetId) in childParentChanges)
            {
                var before = (original.Children ?? []).SingleOrDefault(c => c.PersonId == childId) ?? throw new InvalidOperationException("子女不属于当前人物。");
                var current = currentChildren.SingleOrDefault(c => c.PersonId == childId) ?? throw new InvalidOperationException("子女关系已变化。");
                if (before.BlockReason is not null || current.BlockReason is not null || current.OtherParentId != before.OtherParentId)
                    throw new InvalidOperationException(current.BlockReason ?? "子女父母关系已变化，请重新读取。");
                if (targetId is Guid target)
                {
                    var expectedSex = parent.Sex == PersonSex.Male ? PersonSex.Female : PersonSex.Male;
                    var valid = await context.People.AsNoTracking().AnyAsync(p => p.Id == target && p.GenealogyId == original.GenealogyId && p.Sex == expectedSex, cancellationToken) &&
                        await context.FamilyUnions.AsNoTracking().AnyAsync(u => u.GenealogyId == original.GenealogyId && u.Kind == FamilyUnionKind.Marriage &&
                            ((u.PartnerOneId == parent.Id && u.PartnerTwoId == target) || (u.PartnerTwoId == parent.Id && u.PartnerOneId == target)), cancellationToken);
                    if (!valid) throw new InvalidOperationException("目标必须是当前人物同谱且角色明确的夫妻配偶。");
                }
                var old = before.OtherParentId is Guid oldId ? await context.PersonRelationships.AsNoTracking().SingleOrDefaultAsync(r => r.GenealogyId == original.GenealogyId && r.PersonId == childId && r.RelatedPersonId == oldId && r.Kind == PersonRelationshipKind.BiologicalParent, cancellationToken) : null;
                if (before.OtherParentId is not null && old is null) throw new InvalidOperationException("原生父母关系已变化。");
                if (targetId is null && old is not null)
                {
                    var deleted = await context.PersonRelationships.Where(r => r.Id == old.Id && r.GenealogyId == original.GenealogyId && r.PersonId == childId && r.RelatedPersonId == before.OtherParentId && r.Kind == PersonRelationshipKind.BiologicalParent).ExecuteDeleteAsync(cancellationToken);
                    if (deleted != 1) throw new InvalidOperationException("原生父母关系已变化。");
                }
                else if (targetId is Guid next && old is not null)
                {
                    var corrected = new PersonRelationship { Id = old.Id, GenealogyId = original.GenealogyId, PersonId = childId, RelatedPersonId = next, Kind = PersonRelationshipKind.BiologicalParent };
                    await new PersonRelationshipValidator(context).ValidateParentCorrectionAsync(corrected, cancellationToken);
                    var updated = await context.PersonRelationships.Where(r => r.Id == old.Id && r.GenealogyId == original.GenealogyId && r.PersonId == childId && r.RelatedPersonId == before.OtherParentId && r.Kind == PersonRelationshipKind.BiologicalParent).ExecuteUpdateAsync(s => s.SetProperty(r => r.RelatedPersonId, next), cancellationToken);
                    if (updated != 1) throw new InvalidOperationException("原生父母关系已变化。");
                }
                else if (targetId is Guid added)
                {
                    var relationship = new PersonRelationship { GenealogyId = original.GenealogyId, PersonId = childId, RelatedPersonId = added, Kind = PersonRelationshipKind.BiologicalParent };
                    await new PersonRelationshipValidator(context).ValidateForCreateAsync(relationship, cancellationToken);
                    try { context.PersonRelationships.Add(relationship); await context.SaveChangesAsync(cancellationToken); }
                    catch { context.Entry(relationship).State = EntityState.Detached; throw; }
                }
            }
        }
        if (!generationChanged && original.ChartId is Guid profileChart)
        {
            var choices = await context.LineageChartChoices.AsNoTracking().Where(c => c.ChartId == profileChart).ToListAsync(cancellationToken);
            foreach (var choice in choices)
                if (!await context.PersonRelationships.AsNoTracking().AnyAsync(r => r.Id == choice.RelationshipId && r.GenealogyId == original.GenealogyId && r.PersonId == choice.PersonId && r.RelatedPersonId == choice.ParentId && r.Kind == choice.Kind, cancellationToken))
                    throw new InvalidOperationException("此次更正改变了图表主线，请先复核主线；草稿已保留。");
            foreach (var group in affectedChildGroups)
            {
                var items = group.Items.OrderBy(i => i.Position).ToArray();
                var sexes = await context.People.AsNoTracking().Where(p => p.GenealogyId == original.GenealogyId && items.Select(i => i.PersonId).Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Sex, cancellationToken);
                if (sexes.Count != items.Length) throw new InvalidOperationException("排序人物已变化，请重新读取。");
                var others = items.Where(i => i.PersonId != original.PersonId).Select(i => new WorkbenchOrderRules.Member(i.PersonId, sexes[i.PersonId])).ToArray();
                var reordered = WorkbenchOrderRules.Append(others, new(original.PersonId, values.Sex), true);
                var byId = items.ToDictionary(i => i.PersonId);
                await context.LineageSiblingOrderItems.Where(i => i.GroupId == group.Id).ExecuteDeleteAsync(cancellationToken);
                for (var i = 0; i < reordered.Count; i++)
                {
                    var item = byId[reordered[i].Id];
                    await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageSiblingOrderItems (GroupId,PersonId,Position,RelationshipId,Kind,PersonName,RecordedGeneration,RecordedBirthOrder) VALUES (@group,@person,@position,@relation,@kind,@name,@generation,@birthorder)",
                        new object[] { Sql("@group", group.Id), Sql("@person", item.PersonId), new SqliteParameter("@position", i + 1), Sql("@relation", item.RelationshipId),
                            new SqliteParameter("@kind", (int)item.Kind), new SqliteParameter("@name", item.PersonId == original.PersonId ? name : item.PersonName),
                            new SqliteParameter("@generation", (object?)item.RecordedGeneration ?? DBNull.Value), new SqliteParameter("@birthorder", (object?)item.RecordedBirthOrder ?? DBNull.Value) }, cancellationToken);
                }
            }
            foreach (var group in affectedRootGroups)
            {
                var items = group.Items.OrderBy(i => i.Position).ToArray();
                var others = items.Where(i => i.PersonId != original.PersonId).Select(i => new WorkbenchOrderRules.Member(i.PersonId, i.Sex)).ToArray();
                var reordered = WorkbenchOrderRules.Append(others, new(original.PersonId, values.Sex), true);
                var byId = items.ToDictionary(i => i.PersonId, i => new RootSiblingMember(i.PersonId, i.PersonId == original.PersonId ? values.Sex : i.Sex, i.RecordedGeneration));
                await context.LineageRootSiblingOrderItems.Where(i => i.GroupId == group.Id).ExecuteDeleteAsync(cancellationToken);
                await RootSiblingOrderService.InsertItemsAsync(context, group.Id, reordered.Select(m => byId[m.Id]).ToArray(), cancellationToken);
            }
            await context.LineageChartChoices.Where(c => c.ChartId == profileChart && c.PersonId == original.PersonId).ExecuteUpdateAsync(s => s.SetProperty(c => c.PersonName, name), cancellationToken);
            await context.LineageChartChoices.Where(c => c.ChartId == profileChart && c.ParentId == original.PersonId).ExecuteUpdateAsync(s => s.SetProperty(c => c.ParentName, name), cancellationToken);
            await context.LineageSiblingOrders.Where(g => g.ChartId == profileChart && g.ParentId == original.PersonId && g.EvidenceGeneration == original.Generation)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.ParentName, name), cancellationToken);
            var profileGroups = context.LineageSiblingOrders.Where(g => g.ChartId == profileChart && g.EvidenceGeneration == original.Generation).Select(g => g.Id);
            await context.LineageSiblingOrderItems.Where(i => profileGroups.Contains(i.GroupId) && i.PersonId == original.PersonId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.PersonName, name), cancellationToken);
            var after = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
            await RenewUnaffectedChartOrdersAsync(profileChart, original.Generation, after, null, null, null, cancellationToken);
            var updated = await context.LineageCharts.Where(c => c.Id == profileChart && c.GenealogyId == original.GenealogyId && c.Revision == original.ChartRevision && c.Generation == original.Generation && c.Revision < long.MaxValue)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, c => c.Revision + 1).SetProperty(c => c.Generation, after), cancellationToken);
            if (updated != 1) throw new InvalidOperationException("图表已变化，请重新读取；草稿已保留。");
        }
        await updateTransaction.CommitAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, (string? Before, string After)>> CaptureDescendantGenerationShiftAsync(WorkbenchProfileSnapshot original, WorkbenchProfileValues values, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, (string? Before, string After)>();
        if (values.RecordedGeneration == original.Values.RecordedGeneration) return result;
        if (!RecordedGenerationParser.TryParse(original.Values.RecordedGeneration, out var previous))
        {
            var hasChildren = await context.PersonRelationships.AsNoTracking().AnyAsync(r => r.GenealogyId == original.GenealogyId && r.RelatedPersonId == original.PersonId &&
                (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent), cancellationToken);
            if (!hasChildren) return result;
            throw new InvalidOperationException("原世代不明确，无法同步后代，请先核实。");
        }
        if (!RecordedGenerationParser.TryParse(values.RecordedGeneration, out var next)) throw new InvalidOperationException("目标世代不明确，请先核实。");
        var delta = next - previous;
        if (delta == 0) return result;
        var links = await context.PersonRelationships.AsNoTracking().Where(r => r.GenealogyId == original.GenealogyId &&
            (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent))
            .Select(r => new { Child = r.PersonId, Parent = r.RelatedPersonId }).ToListAsync(cancellationToken);
        var people = await context.People.AsNoTracking().Where(p => p.GenealogyId == original.GenealogyId).Select(p => new { p.Id, p.RecordedGeneration }).ToListAsync(cancellationToken);
        var byId = people.ToDictionary(p => p.Id); var visited = new HashSet<Guid> { original.PersonId }; var queue = new Queue<Guid>(); queue.Enqueue(original.PersonId);
        while (queue.TryDequeue(out var parent))
        foreach (var link in links.Where(l => l.Parent == parent))
        {
            if (link.Child == original.PersonId) throw new InvalidOperationException("后代关系存在循环，请先核实。");
            if (!visited.Add(link.Child)) continue;
            if (!byId.TryGetValue(link.Child, out var child) || !RecordedGenerationParser.TryParse(child.RecordedGeneration, out var oldGeneration) || oldGeneration + delta is < 1 or > 1000)
                throw new InvalidOperationException("后代世代不明确或调整后超出范围，请先核实。");
            result.Add(link.Child, (child.RecordedGeneration, (oldGeneration + delta).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            queue.Enqueue(link.Child);
        }
        var internalLinks = links.Where(l => l.Parent is Guid parent && visited.Contains(parent) && visited.Contains(l.Child)).ToArray();
        var incoming = visited.ToDictionary(id => id, id => internalLinks.Count(l => l.Child == id));
        var acyclic = new Queue<Guid>(incoming.Where(p => p.Value == 0).Select(p => p.Key)); var orderedCount = 0;
        while (acyclic.TryDequeue(out var parent))
        {
            orderedCount++;
            foreach (var link in internalLinks.Where(l => l.Parent == parent)) if (--incoming[link.Child] == 0) acyclic.Enqueue(link.Child);
        }
        if (orderedCount != visited.Count) throw new InvalidOperationException("后代关系存在循环，请先核实。");
        foreach (var link in links.Where(l => visited.Contains(l.Child)))
        {
            if (link.Parent is not Guid parent || !byId.TryGetValue(parent, out var parentPerson) ||
                !RecordedGenerationParser.TryParse(parent == original.PersonId ? values.RecordedGeneration : result.TryGetValue(parent, out var shiftedParent) ? shiftedParent.After : parentPerson.RecordedGeneration, out var parentGeneration) ||
                !RecordedGenerationParser.TryParse(link.Child == original.PersonId ? values.RecordedGeneration : result[link.Child].After, out var childGeneration) || parentGeneration >= childGeneration)
                throw new InvalidOperationException("世代调整与父母关系冲突或父母世代不明确，请先核实。");
        }
        return result;
    }

    private async Task AppendIndependentPersonAsync(WorkbenchProfileSnapshot original, WorkbenchProfileValues values, CancellationToken cancellationToken)
    {
        var chart = original.ChartId!.Value;
        if (!RecordedGenerationParser.TryParse(values.RecordedGeneration, out var targetGeneration)) throw new InvalidOperationException("请先确定人物世代。");
        var after = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        var groups = await context.LineageRootSiblingOrders.AsNoTracking().Include(g => g.Items).Where(g => g.ChartId == chart).ToListAsync(cancellationToken);
        var destination = groups.Where(g => g.Items.Count > 0 && RecordedGenerationParser.TryParse(g.Items[0].RecordedGeneration, out var n) && n == targetGeneration).ToArray();
        if (destination.Length > 1 || destination.Any(g => g.EvidenceGeneration != original.Generation)) throw new InvalidOperationException("目标世代排序已失效，请先复核。");
        RootSiblingMember[] prior;
        if (destination.Length == 1)
        {
            var items = destination[0].Items.OrderBy(i => i.Position).ToArray();
            if (!items.Select(i => i.Position).SequenceEqual(Enumerable.Range(1, items.Length)) || items.Any(i => !RecordedGenerationParser.TryParse(i.RecordedGeneration, out var n) || n != targetGeneration))
                throw new InvalidOperationException("目标世代排序已失效，请先复核。");
            prior = items.Select(i => new RootSiblingMember(i.PersonId, i.Sex, i.RecordedGeneration)).ToArray();
        }
        else
        {
            var people = await context.People.AsNoTracking().Where(p => p.GenealogyId == original.GenealogyId && p.Id != original.PersonId &&
                !context.PersonRelationships.Any(r => r.GenealogyId == original.GenealogyId && r.PersonId == p.Id &&
                    (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent)))
                .Select(p => new { p.Id, p.Sex, p.RecordedGeneration }).ToListAsync(cancellationToken);
            prior = people.Where(p => RecordedGenerationParser.TryParse(p.RecordedGeneration, out var n) && n == targetGeneration).Select(p => new RootSiblingMember(p.Id, p.Sex, p.RecordedGeneration)).ToArray();
            if (prior.Length > 1) throw new InvalidOperationException("目标世代顺序尚未确认，请先设置图表顺序。");
        }
        var updated = await context.LineageCharts.Where(c => c.Id == chart && c.GenealogyId == original.GenealogyId && c.Revision == original.ChartRevision && c.Generation == original.Generation && c.Revision < long.MaxValue)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, c => c.Revision + 1).SetProperty(c => c.Generation, after), cancellationToken);
        if (updated != 1) throw new InvalidOperationException("图表已变化，请重新开始新增人物。");
        if (prior.Length > 0)
        {
            var added = new RootSiblingMember(original.PersonId, values.Sex, values.RecordedGeneration);
            var byId = prior.Append(added).ToDictionary(m => m.Id);
            var order = WorkbenchOrderRules.Append(prior.Select(m => new WorkbenchOrderRules.Member(m.Id, m.Sex)).ToArray(), new(added.Id, added.Sex), true);
            var groupId = destination.Length == 0 ? Guid.NewGuid() : destination[0].Id;
            if (destination.Length == 0)
                await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageRootSiblingOrders (Id,ChartId,EvidenceGeneration) VALUES (@id,@chart,@generation)",
                    new object[] { Sql("@id", groupId), Sql("@chart", chart), new SqliteParameter("@generation", after) }, cancellationToken);
            else
            {
                await context.LineageRootSiblingOrderItems.Where(i => i.GroupId == groupId).ExecuteDeleteAsync(cancellationToken);
                await context.LineageRootSiblingOrders.Where(g => g.Id == groupId && g.EvidenceGeneration == original.Generation)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.EvidenceGeneration, after), cancellationToken);
            }
            await RootSiblingOrderService.InsertItemsAsync(context, groupId, order.Select(m => byId[m.Id]).ToArray(), cancellationToken);
            await RenewUnaffectedChartOrdersAsync(chart, original.Generation, after, null, null, groupId, cancellationToken);
        }
        else await RenewUnaffectedChartOrdersAsync(chart, original.Generation, after, null, null, null, cancellationToken);
    }

    private async Task InsertSpouseOrderItemAsync(Guid group, SpouseOrderEvidence item, int position, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageSpouseOrderItems (GroupId,UnionId,PartnerOneId,PartnerTwoId,Kind,Position) VALUES (@group,@union,@one,@two,@kind,@position)",
            new object[] { Sql("@group", group), Sql("@union", item.UnionId), Sql("@one", item.PartnerOneId),
                new SqliteParameter("@two", item.PartnerTwoId is Guid two ? two.ToString().ToUpperInvariant() : DBNull.Value),
                new SqliteParameter("@kind", (int)item.Kind), new SqliteParameter("@position", position) }, cancellationToken);
    }

    private async Task AppendChartChildAsync(WorkbenchProfileSnapshot original, WorkbenchProfileValues values, string name,
        PersonRelationship parentLink, CancellationToken cancellationToken)
    {
        var chartId = original.ChartId!.Value; var parentId = original.ParentId!.Value;
        var parent = await context.People.AsNoTracking().SingleAsync(p => p.Id == parentId && p.GenealogyId == original.GenealogyId, cancellationToken);
        var newGeneration = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        var updatedChart = await context.LineageCharts.Where(c => c.Id == chartId && c.GenealogyId == original.GenealogyId &&
            c.Revision == original.ChartRevision && c.Generation == original.Generation && c.Revision < long.MaxValue)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, c => c.Revision + 1).SetProperty(c => c.Generation, newGeneration), cancellationToken);
        if (updatedChart != 1) throw new InvalidOperationException("图表或主线已变化，请重新开始新增子女。");
        await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageChartChoices (ChartId,PersonId,ParentId,RelationshipId,Kind,PersonName,ParentName) VALUES (@chart,@person,@parent,@relationship,@kind,@name,@parentName)",
            new object[] { Sql("@chart", chartId), Sql("@person", original.PersonId), Sql("@parent", parentId), Sql("@relationship", parentLink.Id),
                new SqliteParameter("@kind", (int)parentLink.Kind), new SqliteParameter("@name", name), new SqliteParameter("@parentName", parent.Name) }, cancellationToken);
        if (original.ChartSpouseGroupId is Guid spouseGroup)
        {
            var renewed = await context.LineageSpouseOrders.Where(g => g.Id == spouseGroup && g.ChartId == chartId && g.OwnerId == parentId && g.EvidenceGeneration == original.Generation)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.EvidenceGeneration, newGeneration), cancellationToken);
            if (renewed != 1) throw new InvalidOperationException("配偶顺序已变化，请重新读取。");
        }
        await RenewUnaffectedChartOrdersAsync(chartId, original.Generation, newGeneration, parentId, parentId, null, cancellationToken);
        var old = original.ChartChildOrder!;
        if (old.Count == 0) return;
        var added = new ChartChildOrderMember(original.PersonId, values.Sex, parentLink.Id, parentLink.Kind, name, values.RecordedGeneration, null);
        var entries = old.Append(added).ToDictionary(m => m.PersonId);
        var sequence = WorkbenchOrderRules.Append(old.Select(m => new WorkbenchOrderRules.Member(m.PersonId, m.Sex)).ToArray(),
            new WorkbenchOrderRules.Member(added.PersonId, added.Sex), true);
        var groupId = original.ChartChildGroupId ?? Guid.NewGuid();
        if (original.ChartChildGroupId is null)
        {
            await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageSiblingOrders (Id,ChartId,ParentId,ParentName,ParentGeneration,EvidenceGeneration) VALUES (@id,@chart,@parent,@name,@generationText,@generation)",
                new object[] { Sql("@id", groupId), Sql("@chart", chartId), Sql("@parent", parentId), new SqliteParameter("@name", parent.Name),
                    new SqliteParameter("@generationText", (object?)parent.RecordedGeneration ?? DBNull.Value), new SqliteParameter("@generation", newGeneration) }, cancellationToken);
        }
        else
        {
            var changed = await context.LineageSiblingOrders.Where(g => g.Id == groupId && g.ChartId == chartId && g.ParentId == parentId && g.EvidenceGeneration == original.Generation)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.EvidenceGeneration, newGeneration).SetProperty(g => g.ParentName, parent.Name), cancellationToken);
            if (changed != 1) throw new InvalidOperationException("子女顺序已变化，请重新读取。");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM LineageSiblingOrderItems WHERE GroupId=@id", new object[] { Sql("@id", groupId) }, cancellationToken);
        }
        for (var i = 0; i < sequence.Count; i++)
        {
            var member = entries[sequence[i].Id];
            await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageSiblingOrderItems (GroupId,PersonId,Position,RelationshipId,Kind,PersonName,RecordedGeneration,RecordedBirthOrder) VALUES (@group,@person,@position,@relationship,@kind,@name,@generation,@birthOrder)",
                new object[] { Sql("@group", groupId), Sql("@person", member.PersonId), new SqliteParameter("@position", i + 1), Sql("@relationship", member.RelationshipId),
                    new SqliteParameter("@kind", (int)member.Kind), new SqliteParameter("@name", member.Name), new SqliteParameter("@generation", (object?)member.RecordedGeneration ?? DBNull.Value),
                    new SqliteParameter("@birthOrder", (object?)member.RecordedBirthOrder ?? DBNull.Value) }, cancellationToken);
        }
    }
    // This transaction only adds one new person and the explicitly handled relationships.
    // Evidence already current in this chart remains valid for groups unaffected by those additions.
    private async Task RenewUnaffectedChartOrdersAsync(Guid chart, long before, long after, Guid? changedParent, Guid? changedSpouseOwner,
        Guid? changedRootGroup, CancellationToken cancellationToken)
    {
        await context.LineageSiblingOrders.Where(g => g.ChartId == chart && g.EvidenceGeneration == before && (changedParent == null || g.ParentId != changedParent))
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.EvidenceGeneration, after), cancellationToken);
        await context.LineageSpouseOrders.Where(g => g.ChartId == chart && g.EvidenceGeneration == before && (changedSpouseOwner == null || g.OwnerId != changedSpouseOwner))
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.EvidenceGeneration, after), cancellationToken);
        await context.LineageRootSiblingOrders.Where(g => g.ChartId == chart && g.EvidenceGeneration == before && (changedRootGroup == null || g.Id != changedRootGroup))
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.EvidenceGeneration, after), cancellationToken);
    }
    private static SqliteParameter Sql(string name, Guid value) => new(name, value.ToString().ToUpperInvariant());
}

/// <summary>Holds one draft; a successful write is never retried with the old token.</summary>
public sealed class WorkbenchPersonDraft(WorkbenchProfileSnapshot original)
{
    private readonly object _gate = new();
    private WorkbenchProfileValues _values = original.Values;
    private bool _saving;
    private bool _isSaved;
    private readonly Dictionary<Guid, WorkbenchUnionValues> _unionChanges = new();
    private readonly Dictionary<Guid, Guid?> _childParentChanges = new();
    public WorkbenchProfileSnapshot Original { get; private set; } = original;
    internal void AttachChart(Guid chart, long revision)
    {
        lock (_gate)
        {
            if (_saving || _isSaved || Original.IsNew) throw new InvalidOperationException("人物草稿状态不允许设置图表上下文。");
            Original = Original with { ChartId = chart, ChartRevision = revision };
        }
    }
    internal void AttachIndependentChart(Guid chart, long revision)
    {
        lock (_gate)
        {
            if (_saving || _isSaved || !Original.IsNew || Original.ParentId is not null || Original.PartnerId is not null || Original.SiblingId is not null)
                throw new InvalidOperationException("人物草稿状态不允许设置图表上下文。");
            Original = Original with { ChartId = chart, ChartRevision = revision, IsChartIndependent = true };
        }
    }
    public WorkbenchProfileValues Values
    {
        get { lock (_gate) return _values; }
        set { lock (_gate) { if (_saving || _isSaved) throw new InvalidOperationException("保存中或已保存的草稿不能修改。"); _values = value; } }
    }
    public bool IsDirty { get { lock (_gate) return !_isSaved && (Original.IsNew || _values != Original.Values || _unionChanges.Count != 0 || _childParentChanges.Count != 0); } }
    internal bool HasChildParentChanges { get { lock (_gate) return _childParentChanges.Count != 0; } }
    public Guid? ChildOtherParent(Guid id) { lock (_gate) return _childParentChanges.TryGetValue(id, out var value) ? value : (Original.Children ?? []).Single(c => c.PersonId == id).OtherParentId; }
    public void SetChildOtherParent(Guid id, Guid? parentId)
    {
        lock (_gate)
        {
            if (_saving || _isSaved) throw new InvalidOperationException("保存中或已保存的草稿不能修改。");
            var child = (Original.Children ?? []).SingleOrDefault(c => c.PersonId == id) ?? throw new InvalidOperationException("子女不属于当前人物草稿。");
            if (child.BlockReason is not null) throw new InvalidOperationException(child.BlockReason);
            if (parentId == child.OtherParentId) _childParentChanges.Remove(id); else _childParentChanges[id] = parentId;
        }
    }
    public WorkbenchUnionValues UnionValues(Guid id) { lock (_gate) return _unionChanges.GetValueOrDefault(id) ?? (Original.Unions ?? []).Single(u => u.Id == id).Values; }
    public void SetUnionValues(Guid id, WorkbenchUnionValues values)
    {
        lock (_gate)
        {
            if (_saving || _isSaved) throw new InvalidOperationException("保存中或已保存的草稿不能修改。");
            var original = (Original.Unions ?? []).SingleOrDefault(u => u.Id == id) ?? throw new InvalidOperationException("婚配不属于当前草稿。");
            if (values == original.Values) _unionChanges.Remove(id); else _unionChanges[id] = values;
        }
    }
    public bool IsSaved { get { lock (_gate) return _isSaved; } }

    public async Task SaveAsync(WorkbenchPersonProfileCommand command, CancellationToken cancellationToken = default)
    {
        WorkbenchProfileValues values;
        Dictionary<Guid, WorkbenchUnionValues> unions;
        Dictionary<Guid, Guid?> childParents;
        lock (_gate)
        {
            if (_isSaved || _saving) throw new InvalidOperationException("草稿正在保存或已保存，请重新读取人物。");
            _saving = true;
            values = _values;
            unions = new(_unionChanges);
            childParents = new(_childParentChanges);
        }
        try
        {
            await command.SaveAsync(Original, values, cancellationToken, unions, childParents);
            lock (_gate) _isSaved = true;
        }
        finally { lock (_gate) _saving = false; }
    }
}
