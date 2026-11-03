using Xunit;
using ZupuPaiBan.Services;

namespace PuJiang.Tests;

public class HorizontalLineageLayoutTests
{
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");
    private static HorizontalLayoutPerson P(int id, int generation, int order = 0, double width = 80, double height = 30) => new(Id(id), generation, order, width, height);
    private static HorizontalLayoutRelationship E(int id, int parent, int child) => new(Id(id), Id(parent), Id(child));
    private static HorizontalLayoutResult Layout(HorizontalLayoutPerson[] p, HorizontalLayoutRelationship[] e, params int[] expanded) => HorizontalLineageLayout.Calculate(p, e, expanded.Select(Id).ToHashSet());

    [Fact]
    public void SinglePersonAndSingleGenerationForest()
    {
        var single = Layout([P(1, 3)], []);
        Assert.Single(single.Nodes); Assert.Empty(single.Edges);
        var forest = Layout([P(2, 3), P(1, 3)], []);
        Assert.Equal(forest.Nodes[0].X, forest.Nodes[1].X);
        Assert.True(forest.Nodes[0].Y + forest.Nodes[0].Height < forest.Nodes[1].Y);
    }

    [Fact]
    public void DirectLineUsesFixedColumnsAndOrthogonalEndpoints()
    {
        var result = Layout([P(1, 0), P(2, 1), P(3, 2)], [E(11, 1, 2), E(12, 2, 3)], 1, 2);
        Assert.Equal(3, result.Nodes.Count); Assert.Equal(2, result.Edges.Count);
        Assert.True(result.Nodes[0].X < result.Nodes[1].X && result.Nodes[1].X < result.Nodes[2].X);
        AssertEdges(result);
    }

    [Fact]
    public void BranchesAreOrderedAndDoNotOverlapOrCrossOtherBranches()
    {
        var p = new[] { P(1, 0), P(2, 1, 0, 100, 60), P(3, 1, 1), P(4, 2, 0), P(5, 2, 1), P(6, 2, 0), P(7, 2, 1) };
        var e = new[] { E(11, 1, 2), E(12, 1, 3), E(13, 2, 4), E(14, 2, 5), E(15, 3, 6), E(16, 3, 7) };
        var result = Layout(p, e, 1, 2, 3);
        Assert.Equal(7, result.Nodes.Count);
        Assert.True(result.Nodes.Single(n => n.PersonId == Id(2)).Y < result.Nodes.Single(n => n.PersonId == Id(3)).Y);
        foreach (var a in result.Nodes) foreach (var b in result.Nodes.Where(n => n.PersonId.CompareTo(a.PersonId) > 0))
            Assert.True(a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y);
        AssertEdges(result);
        foreach (var edge in result.Edges)
        foreach (var other in result.Edges.Where(e2 => e2.RelationshipId.CompareTo(edge.RelationshipId) > 0 && e2.ParentId != edge.ParentId))
        for (int i = 1; i < edge.Points.Count; i++)
        for (int j = 1; j < other.Points.Count; j++)
            Assert.False(Intersects(edge.Points[i - 1], edge.Points[i], other.Points[j - 1], other.Points[j]));
        foreach (var edge in result.Edges)
        foreach (var node in result.Nodes.Where(n => n.PersonId != edge.ParentId && n.PersonId != edge.ChildId))
        for (int i = 1; i < edge.Points.Count; i++)
        {
            var a = edge.Points[i - 1]; var b = edge.Points[i];
            Assert.False(a.X == b.X ? a.X > node.X && a.X < node.X + node.Width && Math.Max(a.Y,b.Y) > node.Y && Math.Min(a.Y,b.Y) < node.Y + node.Height :
                a.Y > node.Y && a.Y < node.Y + node.Height && Math.Max(a.X,b.X) > node.X && Math.Min(a.X,b.X) < node.X + node.Width);
        }
    }

