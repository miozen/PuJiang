using Xunit;
using ZupuPaiBan.Services;

namespace PuJiang.Tests;

public class InlineNamePlacementTests
{
    [Fact]
    public void TemporaryChildUsesTheSameLayoutAsTheFullVisibleTreeWithoutModifyingInputs()
    {
        var parent = Guid.NewGuid(); var existing = Guid.NewGuid(); var temporary = Guid.NewGuid();
        var people = new[] { new HorizontalLayoutPerson(parent, 0, 0, 170, 85),
            new(existing, 1, 0, 170, 85), new(temporary, 1, 1, 170, 85) };
        var edges = new[] { new HorizontalLayoutRelationship(Guid.NewGuid(), parent, existing),
            new HorizontalLayoutRelationship(Guid.NewGuid(), parent, temporary) };
        var expanded = new HashSet<Guid> { parent };
        var projected = InlineNamePlacement.Project(people, edges, expanded, temporary);
        var layout = HorizontalLineageLayout.Calculate(people, edges, expanded, allowSkippedGenerations: true);
        Assert.Equal(layout.Nodes.Single(node => node.PersonId == temporary), projected);
        Assert.Equal(layout.Nodes.Single(node => node.PersonId == existing).X, projected.X);
        Assert.True(projected.Y > layout.Nodes.Single(node => node.PersonId == existing).Y);
        Assert.Equal(3, people.Length); Assert.Equal(2, edges.Length); Assert.Single(expanded);
        Assert.Throws<HorizontalLayoutException>(() => InlineNamePlacement.Project(people, edges, new HashSet<Guid>(), temporary));
    }
}
