namespace PuJiang.Tests;

// 供集成测试作为独立进程运行；不允许附着正式用户数据目录。
internal static class RestoreWorkerProgram
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--wpf-lifecycle")
            return await WpfLifecycleWorker.RunAsync(args[1]);
        if (args.Length == 2 && args[0] == "--wpf-displayed")
            return await WpfDisplayedWorker.RunAsync(args[1]);
        if (args.Length == 2 && args[0] == "--wpf-inflight")
            return await WpfInflightWorker.RunAsync(args[1]);
        if (args.Length != 3) return 2;
        var target = new TestDatabase(args[0]);
        var sourceDirectory = new TestDatabase(args[1]);
        var package = Path.Combine(sourceDirectory.BackupDirectory, "source.zip");
        var prototype = new IsolatedRestorePrototype(target, () => Task.CompletedTask, stage =>
        {
            if (stage != args[2]) return;
            Console.WriteLine($"READY:{stage}");
            Console.Out.Flush();
            Console.ReadLine();
        });
        await prototype.RunAsync(package);
        return 0;
    }
}
