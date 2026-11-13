using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
using PuJiang.V1.Persistence;
using Xunit;

namespace PuJiang.Tests;

public sealed class V1FirstRootTests
{
    private static async Task<(IsolatedTestDatabase db, Guid g, Guid l)> CreateAsync()
    {
        var database = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using var db = database.OpenContext(); await db.Database.EnsureCreatedAsync();
        db.Genealogies.Add(new(g, "项目", "冯")); db.Lineages.Add(new(l, g, "谱系")); await db.SaveChangesAsync();
        return (database, g, l);
    }
    [Fact]
    public async Task Create_replay_reopen_and_projection_preserve_both_ids()
    {
        var (db, g, l) = await CreateAsync(); var app = new FirstRootApplication(new IsolatedFirstRootStore(db));
        Assert.Empty((await app.ReadAsync(g, l)).Nodes);
        var command = new AddFirstRoot(Guid.NewGuid(), g, l, RootNameMode.Empty, "");
        var created = await app.ExecuteAsync(command); var replay = await app.ExecuteAsync(command);
        Assert.False(created.Replayed); Assert.True(replay.Replayed);
        Assert.Equal(created.PersonId, replay.PersonId); Assert.Equal(created.PlacementId, replay.PlacementId);
        var read = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ReadAsync(g, l);
        var node = Assert.Single(read.Nodes); Assert.Equal(created.PersonId, node.PersonId); Assert.Equal(created.PlacementId, node.PlacementId); Assert.Equal("", node.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.ExecuteAsync(command with { NameMode = RootNameMode.Named, Name = "另一个" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.ExecuteAsync(command with { OperationId = Guid.NewGuid() }));
    }
    [Fact]
    public async Task Invalid_input_and_foreign_target_do_not_write()
    {
        var (db, g, l) = await CreateAsync(); var app = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var command = new AddFirstRoot(Guid.NewGuid(), g, l, RootNameMode.Named, "");
        await Assert.ThrowsAsync<ArgumentException>(() => app.ExecuteAsync(command));
        await Assert.ThrowsAsync<ArgumentException>(() => app.ExecuteAsync(command with { NameMode = RootNameMode.Empty, Name = "非空" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.ExecuteAsync(command with { GenealogyId = Guid.NewGuid(), Name = "姓名" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.ReadAsync(Guid.NewGuid(), l));
        Assert.Empty((await app.ReadAsync(g, l)).Nodes);
    }
    [Fact]
    public async Task Placement_failure_rolls_back_person_and_receipt_then_retry_succeeds()
    {
        var (db, g, l) = await CreateAsync(); var app = new FirstRootApplication(new IsolatedFirstRootStore(db));
        await using (var context = db.OpenContext()) await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailPlacement BEFORE INSERT ON Placements BEGIN SELECT RAISE(ABORT, 'injected'); END");
        var command = new AddFirstRoot(Guid.NewGuid(), g, l, RootNameMode.Named, "冯甲");
        await Assert.ThrowsAsync<DbUpdateException>(() => app.ExecuteAsync(command));
        await using (var context = db.OpenContext())
        {
            Assert.Empty(await context.People.ToListAsync()); Assert.Empty(await context.Placements.ToListAsync());
            Assert.Equal(0L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM FirstRootReceipts").SingleAsync());
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER FailPlacement");
        }
        var result = await app.ExecuteAsync(command); Assert.False(result.Replayed); Assert.Single((await app.ReadAsync(g,l)).Nodes);
    }
    [Fact]
    public async Task Concurrent_duplicate_is_created_once()
    {
        var (db, g, l) = await CreateAsync(); var app = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var command = new AddFirstRoot(Guid.NewGuid(), g, l, RootNameMode.Named, "冯甲");
        var results = await Task.WhenAll(app.ExecuteAsync(command), app.ExecuteAsync(command));
        Assert.Equal(results[0].PersonId, results[1].PersonId); Assert.Equal(results[0].PlacementId, results[1].PlacementId);
        Assert.Single(results, x => x.Replayed); Assert.Single((await app.ReadAsync(g, l)).Nodes);
    }
}
