using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZupuPaiBan.Data;

public sealed record OfflineBackupManifest(int FormatVersion, string DatabaseEntry, long DatabaseBytes,
    string DatabaseSha256, DateTimeOffset CreatedUtc, bool IncludesAttachments, bool IncludesConfiguration);

/// <summary>仅数据库的版本化备份；不实现恢复，附件和配置明确不包含。</summary>
public static class OfflineBackupPackageService
{
    private const string ManifestEntry = "manifest.json";
    private const string DatabaseEntry = "database/pujiang.db";
    private const long MaxDatabaseBytes = 2L * 1024 * 1024 * 1024;

    public static async Task<OfflineBackupManifest> CreateAsync(string sourcePath, string packagePath, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(sourcePath) || !Path.IsPathFullyQualified(packagePath))
            throw new InvalidOperationException("数据库和备份包必须使用绝对路径。");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("源数据库不存在。", sourcePath);
        var directory = Path.GetDirectoryName(packagePath)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("备份目标目录不存在。");
        var unique = Guid.NewGuid().ToString("N");
        var snapshotPath = Path.Combine(directory, $".pujiang-{unique}.db.tmp");
        var tempPackage = Path.Combine(directory, $".pujiang-{unique}.zip.tmp");
        var ownsSnapshot = false;
        var ownsTempPackage = false;
        try
        {
            await DatabaseSnapshotService.CreateAsync(sourcePath, snapshotPath, cancellationToken);
            ownsSnapshot = true;
            var length = new FileInfo(snapshotPath).Length;
            if (length <= 0 || length > MaxDatabaseBytes) throw new InvalidDataException("数据库快照大小超出支持范围。");
            string digest;
            await using (var snapshot = File.OpenRead(snapshotPath))
                digest = Convert.ToHexString(await SHA256.HashDataAsync(snapshot, cancellationToken));
            var manifest = new OfflineBackupManifest(1, DatabaseEntry, length, digest, DateTimeOffset.UtcNow, false, false);
            await using (var stream = new FileStream(tempPackage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTempPackage = true;
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                await using (var entry = archive.CreateEntry(ManifestEntry).Open())
                    await JsonSerializer.SerializeAsync(entry, manifest, cancellationToken: cancellationToken);
                await using var dbEntry = archive.CreateEntry(DatabaseEntry, CompressionLevel.Optimal).Open();
                await using var snapshot = File.OpenRead(snapshotPath);
                await snapshot.CopyToAsync(dbEntry, cancellationToken);
            }
            await ValidateAsync(tempPackage, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPackage, packagePath, overwrite: false);
            return manifest;
        }
        finally
        {
            if (ownsSnapshot && File.Exists(snapshotPath)) File.Delete(snapshotPath);
            if (ownsTempPackage && File.Exists(tempPackage)) File.Delete(tempPackage);
        }
    }

    public static async Task<OfflineBackupManifest> ValidateAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await ValidateStreamAsync(stream, cancellationToken);
    }

    internal static async Task<OfflineBackupManifest> ValidateStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count != 2 || archive.Entries.Count(e => e.FullName == ManifestEntry) != 1 ||
            archive.Entries.Count(e => e.FullName == DatabaseEntry) != 1)
            throw new InvalidDataException("备份包内容不符合版本 1 格式。");
        var manifestEntry = archive.GetEntry(ManifestEntry)!;
        if (manifestEntry.Length > 65536) throw new InvalidDataException("备份清单过大。");
        OfflineBackupManifest manifest;
        try
        {
            await using var input = manifestEntry.Open();
            manifest = await JsonSerializer.DeserializeAsync<OfflineBackupManifest>(input, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("备份清单为空。");
        }
        catch (JsonException ex) { throw new InvalidDataException("备份清单无效。", ex); }
        var db = archive.GetEntry(DatabaseEntry)!;
        if (manifest.FormatVersion != 1 || manifest.DatabaseEntry != DatabaseEntry || manifest.IncludesAttachments ||
            manifest.IncludesConfiguration || manifest.DatabaseBytes <= 0 || manifest.DatabaseBytes > MaxDatabaseBytes ||
            db.Length != manifest.DatabaseBytes || manifest.DatabaseSha256?.Length != 64)
            throw new InvalidDataException("备份清单的版本、范围或数据库元数据无效。");
        await using var dbStream = db.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long readBytes = 0;
        int read;
        while ((read = await dbStream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            readBytes += read;
            if (readBytes > manifest.DatabaseBytes) throw new InvalidDataException("数据库快照超出清单大小。");
            hash.AppendData(buffer, 0, read);
        }
        if (readBytes != manifest.DatabaseBytes) throw new InvalidDataException("数据库快照大小与清单不一致。");
        var actualHash = Convert.ToHexString(hash.GetHashAndReset());
        if (!string.Equals(actualHash, manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("数据库快照校验和不匹配。");
        return manifest;
    }
}
