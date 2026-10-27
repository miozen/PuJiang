using System.Diagnostics;
using Xunit;

namespace PuJiang.Tests;

public class WpfLifecycleTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Isolated_wpf_windows_use_shared_drain_and_release_database()
        => await RunWorkerAsync("--wpf-lifecycle");

    [Fact]
    [Trait("Category", "UI")]
    public async Task Displayed_windows_file_dialog_and_dispatcher_reentry_remain_closed_after_quiescence()
        => await RunWorkerAsync("--wpf-displayed");

    [Fact]
    [Trait("Category", "UI")]
    public async Task Selected_import_and_window_close_keep_inflight_work_registered()
        => await RunWorkerAsync("--wpf-inflight");

    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    [InlineData("child-refusal")]
    [InlineData("child-error")]
    [Trait("Category", "UI")]
    public async Task Application_exit_retains_lock_until_safe_or_fails_closed(string scenario)
        => await RunWorkerAsync("--wpf-app-exit", scenario);

    private static async Task RunWorkerAsync(string mode, string? scenario = null)
    {
        await using var database = new TestDatabase();
        await using (var context = database.CreateContext()) { }
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(RestoreWorkerProgram).Assembly.Location);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(Path.GetDirectoryName(database.DatabasePath)!);
        if (scenario is not null) start.ArgumentList.Add(scenario);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动隔离测试进程。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        var output = await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == 0, $"隔离 WPF 测试退出码 {process.ExitCode}: {output} {error}");
    }
}
