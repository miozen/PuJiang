using ZupuPaiBan.Models;

namespace ZupuPaiBan.Domain;

/// <summary>Rules for an already verified, complete group; no inference from IDs or names.</summary>
internal static class WorkbenchOrderRules
{
    internal sealed record Member(Guid Id, PersonSex Sex);

    internal static IReadOnlyList<Member> Append(IReadOnlyList<Member> current, Member added, bool groupBySex)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(added);
        Validate(current, groupBySex);
        if (added.Id == Guid.Empty || current.Any(m => m.Id == added.Id)) throw new InvalidOperationException("排序人物身份无效或重复。");
        if (groupBySex && added.Sex is not (PersonSex.Male or PersonSex.Female)) throw new InvalidOperationException("请先更正人物性别，再参与排序。");
        var result = current.ToList();
        var index = groupBySex && added.Sex == PersonSex.Male ? result.FindIndex(m => m.Sex == PersonSex.Female) : -1;
        if (index < 0) result.Add(added); else result.Insert(index, added);
        return result.AsReadOnly();
    }

    internal static IReadOnlyList<Member> Move(IReadOnlyList<Member> current, Guid memberId, int targetIndex, bool groupBySex)
    {
        ArgumentNullException.ThrowIfNull(current); Validate(current, groupBySex);
        if (targetIndex < 0 || targetIndex >= current.Count) throw new ArgumentOutOfRangeException(nameof(targetIndex));
        var source = current.ToList(); var from = source.FindIndex(m => m.Id == memberId);
        if (from < 0) throw new InvalidOperationException("人物不属于当前排序组。");
        if (groupBySex && source[from].Sex != source[targetIndex].Sex) throw new InvalidOperationException("兄妹排序须保持男性在女性之前。");
        var item = source[from]; source.RemoveAt(from); source.Insert(targetIndex, item);
        return source.AsReadOnly();
    }

    internal static void Validate(IReadOnlyList<Member> members, bool groupBySex)
    {
        if (members.Any(m => m is null || m.Id == Guid.Empty) || members.Select(m => m.Id).Distinct().Count() != members.Count)
            throw new InvalidOperationException("排序人物身份无效或重复。");
        if (!groupBySex) return;
        var seenFemale = false;
        foreach (var member in members)
        {
            if (member.Sex is not (PersonSex.Male or PersonSex.Female)) throw new InvalidOperationException("请先更正人物性别，再参与排序。");
            if (member.Sex == PersonSex.Female) seenFemale = true;
            else if (seenFemale) throw new InvalidOperationException("兄妹排序须保持男性在女性之前。");
        }
    }
}
