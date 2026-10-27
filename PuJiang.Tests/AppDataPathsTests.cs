using Xunit;
using ZupuPaiBan.Infrastructure;

namespace PuJiang.Tests;

public class AppDataPathsTests
{
    private static readonly string Production = Path.Combine(Path.GetTempPath(), "PuJiangProduction");

    [Fact]
    [Trait("Category", "Unit")]
    public void Default_is_preserved_and_explicit_isolated_path_is_used()
    {
        var isolated = Path.Combine(Path.GetTempPath(), "PuJiangIsolated", "test");
        Assert.Equal(Production, AppDataPaths.ResolveRootDirectory(null, Production));
        Assert.Equal(Path.GetFullPath(isolated), AppDataPaths.ResolveRootDirectory(isolated, Production));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData("relative-test")]
    public void Invalid_override_never_falls_back_to_production(string value) =>
        Assert.Throws<InvalidOperationException>(() => AppDataPaths.ResolveRootDirectory(value, Production));

    [Fact]
    [Trait("Category", "Unit")]
    public void Override_cannot_overlap_production_directory()
    {
        Assert.Throws<InvalidOperationException>(() => AppDataPaths.ResolveRootDirectory(Production, Production));
        Assert.Throws<InvalidOperationException>(() => AppDataPaths.ResolveRootDirectory(Path.Combine(Production, "test"), Production));
        Assert.Throws<InvalidOperationException>(() => AppDataPaths.ResolveRootDirectory(Path.GetDirectoryName(Production)!, Production));
        Assert.Throws<InvalidOperationException>(() => AppDataPaths.ResolveRootDirectory(Path.GetPathRoot(Production)!, Production));
    }
}
