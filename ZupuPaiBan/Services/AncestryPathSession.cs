using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Services;

public sealed record AncestryChoice(Guid RelationshipId, Guid ParentId, string Label);

/// <summary>只在一份族谱快照中选择单条上系路径；不修改或保存原始关系。</summary>
public sealed class AncestryPathSession
{
    private readonly Dictionary<Guid, GenealogyPersonData> _people;
    private readonly IReadOnlyList<GenealogyRelationshipData> _relationships;
    private readonly HashSet<Guid> _visited = new();
    private readonly List<string> _path = new();
    private List<AncestryChoice> _choices = new();
    public IReadOnlyList<string> Path => _path.AsReadOnly();
    public IReadOnlyList<AncestryChoice> Choices => _choices.AsReadOnly();
    public string Status { get; private set; } = string.Empty;

    public AncestryPathSession(GenealogyGraphSnapshot snapshot, Guid personId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _people = snapshot.People.ToDictionary(person => person.Id);
        _relationships = snapshot.Relationships;
        Advance(personId, null);
    }

    public void Choose(Guid relationshipId)
    {
        var choice = _choices.SingleOrDefault(item => item.RelationshipId == relationshipId)
            ?? throw new InvalidOperationException("上系选择已失效，请重新选择。");
        Advance(choice.ParentId, choice.Label);
    }

    private void Advance(Guid personId, string? via)
    {
        _choices = new();
        while (true)
        {
            if (!_people.TryGetValue(personId, out var person) || !_visited.Add(personId))
            {
                _path.Clear();
                Status = "上系数据存在循环或缺失人物，无法预览；请检查原始关系。";
                return;
            }
            var lost = _relationships.Any(r => r.PersonId == personId && r.Kind == PersonRelationshipKind.LostAncestry);
            _path.Add($"{(via is null ? "起点" : via)} → {person.Name}（ID: {person.Id}）{(lost ? "；谱载上系失考" : string.Empty)}");
            var parents = _relationships.Where(r => r.PersonId == personId && r.Kind is PersonRelationshipKind.BiologicalParent or
                PersonRelationshipKind.AdoptiveParent or PersonRelationshipKind.HeirParent).OrderBy(r => r.Id).ToList();
            if (parents.Count == 0)
            {
                Status = "此人物未记录父母关系；不推断为始祖。";
                return;
            }
            foreach (var relation in parents)
            {
                if (relation.RelatedPersonId is not Guid parentId || !_people.TryGetValue(parentId, out var parent))
                {
                    _path.Clear(); _choices.Clear();
                    Status = "上系关系缺少对方人物，无法预览；请检查原始关系。";
                    return;
                }
                var kind = relation.Kind switch { PersonRelationshipKind.BiologicalParent => "生物父母", PersonRelationshipKind.AdoptiveParent => "养父母", _ => "继嗣父母" };
                var preferred = relation.IsPreferredForLineage ? "；谱载首选标记" : string.Empty;
                var note = string.IsNullOrWhiteSpace(relation.Note) ? string.Empty : $"；备注：{relation.Note}";
                var source = string.IsNullOrWhiteSpace(relation.SourceCitation) ? string.Empty : $"；来源：{relation.SourceCitation}";
                _choices.Add(new(relation.Id, parentId, $"{kind}：{parent.Name}（ID: {parentId}；关系 ID: {relation.Id}{preferred}{note}{source}）"));
            }
            if (_choices.Count > 1)
            {
                Status = "存在多条父母关系，请选择本次预览的上系。";
                return;
            }
            var next = _choices[0];
            _choices.Clear();
            personId = next.ParentId;
            via = next.Label;
        }
    }
}
