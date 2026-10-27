using System.Text.Json;

namespace PuJiang.Tests;

// 测试专用：记录只能指导人工检查，绝不依据记录自动替换或删除文件。
internal sealed record RestoreJournalRecord(int Version, string OperationId, string Phase);
internal sealed record RestoreJournalInspection(string Status, string Message, IReadOnlyList<string> Evidence);

internal static class RestoreJournalPrototype
{
    public const string JournalName = "restore-journal.json";
    private static readonly string[] Phases = ["prepared", "replace_pending", "replaced"];

    public static void Write(TestDatabase target, RestoreJournalRecord record, Action? afterFlush = null)
    {
        Validate(record);
        var root = Path.GetDirectoryName(target.DatabasePath)!;
        var destination = Path.Combine(root, JournalName);
        var temporary = Path.Combine(root, $"journal-{Guid.NewGuid():N}.tmp");
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, record);
            file.Flush(flushToDisk: true);
        }
        afterFlush?.Invoke();
        // 同目录替换仅用于实验；目录元数据落盘和断电可靠性尚未验证。
        File.Move(temporary, destination, overwrite: true);
    }

    public static RestoreJournalInspection Inspect(TestDatabase target)
    {
        var root = Path.GetDirectoryName(target.DatabasePath)!;
        var journal = Path.Combine(root, JournalName);
        var pendingWrites = Directory.GetFiles(root, "journal-*.tmp");
        if (!File.Exists(journal))
        {
            var residue = Directory.GetFiles(root, "staged-*.db").Concat(Directory.GetFiles(root, "rollback-*.db"))
                .Concat(Directory.GetFiles(root, "journal-*.tmp")).ToArray();
            return residue.Length == 0 ? new("none", "未发现恢复记录。", residue) :
                new("unknown", "缺少恢复记录但存在残留文件；停止并人工检查。", residue);
        }
        if (pendingWrites.Length != 0)
            return new("unknown", "恢复记录存在未完成的写入；保留全部文件并人工检查。", [journal, .. pendingWrites]);
        RestoreJournalRecord? record;
        try
        {
            using var input = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > 4096) throw new InvalidDataException("记录过大。");
            record = JsonSerializer.Deserialize<RestoreJournalRecord>(input);
            Validate(record);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException)
        {
            return new("unknown", "恢复记录损坏或不支持；停止并人工检查。", [journal]);
        }
        var id = record!.OperationId;
        var staged = Path.Combine(root, $"staged-{id}.db");
        var rollback = Path.Combine(root, $"rollback-{id}.db");
        var snapshot = Path.Combine(target.BackupDirectory, $"before-{id}.db");
        var evidence = new[] { target.DatabasePath, staged, rollback, snapshot }.Where(File.Exists).ToArray();
        var status = record.Phase switch
        {
            "prepared" when File.Exists(target.DatabasePath) && File.Exists(staged) && !File.Exists(rollback) => "before_replace",
            "replace_pending" => "uncertain",
            "replaced" when File.Exists(target.DatabasePath) && File.Exists(rollback) && !File.Exists(staged) => "after_replace",
            _ => "unknown"
        };
        return new(status, $"恢复阶段：{record.Phase}；状态：{status}。保留全部文件并人工检查；不自动恢复。", evidence);
    }

    private static void Validate(RestoreJournalRecord? record)
    {
        if (record is null || record.Version != 1 || record.OperationId is null ||
            !Guid.TryParseExact(record.OperationId, "N", out _) || !Phases.Contains(record.Phase, StringComparer.Ordinal))
            throw new InvalidDataException("恢复记录无效或版本不受支持。");
    }
}
