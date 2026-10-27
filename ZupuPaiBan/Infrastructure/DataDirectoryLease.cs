using System.IO;

namespace ZupuPaiBan.Infrastructure;

/// <summary>在应用生命周期内独占一个数据目录；锁文件本身不会被删除。</summary>
public sealed class DataDirectoryLease : IDisposable
{
    private readonly FileStream _stream;

    private DataDirectoryLease(FileStream stream) => _stream = stream;

    public static DataDirectoryLease Acquire(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        var lockPath = Path.Combine(rootDirectory, ".pujiang.lock");
        try
        {
            return new DataDirectoryLease(new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
        {
            throw new InvalidOperationException("此数据目录已由另一个谱匠实例使用，请先关闭该实例。", ex);
        }
    }

    public void Dispose() => _stream.Dispose();
}