    [Fact]
    public void ExpandedStateProjectsOnlyReachableDescendantsAndKeepsColumns()
    {
        var p = new[] { P(1, 0), P(2, 1), P(3, 2), P(4, 1, 1, 200) };
        var e = new[] { E(11, 1, 2), E(12, 2, 3), E(13, 1, 4) };
        var closed = Layout(p, e, 2);
        Assert.Single(closed.Nodes); Assert.Empty(closed.Edges);
        var partial = Layout(p, e, 1);
        Assert.Equal(3, partial.Nodes.Count); Assert.Equal(2, partial.Edges.Count);
        var open = Layout(p, e, 1, 2);
        Assert.Equal(4, open.Nodes.Count);
        Assert.Equal(partial.Nodes.Single(n => n.PersonId == Id(2)).X, open.Nodes.Single(n => n.PersonId == Id(2)).X);
    }

    [Fact]
    public void InputOrderDoesNotChangeResultOrMutateInputs()
    {
        var p = new[] { P(1, 0), P(2, 1), P(3, 1, 1), P(4, 2) };
        var e = new[] { E(11, 1, 2), E(12, 1, 3), E(13, 2, 4) };
        var pBefore = p.ToArray(); var eBefore = e.ToArray();
        var a = Layout(p, e, 1, 2); var b = Layout(p.Reverse().ToArray(), e.Reverse().ToArray(), 2, 1);
        Assert.Equal(a.Nodes, b.Nodes);
        Assert.Equal(a.Edges.Select(x => (x.RelationshipId, x.ParentId, x.ChildId, string.Join(";", x.Points))), b.Edges.Select(x => (x.RelationshipId, x.ParentId, x.ChildId, string.Join(";", x.Points))));
        Assert.Equal(pBefore, p); Assert.Equal(eBefore, e);
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        yield return [new[] { P(1, -1) }, Array.Empty<HorizontalLayoutRelationship>(), "Invalid person"];
        yield return [new[] { P(1, 0), P(1, 0) }, Array.Empty<HorizontalLayoutRelationship>(), "Duplicate person"];
        yield return [new[] { P(2, 1) }, new[] { E(11, 1, 2) }, "missing person"];
        yield return [new[] { P(1, 0), P(2, 1), P(3, 1) }, new[] { E(11, 1, 2), E(12, 1, 3) }, "Sibling orders"];
        yield return [new[] { P(1, 0), P(2, 2) }, new[] { E(11, 1, 2) }, "adjacent generations"];
        yield return [new[] { P(1, 0), P(2, 1), P(3, 0) }, new[] { E(11, 1, 2), E(12, 3, 2) }, "multiple parent"];
        yield return [new[] { P(1, 0, 0, double.NaN) }, Array.Empty<HorizontalLayoutRelationship>(), "Invalid person"];
        yield return [new[] { P(1, 0), P(2, 1) }, new[] { E(11, 1, 2), E(11, 1, 2) }, "duplicate relationship"];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void InvalidInputFailsWithoutResultEvenWhenInvalidBranchIsCollapsed(HorizontalLayoutPerson[] p, HorizontalLayoutRelationship[] e, string message)
    {
        HorizontalLayoutResult? result = null;
        var error = Assert.Throws<HorizontalLayoutException>(() => result = Layout(p, e));
        Assert.Null(result); Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownExpansionFails() => Assert.Throws<HorizontalLayoutException>(() => Layout([P(1, 0)], [], 2));

    [Fact]
    public void MultipleRootsMixedSizesRemainSeparatedAndDeterministicWhenBranchesChange()
    {
        var people = new[] { P(1, 0, 0, 12, 110), P(2, 1, 0, 180, 15), P(3, 1, 1, 30, 80), P(4, 2, 0, 25, 130),
            P(5, 0, 1, 60, 20), P(6, 1, 0, 40, 95), P(7, 2, 0, 200, 25), P(8, 1, 4, 90, 35), P(9, 2, 0, 22, 85) };
        var edges = new[] { E(101, 1, 2), E(102, 1, 3), E(103, 3, 4), E(104, 5, 6), E(105, 6, 7), E(106, 8, 9) };
        foreach (var expanded in new[] { new[] { 1, 3, 5, 6, 8 }, new[] { 1, 5, 8 }, new[] { 3, 6 } })
        {
            var result = Layout(people, edges, expanded);
            var reordered = Layout(people.Reverse().ToArray(), edges.Reverse().ToArray(), expanded.Reverse().ToArray());
            Assert.Equal(result.Nodes, reordered.Nodes);
            Assert.Equal(result.Edges.Select(e => (e.RelationshipId, Points: string.Join(";", e.Points))),
                reordered.Edges.Select(e => (e.RelationshipId, Points: string.Join(";", e.Points))));
            AssertGeometry(result);
        }
    }

    [Theory]
    [InlineData(double.Epsilon, 30, 1)]
    [InlineData(80, double.Epsilon, 0)]
    [InlineData(double.MaxValue, 30, 0)]
    [InlineData(80, double.MaxValue, 0)]
    public void UnrepresentableGeometryFailsWithoutResult(double width, double height, int generation)
    {
        HorizontalLayoutResult? result = null;
        Assert.Throws<HorizontalLayoutException>(() => result = Layout([P(1, generation, 0, width, height)], []));
        Assert.Null(result);
    }

    [Fact]
    public void LargeGenerationEitherProducesRepresentableGeometryOrFailsClearly()
    {
        var result = Layout([P(1, int.MaxValue, 0, 80, 30)], []);
        var node = Assert.Single(result.Nodes);
        Assert.True(node.X + node.Width > node.X);
        Assert.True(double.IsFinite(node.X + node.Width));
    }

    [Fact]
    public void UnrepresentableEdgeTurnFailsWithoutPartialResult()
    {
        var width = Math.Pow(2, 52);
        HorizontalLayoutResult? result = null;
        var error = Assert.Throws<HorizontalLayoutException>(() =>
            result = Layout([P(1, 63, 0, width), P(2, 64, 0, width)], [E(11, 1, 2)], 1));
        Assert.Null(result);
        Assert.Contains("precision", error.Message);
    }

    private static void AssertGeometry(HorizontalLayoutResult result)
    {
        AssertEdges(result);
        foreach (var group in result.Nodes.GroupBy(n => n.Generation))
            Assert.Single(group.Select(n => n.X).Distinct());
        foreach (var a in result.Nodes)
        foreach (var b in result.Nodes.Where(n => n.PersonId.CompareTo(a.PersonId) > 0))
            Assert.True(a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y);
        foreach (var edge in result.Edges)
        {
            foreach (var other in result.Edges.Where(e => e.RelationshipId.CompareTo(edge.RelationshipId) > 0 && e.ParentId != edge.ParentId))
                for (var i = 1; i < edge.Points.Count; i++)
                for (var j = 1; j < other.Points.Count; j++)
                    Assert.False(Intersects(edge.Points[i - 1], edge.Points[i], other.Points[j - 1], other.Points[j]));
            foreach (var node in result.Nodes.Where(n => n.PersonId != edge.ParentId && n.PersonId != edge.ChildId))
                for (var i = 1; i < edge.Points.Count; i++)
                {
                    var a = edge.Points[i - 1]; var b = edge.Points[i];
                    Assert.False(a.X == b.X ? a.X > node.X && a.X < node.X + node.Width && Math.Max(a.Y, b.Y) > node.Y && Math.Min(a.Y, b.Y) < node.Y + node.Height :
                        a.Y > node.Y && a.Y < node.Y + node.Height && Math.Max(a.X, b.X) > node.X && Math.Min(a.X, b.X) < node.X + node.Width);
                }
        }
    }

    private static bool Intersects(HorizontalLayoutPoint a, HorizontalLayoutPoint b, HorizontalLayoutPoint c, HorizontalLayoutPoint d)
    {
        return Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) &&
               Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
    }

    private static void AssertEdges(HorizontalLayoutResult result)
    {
        foreach (var edge in result.Edges)
        {
            var parent = result.Nodes.Single(n => n.PersonId == edge.ParentId);
            var child = result.Nodes.Single(n => n.PersonId == edge.ChildId);
            Assert.Equal(new HorizontalLayoutPoint(parent.X + parent.Width, parent.Y + parent.Height / 2), edge.Points[0]);
            Assert.Equal(new HorizontalLayoutPoint(child.X, child.Y + child.Height / 2), edge.Points[^1]);
            for (int i = 1; i < edge.Points.Count; i++)
                Assert.True(edge.Points[i - 1].X == edge.Points[i].X || edge.Points[i - 1].Y == edge.Points[i].Y);
        }
    }
}
