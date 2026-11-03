namespace ZupuPaiBan.Services;

public sealed record LineageGraphNode(LineageTreeNode Person, double X, double Y, bool Expanded);
public sealed record LineageGraphEdge(Guid ParentId, Guid ChildId, Guid RelationshipId);
public sealed record LineageGraphLayout(IReadOnlyList<LineageGraphNode> Nodes, IReadOnlyList<LineageGraphEdge> Edges, double Width, double Height);

/// <summary>Deterministic layout of visible nodes from an already validated lineage projection.</summary>
public static class LineageGraphLayoutService
{
    public const double NodeWidth = 170, NodeHeight = 72, HorizontalGap = 24, VerticalGap = 55;

    public static LineageGraphLayout Calculate(LineageTreeState tree, IReadOnlySet<Guid> expanded)
    {
        var widths = new Dictionary<Guid, double>();
        var stack = new Stack<(LineageTreeNode Node, bool Visited)>();
        foreach (var root in tree.Roots.Reverse()) stack.Push((root, false));
        while (stack.TryPop(out var item))
        {
            var children = expanded.Contains(item.Node.PersonId) ? item.Node.Children : Array.Empty<LineageTreeNode>().AsEnumerable();
            if (!item.Visited)
            {
                stack.Push((item.Node, true));
                foreach (var child in children.Reverse()) stack.Push((child, false));
            }
            else
            {
                var list = children.ToArray();
                widths[item.Node.PersonId] = Math.Max(NodeWidth, list.Sum(c => widths[c.PersonId]) + Math.Max(0, list.Length - 1) * HorizontalGap);
            }
        }
        var nodes = new List<LineageGraphNode>();
        var edges = new List<LineageGraphEdge>();
        var positions = new Stack<(LineageTreeNode Node, double Left, int Depth)>();
        double left = 20;
        foreach (var root in tree.Roots)
        {
            positions.Push((root, left, 0));
            left += widths[root.PersonId] + HorizontalGap;
        }
        int maxDepth = 0;
        while (positions.TryPop(out var item))
        {
            var node = item.Node;
            maxDepth = Math.Max(maxDepth, item.Depth);
            nodes.Add(new LineageGraphNode(node, item.Left + (widths[node.PersonId] - NodeWidth) / 2,
                20 + item.Depth * (NodeHeight + VerticalGap), expanded.Contains(node.PersonId)));
            if (!expanded.Contains(node.PersonId)) continue;
            var childLeft = item.Left;
            foreach (var child in node.Children)
            {
                var selected = child.Projection.SelectedParent ?? throw new InvalidOperationException("已显示的父母边缺少关系。");
                edges.Add(new LineageGraphEdge(node.PersonId, child.PersonId, selected.Relationship.Id));
                positions.Push((child, childLeft, item.Depth + 1));
                childLeft += widths[child.PersonId] + HorizontalGap;
            }
        }
        return new LineageGraphLayout(nodes.AsReadOnly(), edges.AsReadOnly(),
            Math.Max(200, left + 20), 40 + (maxDepth + 1) * NodeHeight + maxDepth * VerticalGap);
    }
}
