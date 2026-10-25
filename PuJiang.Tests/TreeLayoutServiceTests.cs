using PuJiang.Tests.TestData;
using Xunit;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace PuJiang.Tests;

public class TreeLayoutServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Category", "Layout")]
    public void Horizontal_layout_is_deterministic_for_the_fixed_three_generation_sample()
    {
        var settings = new LayoutSettings { LayoutMode = LayoutMode.Horizontal };
        var service = new TreeLayoutService(settings);

        var first = service.CalculateLayout(FamilyTestData.ThreeGenerations());
        var second = service.CalculateLayout(FamilyTestData.ThreeGenerations());

        Assert.Equal(first.TotalWidth, second.TotalWidth);
        Assert.Equal(first.TotalHeight, second.TotalHeight);
        Assert.Equal(
            first.Nodes.OrderBy(node => node.Member.Id).Select(node => (node.Member.Id, node.X, node.Y)),
            second.Nodes.OrderBy(node => node.Member.Id).Select(node => (node.Member.Id, node.X, node.Y)));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Thousand_person_fixture_is_repeatable_and_contains_only_fictitious_records()
    {
        var sample = FamilyTestData.ThousandPersonSample();

        Assert.Equal(1_000, sample.Count);
        Assert.All(sample, member => Assert.StartsWith("虚构", member.Name));
        Assert.DoesNotContain(sample, member => member.FatherId == member.Id);
    }
}
