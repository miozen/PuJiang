using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PuJiang.V1.Persistence;

// Windows handles pin every path component against rename/delete while SQLite uses the sample.
internal sealed class ControlledPathLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles = new();
    private const uint GenericRead = 0x80000000, ShareRead = 1, ShareWrite = 2, OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000, ReparsePoint = 0x400;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, System.Text.StringBuilder path, uint length, uint flags);

    internal void PinDirectories(string directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("受控虚构样本仅支持 Windows。");
        var parents = new Stack<string>();
        for (var current = Path.GetFullPath(directory); current is not null; current = Path.GetDirectoryName(current)) parents.Push(current);
        while (parents.Count > 0) Pin(parents.Pop(), directory: true, allowWrite: true);
    }

    internal void PinFile(string path, bool allowWrite) => Pin(path, directory: false, allowWrite);

    private void Pin(string path, bool directory, bool allowWrite)
    {
        var handle = CreateFileW(path, GenericRead, ShareRead | (allowWrite ? ShareWrite : 0), IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException($"无法锁定受控样本路径：{path}", new Win32Exception(error)); }
        try
        {
            if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if ((info.Attributes & ReparsePoint) != 0 || (!directory && info.Links != 1)) throw new IOException("受控样本路径不能使用链接或硬链接。");
            _handles.Add(handle);
        }
        catch { handle.Dispose(); throw; }
    }

    internal string FinalPath()
    {
        var builder = new System.Text.StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(_handles[^1], builder, (uint)builder.Capacity, 0);
        if (length == 0 || length >= builder.Capacity) throw new IOException("无法确认受控样本目录的实际路径。");
        var value = builder.ToString();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) throw new IOException("受控样本不支持网络路径。");
        return value.StartsWith(@"\\?\", StringComparison.Ordinal) ? value[4..] : value;
    }

    public void Dispose() { for (var i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose(); _handles.Clear(); }
}
