using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PuJiang.V1.Persistence;

// This capability only accepts an explicitly configured test root, never a database path.
public static class ControlledFictionalSamples
{
    private const string EnvironmentName = "PUJIANG_TEST_DATA_ROOT";
    private sealed record Marker(int Version, Guid SampleId, Guid GenealogyId, Guid LineageId);

    private static (string Path, ControlledPathLease Lease) Root()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentName);
        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured))
            throw new InvalidOperationException("请先配置独立测试数据目录，不能使用正式数据目录。");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
        var production = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PuJiang")));
        static bool Within(string a, string b) { var relative = Path.GetRelativePath(b, a); return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal); }
        if (Within(root, production) || Within(production, root)) throw new InvalidOperationException("测试目录不得与正式数据目录重叠。");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("独立测试目录不存在。");
        var lease = new ControlledPathLease();
        try
        {
            lease.PinDirectories(root);
            var resolvedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(lease.FinalPath()));
            if (Within(resolvedRoot, production) || Within(production, resolvedRoot)) throw new InvalidOperationException("测试目录不得与正式数据目录重叠。");
            var samples = Path.Combine(root, "V1FictionalSamples");
            CheckPath(samples); Directory.CreateDirectory(samples); lease.PinDirectories(samples);
            return (samples, lease);
        }
        catch { lease.Dispose(); throw; }
    }

    private static void CheckPath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("隔离样本路径不能包含链接或重解析点。");
        }
    }

    private static string FileFor(string root, Guid id) => Path.Combine(root, id.ToString("N") + ".sqlite");
    private static string Pointer(string root) => Path.Combine(root, "latest.json");

    public static async Task<IsolatedSample> OpenLatestOrCreateAsync()
    {
        var (root, acquiredLease) = Root(); ControlledPathLease? lease = acquiredLease;
        try
        {
        var pointer = Pointer(root); CheckPath(pointer);
        if (!File.Exists(pointer))
        {
            if (Directory.EnumerateFileSystemEntries(root).Any()) throw new InvalidDataException("样本登记缺失，已保留目录中的原始文件；可手动新建虚构样本。");
            return await CreateAsync();
        }
        using var pointerLease = new ControlledPathLease(); pointerLease.PinFile(pointer, allowWrite: false);
        var marker = JsonSerializer.Deserialize<Marker>(await File.ReadAllTextAsync(pointer)) ?? throw new InvalidDataException("样本登记无效。");
        if (marker.Version != 1 || marker.SampleId == Guid.Empty || marker.GenealogyId == Guid.Empty || marker.LineageId == Guid.Empty)
            throw new InvalidDataException("样本登记版本或身份无效。");
        var path = FileFor(root, marker.SampleId); CheckPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("上次虚构样本文件不存在，原登记已保留。", path);
        lease.PinFile(path, allowWrite: true);
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using (var connection = new SqliteConnection(cs))
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check";
            if (!string.Equals((string?)await cmd.ExecuteScalarAsync(), "ok", StringComparison.Ordinal))
                throw new InvalidDataException("样本数据库检查失败，已保留原文件。");
            cmd.CommandText = "SELECT Version, SampleId, GenealogyId, LineageId FROM FictionalSampleIdentity";
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || reader.GetInt32(0) != marker.Version || reader.GetString(1) != marker.SampleId.ToString("D") || reader.GetString(2) != marker.GenealogyId.ToString("D") || reader.GetString(3) != marker.LineageId.ToString("D") || await reader.ReadAsync())
                throw new InvalidDataException("样本身份不匹配，已保留原文件。");
        }
        CheckPath(path);
        var db = IsolatedTestDatabase.FromControlledFile(path, lease);
        await using var context = db.OpenContext();
        var genealogy = await context.Genealogies.AsNoTracking().SingleAsync(x => x.Id == marker.GenealogyId);
        if (!await context.Lineages.AsNoTracking().AnyAsync(x => x.Id == marker.LineageId && x.GenealogyId == marker.GenealogyId)) throw new InvalidDataException("样本初始谱系不存在。");
        lease = null; // the database now owns the pinned path for its lifetime
        return new IsolatedSample(db, marker.GenealogyId, marker.LineageId, genealogy.Surname ?? "");
        }
        finally { lease?.Dispose(); }
    }

    public static async Task<IsolatedSample> CreateAsync()
    {
        var (root, acquiredLease) = Root(); ControlledPathLease? lease = acquiredLease;
        var id = Guid.NewGuid(); var path = FileFor(root, id);
        try
        {
            CheckPath(path);
            using (new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite)) { lease.PinFile(path, allowWrite: true); }
            var db = IsolatedTestDatabase.FromControlledFile(path, lease);
            var sample = await IsolatedSample.InitializeAsync(db);
            var marker = new Marker(1, id, sample.GenealogyId, sample.LineageId);
            await using (var context = db.OpenContext())
            {
                await context.Database.ExecuteSqlRawAsync("CREATE TABLE FictionalSampleIdentity (Version INTEGER NOT NULL, SampleId TEXT NOT NULL, GenealogyId TEXT NOT NULL, LineageId TEXT NOT NULL)");
                await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO FictionalSampleIdentity VALUES ({marker.Version}, {marker.SampleId.ToString("D")}, {marker.GenealogyId.ToString("D")}, {marker.LineageId.ToString("D")})");
            }
            var pointer = Pointer(root); CheckPath(pointer);
            var temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".pending");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(marker));
            CheckPath(pointer); File.Move(temporary, pointer, true);
            lease = null;
            return sample;
        }
        catch (Exception error) { throw new IOException($"创建虚构样本失败，已保留文件 {path}；上次样本登记未替换。{error.Message}", error); }
        finally { lease?.Dispose(); }
    }
}
