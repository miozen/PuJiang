using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ZupuPaiBan.Data;

/// <summary>Single-connection, read-only, version-bound page reader. It is not a restore mechanism.</summary>
public sealed class ProjectionReadSession : IAsyncDisposable
{
    public sealed record PersonRow(Guid Id, string Name, string? RecordedGeneration, string? RecordedBirthOrder);
    public sealed record PersonPage(IReadOnlyList<PersonRow> Items, Guid? NextCursor);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private readonly CancellationTokenSource _closing = new();
    private readonly Guid _token = Guid.NewGuid();
    private readonly Guid _genealogyId;
    private readonly ZupuDbContext _lease;
    private readonly FileStream _fileLease;
    private readonly SqliteConnection _connection;
    private readonly long _version;
    private IDisposable? _registration;
    private int _closed;
    private volatile bool _invalid;
    internal Func<CancellationToken, Task>? AfterVersionChecked { get; set; }
    internal Action<SqliteConnection>? BeforePeopleQueryForTest { get; set; }

    private ProjectionReadSession(Guid genealogyId, ZupuDbContext lease, FileStream fileLease, SqliteConnection connection, long version)
        => (_genealogyId, _lease, _fileLease, _connection, _version) = (genealogyId, lease, fileLease, connection, version);

    public Guid Token => Volatile.Read(ref _closed) == 0 && !_invalid ? _token : throw new InvalidOperationException("只读会话已失效，请重新打开。");

    public static async Task<ProjectionReadSession> OpenAsync(TargetContextCoordinator coordinator, Guid genealogyId, CancellationToken cancellationToken = default)
    {
        if (genealogyId == Guid.Empty) throw new ArgumentException("族谱 ID 无效。", nameof(genealogyId));
        var lease = coordinator.CreateContext();
        FileStream? fileLease = null;
        SqliteConnection? connection = null;
        ProjectionReadSession? session = null;
        try
        {
            var source = new SqliteConnectionStringBuilder(lease.Database.GetConnectionString()).DataSource;
            if (!Path.IsPathFullyQualified(source) || !File.Exists(source) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("只读会话需要现存的本地数据库文件。");
            if (new DriveInfo(Path.GetPathRoot(source)!).DriveType == DriveType.Network)
                throw new InvalidOperationException("只读会话暂不支持网络数据库。");
            for (var directory = Directory.GetParent(Path.GetFullPath(source)); directory is not null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("只读会话暂不支持链接目录。");
            fileLease = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await connection.OpenAsync(cancellationToken);
            var version = await ReadVersionAsync(connection, null, cancellationToken);
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(*) FROM Genealogies WHERE Id = $id";
                check.Parameters.AddWithValue("$id", genealogyId.ToString().ToUpperInvariant());
                if (Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("族谱不存在。");
            }
            if (await ReadVersionAsync(connection, null, cancellationToken) != version) throw new InvalidOperationException("数据库已变化，请重新打开。");
            session = new ProjectionReadSession(genealogyId, lease, fileLease, connection, version);
            session._registration = coordinator.RegisterLongLivedCloser(() => session.DisposeAsync().AsTask());
            return session;
        }
        catch
        {
            if (session is not null) await session.DisposeAsync();
            else
            {
                try { if (connection is not null) await connection.DisposeAsync(); }
                finally { try { if (fileLease is not null) await fileLease.DisposeAsync(); } finally { await lease.DisposeAsync(); } }
            }
            throw;
        }
    }

    public async Task<PersonPage> ReadPeopleAsync(Guid token, Guid? after, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        EnsureValid(token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);
        try { await _gate.WaitAsync(linked.Token); }
        catch { _invalid = true; throw; }
        try
        {
            EnsureValid(token);
            await using var tx = _connection.BeginTransaction(deferred: true);
            if (await ReadVersionAsync(_connection, tx, linked.Token) != _version) throw new InvalidOperationException("数据库已变化，请重新打开。");
            if (AfterVersionChecked is not null) await AfterVersionChecked(linked.Token);
            BeforePeopleQueryForTest?.Invoke(_connection);
            var rows = new List<PersonRow>();
            await using (var command = _connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = "SELECT Id, Name, RecordedGeneration, RecordedBirthOrder FROM People WHERE GenealogyId = $genealogy AND ($after IS NULL OR Id > $after)" +
                    (BeforePeopleQueryForTest is null ? "" : " AND projection_test_wait(Id) = 1") + " ORDER BY Id LIMIT $limit";
                command.Parameters.AddWithValue("$genealogy", _genealogyId.ToString().ToUpperInvariant());
                command.Parameters.AddWithValue("$after", after is Guid cursor ? cursor.ToString().ToUpperInvariant() : DBNull.Value);
                command.Parameters.AddWithValue("$limit", limit + 1);
                await using var reader = await command.ExecuteReaderAsync(linked.Token);
                while (await reader.ReadAsync(linked.Token)) rows.Add(new PersonRow(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
            await tx.CommitAsync(linked.Token);
            if (await ReadVersionAsync(_connection, null, linked.Token) != _version) throw new InvalidOperationException("数据库已变化，请重新打开。");
            linked.Token.ThrowIfCancellationRequested();
            EnsureValid(token);
            var hasMore = rows.Count > limit;
            if (hasMore) rows.RemoveAt(rows.Count - 1);
            return new PersonPage(rows.AsReadOnly(), hasMore ? rows[^1].Id : null);
        }
        catch { _invalid = true; throw; }
        finally { _gate.Release(); }
    }

    private void EnsureValid(Guid token)
    {
        if (_closed != 0 || _invalid || token != _token || _connection.State != ConnectionState.Open)
            throw new InvalidOperationException("只读会话已失效，请重新打开。");
    }
    private static async Task<long> ReadVersionAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand(); cmd.Transaction = transaction; cmd.CommandText = "PRAGMA data_version";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
    }
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _closed, 1);
        _closing.Cancel();
        await _gate.WaitAsync();
        try
        {
            try { await _connection.DisposeAsync(); }
            finally { try { await _fileLease.DisposeAsync(); } finally { await _lease.DisposeAsync(); } }
            // Retain a failed closer so a later drain observes the disposal failure.
            _registration?.Dispose();
        }
        finally { _gate.Release(); }
    }
}
