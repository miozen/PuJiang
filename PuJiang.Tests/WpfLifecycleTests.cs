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
    [InlineData("native-dialog")]
    [InlineData("native-dialog-idle")]
    [InlineData("native-dialog-accept")]
    [InlineData("modal-backup-cancel")]
    [InlineData("modal-backup-accept")]
    [InlineData("excel")]
    [InlineData("excel-error")]
    [InlineData("gedcom")]
    [InlineData("gedcom-error")]
    [InlineData("collaboration")]
    [InlineData("collaboration-error")]
    [InlineData("native-gedcom")]
    [InlineData("native-collaboration")]
    [InlineData("complete-gedcom-success")]
    [InlineData("complete-gedcom-error")]
    [InlineData("complete-collaboration-success")]
    [InlineData("complete-collaboration-error")]
    [InlineData("preview-pdf-cancel")]
    [InlineData("preview-pdf-save")]
    [InlineData("preview-pdf-error")]
    [InlineData("preview-xps-cancel")]
    [InlineData("preview-xps-save")]
    [InlineData("preview-xps-error")]
    [InlineData("modal-photo-cancel")]
    [InlineData("modal-excel-import-cancel")]
    [InlineData("modal-gedcom-import-cancel")]
    [InlineData("modal-collaboration-import-cancel")]
    [InlineData("modal-collaboration-confirm-cancel")]
    [InlineData("backup-validate")]
    [InlineData("backup-preflight")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    [InlineData("child-refusal")]
    [InlineData("child-error")]
    [Trait("Category", "UI")]
    public async Task Application_exit_retains_lock_until_safe_or_fails_closed(string scenario)
        => await RunWorkerAsync("--wpf-app-exit", scenario);

    [Fact]
    [Trait("Category", "UI")]
    public async Task Print_dialog_cancel_and_exit_race_requires_interactive_print_environment()
        => await RunWorkerAsync("--wpf-app-exit", "preview-print-cancel");

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
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            var timedOutOutput = await stdout;
            var timedOutError = await stderr;
            throw new TimeoutException($"隔离 WPF 测试超时: {timedOutOutput} {timedOutError}");
        }
        var output = await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == 0, $"隔离 WPF 测试退出码 {process.ExitCode}: {output} {error}");
    }
}
