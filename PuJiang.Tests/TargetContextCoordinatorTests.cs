using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

public class TargetContextCoordinatorTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_contexts_are_drained_and_new_operations_are_rejected()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").Options;
        var coordinator = new TargetContextCoordinator(options);
        var viewModel = new GenealogySelectionViewModel(coordinator.CreateContext) { NewName = "虚构测试族谱" };
        await viewModel.CreateAsync();
        Assert.Single(viewModel.Genealogies);

        var context = coordinator.CreateContext();
        Assert.Single(await context.Genealogies.ToListAsync());
        var drain = coordinator.DrainAsync();
        Assert.False(drain.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => coordinator.CreateContext());
        await context.DisposeAsync();
        await drain;
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.LoadAsync());
        await using var verify = database.CreateContext();
        Assert.Equal("虚构测试族谱", (await verify.Genealogies.SingleAsync()).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancelled_drain_remains_closed_after_existing_context_is_disposed()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").Options;
        var coordinator = new TargetContextCoordinator(options);
        await using var context = coordinator.CreateContext();
        using var cancellation = new CancellationTokenSource();
        var drain = coordinator.DrainAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain);
        await context.DisposeAsync();
        Assert.Throws<InvalidOperationException>(() => coordinator.CreateContext());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DrainAsync());
    }
}
