using Xunit;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class WorkbenchOrderRulesTests
{
    private static WorkbenchOrderRules.Member M(int n, PersonSex sex) => new(Guid.Parse($"00000000-0000-0000-0000-{n:D12}"), sex);

    [Fact]
    public void Children_append_in_sex_group_and_move_only_inside_that_group()
    {
        var olderBoy = M(8, PersonSex.Male); var olderGirl = M(7, PersonSex.Female);
        var newBoy = M(1, PersonSex.Male); var newGirl = M(2, PersonSex.Female);
        var initial = new[] { olderBoy, olderGirl };
        var withBoy = WorkbenchOrderRules.Append(initial, newBoy, true);
        var withBoth = WorkbenchOrderRules.Append(withBoy, newGirl, true);
        Assert.Equal(new[] { olderBoy, newBoy, olderGirl, newGirl }, withBoth);
        Assert.Equal(new[] { newBoy, olderBoy, olderGirl, newGirl }, WorkbenchOrderRules.Move(withBoth, newBoy.Id, 0, true));
        Assert.Throws<InvalidOperationException>(() => WorkbenchOrderRules.Move(withBoth, newBoy.Id, 2, true));
        Assert.Equal(new[] { olderBoy, olderGirl }, initial);
    }

    [Fact]
    public void Spouse_append_and_move_use_explicit_order_not_ids()
    {
        var first = M(9, PersonSex.Female); var second = M(1, PersonSex.Unknown); var third = M(5, PersonSex.Male);
        var order = WorkbenchOrderRules.Append(new[] { first, second }, third, false);
        Assert.Equal(new[] { first, second, third }, order);
        Assert.Equal(new[] { third, first, second }, WorkbenchOrderRules.Move(order, third.Id, 0, false));
    }

    [Fact]
    public void Invalid_group_unknown_sex_and_duplicate_members_fail_closed()
    {
        var male = M(1, PersonSex.Male); var female = M(2, PersonSex.Female);
        Assert.Throws<InvalidOperationException>(() => WorkbenchOrderRules.Append(new[] { male }, M(3, PersonSex.Unknown), true));
        Assert.Throws<InvalidOperationException>(() => WorkbenchOrderRules.Append(new[] { male }, male, true));
        Assert.Throws<InvalidOperationException>(() => WorkbenchOrderRules.Validate(new[] { female, male }, true));
        Assert.Throws<InvalidOperationException>(() => WorkbenchOrderRules.Move(new[] { male }, female.Id, 0, true));
    }
}
