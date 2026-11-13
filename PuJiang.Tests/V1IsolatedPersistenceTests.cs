using Xunit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Persistence;

namespace PuJiang.Tests;

public sealed class V1IsolatedPersistenceTests
{
    [Fact]
    public async Task Empty_lineage_and_multiple_empty_named_roots_round_trip()
    {
        var database = IsolatedTestDatabase.CreateNew();
        await using var db = database.OpenContext(); await db.Database.EnsureCreatedAsync();
        var g = Guid.NewGuid(); var l = Guid.NewGuid(); var p1 = Guid.NewGuid(); var p2 = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        db.Genealogies.Add(new(g, "项目", null)); db.Lineages.Add(new(l, g, "谱系")); await db.SaveChangesAsync();
        Assert.Empty(await db.People.ToListAsync());
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.People.AddRange(new Person(p1, l, ""), new Person(p2, l, ""));
            db.Placements.AddRange(new Placement(a, l, p1), new Placement(b, l, p2));
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.People.CountAsync(p => p.LineageId == l && p.Name == ""));
        Assert.Equal(new[] { a, b }.Order(), (await db.Placements.Select(x => x.Id).ToListAsync()).Order());
        Assert.Equal(p1, (await db.Placements.SingleAsync(x => x.Id == a)).PersonId);
    }

    [Fact]
    public async Task Invalid_cross_lineage_placement_rolls_back_person_and_position()
    {
        var database = IsolatedTestDatabase.CreateNew();
        await using var db = database.OpenContext(); await db.Database.EnsureCreatedAsync();
        var g = Guid.NewGuid(); var l1 = Guid.NewGuid(); var l2 = Guid.NewGuid(); var p = Guid.NewGuid();
        db.Genealogies.Add(new(g, "项目", null)); db.Lineages.AddRange(new Lineage(l1, g, "一"), new Lineage(l2, g, "二")); await db.SaveChangesAsync();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.People.Add(new(p, l1, "")); await db.SaveChangesAsync();
            db.Placements.Add(new(Guid.NewGuid(), l2, p));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Empty(await db.People.ToListAsync()); Assert.Empty(await db.Placements.ToListAsync());
    }
    [Fact]
    public async Task File_database_reopens_with_stable_identity_and_enforces_database_constraints()
    {
        var database = IsolatedTestDatabase.CreateNew();
        var g = Guid.NewGuid(); var l = Guid.NewGuid(); var p = Guid.NewGuid(); var placement = Guid.NewGuid();
        await using (var db = database.OpenContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Genealogies.Add(new(g, "项目", "冯")); db.Lineages.Add(new(l, g, "谱系"));
            await db.SaveChangesAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            db.People.Add(new(p, l, "")); db.Placements.Add(new(placement, l, p));
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        await using (var reopened = database.OpenContext())
        {
            Assert.Equal(p, (await reopened.Placements.SingleAsync(x => x.Id == placement)).PersonId);
            Assert.Equal("", (await reopened.People.SingleAsync(x => x.Id == p)).Name);
            await Assert.ThrowsAsync<SqliteException>(() => reopened.Database.ExecuteSqlRawAsync("INSERT INTO Genealogies (Id, Name) VALUES ('00000000-0000-0000-0000-000000000000', 'invalid')"));
            await Assert.ThrowsAsync<SqliteException>(() => reopened.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Placements (Id, LineageId, PersonId) VALUES ({Guid.NewGuid()}, {l}, {Guid.NewGuid()})"));
            Assert.Equal(1, await reopened.Placements.CountAsync());
        }
    }
}
