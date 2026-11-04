using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

// Prototype only: an isolated SQLite connection must stay alive across every version check.
public class ProjectionReadVersionPrototypeTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Other_connection_commit_invalidates_and_rollback_does_not()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var readOptions = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        var writeOptions = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Pooling = false }.ToString();
        await using var reader = new SqliteConnection(readOptions);
        await using var writer = new SqliteConnection(writeOptions);
        await reader.OpenAsync(); await writer.OpenAsync();
        await Execute(writer, "CREATE TABLE ProjectionVersionProbe (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
        await Execute(writer, "INSERT INTO ProjectionVersionProbe VALUES (1, 'one'), (2, 'two')");
        var firstVersion = await Version(reader);
        Assert.Equal("one", await FirstPage(reader, firstVersion));
        await Execute(writer, "UPDATE ProjectionVersionProbe SET Value = 'changed' WHERE Id = 2");
        await Assert.ThrowsAsync<InvalidOperationException>(() => FirstPage(reader, firstVersion));
        var nextVersion = await Version(reader);
        Assert.Equal("one", await FirstPage(reader, nextVersion));
        await using (var transaction = writer.BeginTransaction())
        {
            await Execute(writer, "UPDATE ProjectionVersionProbe SET Value = 'rolled back' WHERE Id = 1", transaction);
            await transaction.RollbackAsync();
        }
        Assert.Equal(nextVersion, await Version(reader));
        Assert.Equal("one", await FirstPage(reader, nextVersion));
        await Assert.ThrowsAsync<SqliteException>(() => Execute(reader, "UPDATE ProjectionVersionProbe SET Value = 'forbidden'"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Wal_read_transaction_keeps_page_stable_during_commit_and_next_batch_detects_it()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var writeOptions = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Pooling = false }.ToString();
        await using var writer = new SqliteConnection(writeOptions); await writer.OpenAsync();
        await Execute(writer, "PRAGMA journal_mode = WAL");
        await Execute(writer, "CREATE TABLE ProjectionVersionProbe (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
        await Execute(writer, "INSERT INTO ProjectionVersionProbe VALUES (1, 'old')");
        var options = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var reader = new SqliteConnection(options); await reader.OpenAsync();
        var baseline = await Version(reader);
        await using (var transaction = reader.BeginTransaction(deferred: true))
        {
            // An actual table read establishes the snapshot before the version check.
            await using var probe = reader.CreateCommand(); probe.Transaction = transaction;
            probe.CommandText = "SELECT Value FROM ProjectionVersionProbe WHERE Id = 1";
            Assert.Equal("old", await probe.ExecuteScalarAsync());
            Assert.Equal(baseline, await Version(reader, transaction));
            await Execute(writer, "UPDATE ProjectionVersionProbe SET Value = 'new' WHERE Id = 1");
            Assert.Equal("old", await probe.ExecuteScalarAsync());
            await transaction.CommitAsync();
        }
        Assert.NotEqual(baseline, await Version(reader));
        await Assert.ThrowsAsync<InvalidOperationException>(() => FirstPage(reader, baseline));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Commits_to_each_actual_projection_table_invalidate_the_reader()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using (var context = database.CreateContext())
        {
            var genealogy = new Genealogy { Name = "虚构谱" };
            var parent = new Person { GenealogyId = genealogy.Id, Name = "父" };
            var child = new Person { GenealogyId = genealogy.Id, Name = "子" };
            context.Genealogies.Add(genealogy);
            context.People.AddRange(parent, child);
            context.FamilyBranches.Add(new FamilyBranch { GenealogyId = genealogy.Id, Name = "房" });
            var relationship = new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent };
            context.PersonRelationships.Add(relationship);
            context.FamilyUnions.Add(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = parent.Id });
            var chart = new LineageChart { GenealogyId = genealogy.Id, Name = "图", Revision = 1 };
            chart.Choices.Add(new LineageChartChoice { PersonId = child.Id, ParentId = parent.Id, RelationshipId = relationship.Id,
                Kind = relationship.Kind, PersonName = child.Name, ParentName = parent.Name });
            context.LineageCharts.Add(chart);
            await context.SaveChangesAsync();
        }
        var options = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var reader = new SqliteConnection(options); await reader.OpenAsync();
        await using var writer = database.CreateContext();
        foreach (var sql in new[] { "UPDATE Genealogies SET Name = Name || 'a'", "UPDATE FamilyBranches SET Name = Name || 'a'",
            "UPDATE People SET Name = Name || 'a'", "UPDATE PersonRelationships SET Note = 'a'",
            "UPDATE FamilyUnions SET Note = 'a'", "UPDATE LineageCharts SET Name = Name || 'a'",
            "UPDATE LineageChartChoices SET PersonName = PersonName || 'a'" })
        {
            var before = await Version(reader);
            Assert.True(await writer.Database.ExecuteSqlRawAsync(sql) > 0);
            Assert.NotEqual(before, await Version(reader));
        }
    }

    private static async Task<long> Version(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "PRAGMA data_version";
        return (long)(await command.ExecuteScalarAsync())!;
    }
    private static async Task<string> FirstPage(SqliteConnection connection, long expected)
    {
        await using var transaction = connection.BeginTransaction(deferred: true);
        if (await Version(connection, transaction) != expected) throw new InvalidOperationException("stale projection");
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT Value FROM ProjectionVersionProbe ORDER BY Id LIMIT 1";
        var value = (string)(await command.ExecuteScalarAsync())!;
        await transaction.CommitAsync();
        return value;
    }
    private static async Task Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
