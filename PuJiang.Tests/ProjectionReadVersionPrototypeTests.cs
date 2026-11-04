using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
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
    public async Task Wal_version_read_pins_snapshot_before_concurrent_commit()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var rw = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Pooling = false }.ToString();
        var ro = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var writer = new SqliteConnection(rw); await writer.OpenAsync();
        await Execute(writer, "PRAGMA journal_mode = WAL");
        await Execute(writer, "CREATE TABLE ProjectionVersionProbe (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
        await Execute(writer, "INSERT INTO ProjectionVersionProbe VALUES (1, 'old')");
        await using var reader = new SqliteConnection(ro); await reader.OpenAsync();
        var baseline = await Version(reader);
        await using var transaction = reader.BeginTransaction(deferred: true);
        Assert.Equal(baseline, await Version(reader, transaction));
        await Execute(writer, "UPDATE ProjectionVersionProbe SET Value = 'new' WHERE Id = 1");
        await using var command = reader.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT Value FROM ProjectionVersionProbe WHERE Id = 1";
        var value = (string)(await command.ExecuteScalarAsync())!;
        await transaction.CommitAsync();
        // On this SQLite build the version read in the transaction pinned the old snapshot.
        Assert.Equal("old", value);
        Assert.NotEqual(baseline, await Version(reader));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task File_lease_blocks_replacement_until_closed_on_windows()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var moved = database.DatabasePath + ".moved";
        await using (var lease = new FileStream(database.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var options = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Pooling = false }.ToString();
            await using var concurrent = new SqliteConnection(options); await concurrent.OpenAsync();
            await Execute(concurrent, "CREATE TABLE ProjectionLeaseProbe (Id INTEGER PRIMARY KEY)");
            Assert.ThrowsAny<IOException>(() => File.Move(database.DatabasePath, moved));
        }
        File.Move(database.DatabasePath, moved);
        File.Move(moved, database.DatabasePath);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Session_token_cannot_cross_dispose_or_new_connection_even_if_versions_match()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var options = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        var first = await PrototypeSession.Open(options);
        var token = await first.Token();
        await first.Read(token);
        await first.DisposeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.Read(token));
        await using var reopened = await PrototypeSession.Open(options);
        var newToken = await reopened.Token();
        // Equal PRAGMA numbers do not authorize a token issued by a different session.
        Assert.Equal(token.Version, newToken.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.Read(token));
        await reopened.Read(newToken);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Session_holds_coordinator_until_closed_and_cancellation_invalidates_token()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var contextOptions = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Pooling = false }.ToString()).Options;
        var coordinator = new TargetContextCoordinator(contextOptions);
        var lease = coordinator.CreateContext();
        var options = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var session = await PrototypeSession.Open(options, lease);
        var token = await session.Token();
        var draining = coordinator.DrainAsync();
        Assert.False(draining.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => coordinator.CreateContext());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Read(token, cancellation.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Read(token));
        await draining.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Session_rejects_second_page_after_change_and_does_not_reuse_old_token()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var rw = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Pooling = false }.ToString();
        var ro = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var writer = new SqliteConnection(rw); await writer.OpenAsync();
        await Execute(writer, "CREATE TABLE ProjectionVersionProbe (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
        await Execute(writer, "INSERT INTO ProjectionVersionProbe VALUES (1, 'old first'), (2, 'old second')");
        await using var oldSession = await PrototypeSession.Open(ro);
        var oldToken = await oldSession.Token();
        Assert.Equal("old first", await oldSession.Page(oldToken, 1));
        await Execute(writer, "UPDATE ProjectionVersionProbe SET Value = 'new second' WHERE Id = 2");
        await Assert.ThrowsAsync<InvalidOperationException>(() => oldSession.Page(oldToken, 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => oldSession.Token());
        await using var newSession = await PrototypeSession.Open(ro);
        await Assert.ThrowsAsync<InvalidOperationException>(() => newSession.Page(oldToken, 2));
        var newToken = await newSession.Token();
        Assert.Equal("new second", await newSession.Page(newToken, 2));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Session_discards_page_when_commit_occurs_after_version_check()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        var rw = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Pooling = false }.ToString();
        var ro = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var writer = new SqliteConnection(rw); await writer.OpenAsync();
        await Execute(writer, "PRAGMA journal_mode = WAL");
        await Execute(writer, "CREATE TABLE ProjectionVersionProbe (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
        await Execute(writer, "INSERT INTO ProjectionVersionProbe VALUES (1, 'old')");
        await using var session = await PrototypeSession.Open(ro);
        var token = await session.Token();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Page(token, 1,
            () => Execute(writer, "UPDATE ProjectionVersionProbe SET Value = 'new' WHERE Id = 1")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Page(token, 1));
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

    private readonly record struct SessionToken(Guid Owner, long Version);
    private sealed class PrototypeSession(SqliteConnection connection, ZupuDbContext? coordinatorLease, long initialVersion) : IAsyncDisposable
    {
        private readonly Guid _owner = Guid.NewGuid();
        private bool _closed;
        private bool _invalid;
        public static async Task<PrototypeSession> Open(string options, ZupuDbContext? coordinatorLease = null)
        {
            var connection = new SqliteConnection(options);
            await connection.OpenAsync();
            return new PrototypeSession(connection, coordinatorLease, await Version(connection));
        }
        public Task<SessionToken> Token() => _closed || _invalid
            ? Task.FromException<SessionToken>(new InvalidOperationException("stale session"))
            : Task.FromResult(new SessionToken(_owner, initialVersion));
        public async Task Read(SessionToken token, CancellationToken cancellationToken = default)
        {
            if (_closed || _invalid || token.Owner != _owner) throw new InvalidOperationException("stale session");
            if (cancellationToken.IsCancellationRequested)
            {
                await DisposeAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
            await using var tx = connection.BeginTransaction(deferred: true);
            if (await Version(connection, tx) != token.Version) { _invalid = true; throw new InvalidOperationException("stale version"); }
            await using var command = connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master";
            await command.ExecuteScalarAsync();
            await tx.CommitAsync();
        }
        public async Task<string> Page(SessionToken token, long id, Func<Task>? afterVersion = null)
        {
            if (_closed || _invalid || token.Owner != _owner) throw new InvalidOperationException("stale session");
            await using var tx = connection.BeginTransaction(deferred: true);
            if (await Version(connection, tx) != token.Version) { _invalid = true; throw new InvalidOperationException("stale version"); }
            if (afterVersion is not null) await afterVersion();
            await using var command = connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = "SELECT Value FROM ProjectionVersionProbe WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);
            var value = (string)(await command.ExecuteScalarAsync())!;
            await tx.CommitAsync();
            if (await Version(connection) != token.Version) { _invalid = true; throw new InvalidOperationException("stale version"); }
            return value;
        }
        public async ValueTask DisposeAsync()
        {
            if (_closed) return;
            _closed = true;
            try { await connection.DisposeAsync(); }
            finally { if (coordinatorLease is not null) await coordinatorLease.DisposeAsync(); }
        }
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
