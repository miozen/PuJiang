using PuJiang.Domain;
using Xunit;
namespace PuJiang.Tests;
public sealed class V1DomainTests
{
    [Fact]
    public void Empty_id_and_blank_name_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Person(Guid.Empty, Guid.NewGuid(), ""));
        Assert.Throws<ArgumentException>(() => new Placement(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));
        Assert.Throws<ArgumentException>(() => new Person(Guid.NewGuid(), Guid.NewGuid(), " "));
    }
}
