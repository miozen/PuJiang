using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Services;

public sealed record LineageParentOption(GenealogyRelationshipData Relationship, GenealogyPersonData Parent)
{
    public string Label => $"{KindName(Relationship.Kind)}：{Parent.Name}（人物 ID: {Parent.Id}；关系 ID: {Relationship.Id}）" +
        (Relationship.IsPreferredForLineage ? " [谱载首选标记]" : "") +
        (string.IsNullOrWhiteSpace(Relationship.Note) ? "" : $"；备注：{Relationship.Note}") +
        (string.IsNullOrWhiteSpace(Relationship.SourceCitation) ? "" : $"；来源：{Relationship.SourceCitation}");

    private static string KindName(PersonRelationshipKind kind) => kind switch
    {
        PersonRelationshipKind.BiologicalParent => "生物父母",
        PersonRelationshipKind.AdoptiveParent => "养父母",
        PersonRelationshipKind.HeirParent => "继嗣父母",
        _ => throw new InvalidOperationException("父母关系类型无效。")
    };
}

public sealed record LineagePersonProjection(GenealogyPersonData Person, IReadOnlyList<LineageParentOption> ParentCandidates,
    LineageParentOption? SelectedParent, IReadOnlyList<GenealogyRelationshipData> LostAncestry)
{
    public bool NeedsChoice => ParentCandidates.Count > 1 && SelectedParent is null;
    public string Label => $"{Person.Name}（人物 ID: {Person.Id}） — " +
        (NeedsChoice ? "父母关系待选择" : SelectedParent is null ? "未记录父母（不推断始祖）" : $"已选 {SelectedParent.Label}") +
        (LostAncestry.Count > 0 ? "；有谱载上系失考记录" : "");
}

/// <summary>单谱快照的只读、会话内主线选择；保留全部人物及原始父母关系引用。</summary>
public sealed class LineageProjectionSession
{
    private readonly IReadOnlyList<GenealogyPersonData> _people;
    private readonly Dictionary<Guid, List<LineageParentOption>> _parents;
    private readonly Dictionary<Guid, List<GenealogyRelationshipData>> _lost;
    private readonly Dictionary<Guid, Guid> _selections = new();

    public LineageProjectionSession(GenealogyGraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var people = snapshot.People.ToDictionary(p => p.Id);
        _people = snapshot.People.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Id).ToList().AsReadOnly();
        _parents = people.Keys.ToDictionary(id => id, _ => new List<LineageParentOption>());
        _lost = people.Keys.ToDictionary(id => id, _ => new List<GenealogyRelationshipData>());
        var relationIds = new HashSet<Guid>();
        foreach (var relation in snapshot.Relationships)
        {
            if (!relationIds.Add(relation.Id) || !people.ContainsKey(relation.PersonId) ||
                (relation.RelatedPersonId is Guid related && !people.ContainsKey(related)))
                throw new InvalidOperationException("关系包含重复 ID 或缺失人物，无法预览。");
            if (relation.Kind == PersonRelationshipKind.LostAncestry) { _lost[relation.PersonId].Add(relation); continue; }
            if (relation.Kind is not (PersonRelationshipKind.BiologicalParent or PersonRelationshipKind.AdoptiveParent or PersonRelationshipKind.HeirParent)) continue;
            if (relation.RelatedPersonId is not Guid parentId)
                throw new InvalidOperationException("父母关系缺少对方人物，无法预览。");
            _parents[relation.PersonId].Add(new LineageParentOption(relation, people[parentId]));
        }
        if (snapshot.Unions.Any(u => !people.ContainsKey(u.PartnerOneId) || (u.PartnerTwoId is Guid id && !people.ContainsKey(id))))
            throw new InvalidOperationException("婚配引用缺失人物，无法预览。");
        foreach (var candidates in _parents.Values) candidates.Sort((a, b) => a.Relationship.Id.CompareTo(b.Relationship.Id));
        ValidateAcyclic(people.Keys);
    }

    public IReadOnlyList<LineagePersonProjection> Project() => _people.Select(person =>
    {
        var candidates = _parents[person.Id];
        LineageParentOption? selected = candidates.Count == 1 ? candidates[0] :
            _selections.TryGetValue(person.Id, out var id) ? candidates.Single(c => c.Relationship.Id == id) : null;
        return new LineagePersonProjection(person, candidates.AsReadOnly(), selected, _lost[person.Id].AsReadOnly());
    }).ToList().AsReadOnly();

    public void Choose(Guid personId, Guid? relationshipId)
    {
        if (!_parents.TryGetValue(personId, out var candidates) || candidates.Count < 2)
            throw new InvalidOperationException("当前人物没有多条可选父母关系。");
        if (relationshipId is null) { _selections.Remove(personId); return; }
        if (!candidates.Any(candidate => candidate.Relationship.Id == relationshipId))
            throw new InvalidOperationException("关系不属于当前人物，请重新选择。");
        _selections[personId] = relationshipId.Value;
    }

    private void ValidateAcyclic(IEnumerable<Guid> ids)
    {
        var indegree = ids.ToDictionary(id => id, _ => 0);
        foreach (var candidate in _parents.Values.SelectMany(items => items)) indegree[candidate.Parent.Id]++;
        var ready = new Queue<Guid>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var count = 0;
        while (ready.TryDequeue(out var current))
        {
            count++;
            foreach (var edge in _parents[current]) if (--indegree[edge.Parent.Id] == 0) ready.Enqueue(edge.Parent.Id);
        }
        if (count != indegree.Count) throw new InvalidOperationException("上系关系存在循环，无法预览；请检查原始关系。");
    }
}
