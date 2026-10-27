using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Services;

/// <summary>以人物为入口列出关系；不选取单一父母、不推导世代。</summary>
public sealed record TargetMemberTreeNode(Guid PersonId, string Label, IReadOnlyList<string> Details);

public static class TargetMemberTreeProjection
{
    public static IReadOnlyList<TargetMemberTreeNode> Create(GenealogyGraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var people = snapshot.People.ToDictionary(p => p.Id);
        var details = people.Keys.ToDictionary(id => id, _ => new List<string>());
        var parentCounts = people.Keys.ToDictionary(id => id, _ => 0);

        string Display(Guid id) => people.TryGetValue(id, out var person)
            ? $"{person.Name}（ID: {person.Id}）"
            : throw new InvalidOperationException($"谱系快照引用了不存在的人物：{id}");

        foreach (var relation in snapshot.Relationships.OrderBy(r => r.Id))
        {
            if (!people.ContainsKey(relation.PersonId))
                throw new InvalidOperationException($"谱系快照关系主体不存在：{relation.PersonId}");
            var parentKind = relation.Kind is PersonRelationshipKind.BiologicalParent or PersonRelationshipKind.AdoptiveParent or PersonRelationshipKind.HeirParent;
            var kind = relation.Kind switch
            {
                PersonRelationshipKind.BiologicalParent => "生物父母",
                PersonRelationshipKind.AdoptiveParent => "收养父母",
                PersonRelationshipKind.HeirParent => "嗣继父母",
                PersonRelationshipKind.Spouse => "配偶关系",
                PersonRelationshipKind.LostAncestry => "上系失考",
                _ => $"未知关系类型 {(int)relation.Kind}"
            };
            var other = relation.RelatedPersonId is Guid id ? Display(id) : "未记录对方";
            var preferred = relation.IsPreferredForLineage ? " [谱载首选标记]" : string.Empty;
            var note = string.IsNullOrWhiteSpace(relation.Note) ? string.Empty : $"；备注：{relation.Note}";
            var source = string.IsNullOrWhiteSpace(relation.SourceCitation) ? string.Empty : $"；来源：{relation.SourceCitation}";
            details[relation.PersonId].Add($"{kind}：{other}{preferred}{note}{source}（关系 ID: {relation.Id}）");
            if (parentKind)
            {
                parentCounts[relation.PersonId]++;
                if (relation.RelatedPersonId is Guid parentId)
                    details[parentId].Add($"{kind}对应子女：{Display(relation.PersonId)}（关系 ID: {relation.Id}）");
            }
        }

        foreach (var union in snapshot.Unions.OrderBy(u => u.Id))
        {
            var first = Display(union.PartnerOneId);
            var second = union.PartnerTwoId is Guid otherId ? Display(otherId) : "对方资料未记录";
            var kind = union.Kind switch
            {
                FamilyUnionKind.Marriage => "婚配",
                FamilyUnionKind.Partnership => "伴侣",
                FamilyUnionKind.Unknown => "关系类型未详",
                _ => $"未知婚配类型 {(int)union.Kind}"
            };
            var start = string.IsNullOrWhiteSpace(union.StartDateText) ? string.Empty : $"；始于：{union.StartDateText}";
            var end = string.IsNullOrWhiteSpace(union.EndDateText) ? string.Empty : $"；止于：{union.EndDateText}";
            var note = string.IsNullOrWhiteSpace(union.Note) ? string.Empty : $"；备注：{union.Note}";
            details[union.PartnerOneId].Add($"{kind}：{second}{start}{end}{note}（记录 ID: {union.Id}）");
            if (union.PartnerTwoId is Guid partnerId)
                details[partnerId].Add($"{kind}：{first}{start}{end}{note}（记录 ID: {union.Id}）");
        }

        return snapshot.People.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Id).Select(person =>
        {
            var entries = details[person.Id];
            if (parentCounts[person.Id] > 1) entries.Insert(0, "存在多条父母关系；未自动选择展示主线");
            if (entries.Count == 0) entries.Add("未记录人物关系（不推断为始祖）");
            return new TargetMemberTreeNode(person.Id, Display(person.Id), entries.AsReadOnly());
        }).ToList().AsReadOnly();
    }
}
