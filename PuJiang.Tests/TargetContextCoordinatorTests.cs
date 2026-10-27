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

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Drain_waits_for_outer_operation_and_allows_nested_viewmodel_work_but_rejects_new_work()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").Options;
        var coordinator = new TargetContextCoordinator(options);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new GenealogySelectionViewModel(coordinator.CreateContext, coordinator) { NewName = "虚构嵌套族谱" };

        var work = Task.Run(async () =>
        {
            using var operation = coordinator.EnterOperation();
            entered.SetResult();
            await proceed.Task;
            await viewModel.CreateAsync(); // Creates, reloads and selects using nested operations and contexts.
        });
        await entered.Task;
        var drain = coordinator.DrainAsync();
        Assert.False(drain.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.LoadAsync());
        proceed.SetResult();
        await work;
        await drain;

        Assert.Equal("虚构嵌套族谱", Assert.Single(viewModel.Genealogies).Name);
        Assert.Equal("虚构嵌套族谱", viewModel.SelectedGenealogy?.Name);
        Assert.Throws<InvalidOperationException>(() => coordinator.EnterOperation());
        await using var verify = database.CreateContext();
        Assert.Equal("虚构嵌套族谱", (await verify.Genealogies.SingleAsync()).Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Drain_waits_until_outer_operation_ends_even_without_an_open_context()
    {
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite("Data Source=:memory:").Options;
        var coordinator = new TargetContextCoordinator(options);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = Task.Run(async () =>
        {
            using var operation = coordinator.EnterOperation();
            entered.SetResult();
            await proceed.Task;
        });
        await entered.Task;
        var drain = coordinator.DrainAsync();
        Assert.False(drain.IsCompleted);
        proceed.SetResult();
        await work;
        await drain;
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Drain_from_inside_an_operation_is_rejected_without_closing_the_gate()
    {
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite("Data Source=:memory:").Options;
        var coordinator = new TargetContextCoordinator(options);
        using (coordinator.EnterOperation())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DrainAsync());
        }
        await coordinator.DrainAsync();
    }
}
