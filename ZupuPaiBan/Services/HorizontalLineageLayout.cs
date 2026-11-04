namespace ZupuPaiBan.Services;

// This engine only accepts an explicitly ordered, adjacent-generation forest.
public sealed record HorizontalLayoutPerson(Guid Id, int Generation, int SiblingOrder, double Width, double Height);
public sealed record HorizontalLayoutRelationship(Guid Id, Guid ParentId, Guid ChildId);
public readonly record struct HorizontalLayoutPoint(double X, double Y);
public sealed record HorizontalLayoutNode(Guid PersonId, int Generation, double X, double Y, double Width, double Height);
public sealed record HorizontalLayoutEdge(Guid RelationshipId, Guid ParentId, Guid ChildId, IReadOnlyList<HorizontalLayoutPoint> Points);
public sealed record HorizontalLayoutResult(IReadOnlyList<HorizontalLayoutNode> Nodes, IReadOnlyList<HorizontalLayoutEdge> Edges);

public sealed class HorizontalLayoutException(string message) : ArgumentException(message);

public static class HorizontalLineageLayout
{
    private const double ColumnGap = 64;
    private const double RowGap = 24;

    public static HorizontalLayoutResult Calculate(
        IReadOnlyCollection<HorizontalLayoutPerson> persons,
        IReadOnlyCollection<HorizontalLayoutRelationship> relationships,
        IReadOnlySet<Guid> expandedIds)
    {
        ArgumentNullException.ThrowIfNull(persons);
        ArgumentNullException.ThrowIfNull(relationships);
        ArgumentNullException.ThrowIfNull(expandedIds);
        // Snapshot all inputs before validation or projection; never modify caller data.
        var people = persons.ToArray();
        var relations = relationships.ToArray();
        var expanded = expandedIds.ToHashSet();
        var byId = new Dictionary<Guid, HorizontalLayoutPerson>();
        foreach (var p in people)
        {
            if (p is null || p.Id == Guid.Empty || p.Generation < 0 || p.SiblingOrder < 0 ||
                !double.IsFinite(p.Width) || p.Width <= 0 || !double.IsFinite(p.Height) || p.Height <= 0)
                throw new HorizontalLayoutException("Invalid person ID, generation, order or dimensions.");
            if (!byId.TryAdd(p.Id, p))
                throw new HorizontalLayoutException($"Duplicate person ID: {p.Id}.");
        }
        if (expanded.Any(id => !byId.ContainsKey(id)))
            throw new HorizontalLayoutException("Expanded person ID does not exist.");
        var children = byId.Keys.ToDictionary(id => id, _ => new List<HorizontalLayoutRelationship>());
        var childIds = new HashSet<Guid>();
        var edgeIds = new HashSet<Guid>();
        foreach (var edge in relations)
        {
            if (edge is null || edge.Id == Guid.Empty || !edgeIds.Add(edge.Id))
                throw new HorizontalLayoutException("Invalid or duplicate relationship ID.");
            if (!byId.TryGetValue(edge.ParentId, out var parent) || !byId.TryGetValue(edge.ChildId, out var child))
                throw new HorizontalLayoutException($"Relationship {edge.Id} references a missing person.");
            if (!childIds.Add(child.Id))
                throw new HorizontalLayoutException($"Person {child.Id} has multiple parent edges.");
            if ((long)child.Generation != (long)parent.Generation + 1)
                throw new HorizontalLayoutException($"Relationship {edge.Id} must join adjacent generations.");
            children[parent.Id].Add(edge);
        }
        foreach (var group in children.Values)
        {
            if (group.Select(e => byId[e.ChildId].SiblingOrder).Distinct().Count() != group.Count)
                throw new HorizontalLayoutException("Sibling orders conflict under the same parent.");
            group.Sort((a, b) => byId[a.ChildId].SiblingOrder.CompareTo(byId[b.ChildId].SiblingOrder));
        }
        if (people.Length == 0)
            return new HorizontalLayoutResult(Array.Empty<HorizontalLayoutNode>(), Array.Empty<HorizontalLayoutEdge>());
        var columnWidth = people.Max(p => p.Width) + ColumnGap;
        var rowHeight = people.Max(p => p.Height) + RowGap;
        if (!double.IsFinite(columnWidth) || !double.IsFinite(rowHeight) ||
            columnWidth <= people.Max(p => p.Width) || rowHeight <= people.Max(p => p.Height))
            throw new HorizontalLayoutException("Dimensions exceed the supported coordinate range.");
        var roots = people.Where(p => !childIds.Contains(p.Id)).OrderBy(p => p.Generation)
            .ThenBy(p => p.SiblingOrder).ThenBy(p => p.Id).ToArray();
        var nodeMap = new Dictionary<Guid, HorizontalLayoutNode>();
        var visibleEdges = new List<HorizontalLayoutRelationship>();
        double nextCenter = rowHeight / 2;
        foreach (var root in roots)
        {
            var stack = new Stack<(HorizontalLayoutPerson Person, bool Visited)>();
            stack.Push((root, false));
            while (stack.TryPop(out var item))
            {
                var p = item.Person;
                var visibleChildren = expanded.Contains(p.Id) ? children[p.Id] : null;
                if (!item.Visited)
                {
                    stack.Push((p, true));
                    if (visibleChildren is not null)
                        for (int i = visibleChildren.Count - 1; i >= 0; i--)
                            stack.Push((byId[visibleChildren[i].ChildId], false));
                    continue;
                }
                double center;
                if (visibleChildren is null || visibleChildren.Count == 0)
                {
                    center = nextCenter;
                    nextCenter += rowHeight;
                }
                else
                {
                    var first = nodeMap[visibleChildren[0].ChildId];
                    center = first.Y + first.Height / 2;
                    visibleEdges.AddRange(visibleChildren);
                }
                var x = p.Generation * columnWidth;
                var y = center - p.Height / 2;
                if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(x + p.Width) || !double.IsFinite(y + p.Height) ||
                    x + p.Width <= x || x + p.Width >= ((double)p.Generation + 1) * columnWidth ||
                    y + p.Height <= y || y + p.Height / 2 <= y || y + p.Height / 2 >= y + p.Height)
                    throw new HorizontalLayoutException("Layout exceeds the supported coordinate range.");
                nodeMap.Add(p.Id, new(p.Id, p.Generation, x, y, p.Width, p.Height));
            }
        }
        var nodes = nodeMap.Values.OrderBy(n => n.Generation).ThenBy(n => n.Y).ThenBy(n => n.PersonId).ToArray();
        var edges = visibleEdges.OrderBy(e => e.Id).Select(e =>
        {
            var parent = nodeMap[e.ParentId];
            var child = nodeMap[e.ChildId];
            var start = new HorizontalLayoutPoint(parent.X + parent.Width, parent.Y + parent.Height / 2);
            var end = new HorizontalLayoutPoint(child.X, child.Y + child.Height / 2);
            var mid = start.X + (end.X - start.X) / 2;
            if (!double.IsFinite(mid) || mid <= start.X || mid >= end.X)
                throw new HorizontalLayoutException("Edge routing exceeds the supported coordinate precision.");
            return new HorizontalLayoutEdge(e.Id, e.ParentId, e.ChildId,
                Array.AsReadOnly(new[] { start, new HorizontalLayoutPoint(mid, start.Y), new HorizontalLayoutPoint(mid, end.Y), end }));
        }).ToArray();
        return new HorizontalLayoutResult(Array.AsReadOnly(nodes), Array.AsReadOnly(edges));
    }
}
