namespace PuJiang.Domain;

public static class OrdinaryChildRules
{
    public static void ValidateFather(Person person, Placement placement, Guid lineageId)
    {
        ArgumentNullException.ThrowIfNull(person); ArgumentNullException.ThrowIfNull(placement);
        if (lineageId == Guid.Empty || person.LineageId != lineageId || placement.LineageId != lineageId || placement.PersonId != person.Id)
            throw new InvalidOperationException("父人物与当前位置不属于同一谱系或不匹配。");
        if (person.Sex != PersonSex.Male) throw new InvalidOperationException("父亲角色与人物性别不一致或尚未确定，请先核实人物资料。");
    }
}
