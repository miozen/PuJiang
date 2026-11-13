using PuJiang.Domain;
using Xunit;
namespace PuJiang.Tests;
public sealed class V1DomainTests
{
    [Fact]
    public void Empty_id_is_rejected_and_empty_name_is_allowed()
    {
        Assert.Throws<ArgumentException>(() => new Person(Guid.Empty, Guid.NewGuid(), ""));
        Assert.Throws<ArgumentException>(() => new Placement(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));
        Assert.Equal("", new Person(Guid.NewGuid(), Guid.NewGuid(), "").Name);
    }
}
