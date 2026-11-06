namespace ZupuPaiBan.Services;

/// <summary>Projects a caller-supplied temporary node without changing the source graph.</summary>
internal static class InlineNamePlacement
{
    internal static HorizontalLayoutNode Project(
        IReadOnlyCollection<HorizontalLayoutPerson> people,
        IReadOnlyCollection<HorizontalLayoutRelationship> relationships,
        IReadOnlySet<Guid> expanded,
        Guid temporaryId)
    {
        var layout = HorizontalLineageLayout.Calculate(people, relationships, expanded, allowSkippedGenerations: true);
        return layout.Nodes.SingleOrDefault(node => node.PersonId == temporaryId)
            ?? throw new HorizontalLayoutException("The temporary input position is not visible in the projected layout.");
    }
}
