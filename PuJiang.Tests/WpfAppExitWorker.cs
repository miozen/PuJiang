using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

// Runs a real App.OnStartup/OnExit in a dedicated temporary-directory process.
internal static class WpfAppExitWorker
{
    private static Task? _fileWork;
    private static bool _nativeExportObserved;
    private static Exception? _nativeExportFailure;
    private static bool _exportMessageObserved;
    private static bool _exportStatusObserved;
    private static bool _xpsFailureStatusObserved;
    private static bool _modalExitObserved;
    private static Exception? _modalExitFailure;
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, System.Text.StringBuilder className, int maxCount);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static async Task<int> RunAsync(string root, string scenario)
    {
        var database = new TestDatabase(root);
        Environment.SetEnvironmentVariable(AppDataPaths.TestRootEnvironmentVariable, root);
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var appDatabasePath = scenario.StartsWith("modal-backup-", StringComparison.Ordinal)
                    ? AppDataPaths.DatabasePath : database.DatabasePath;
                var options = new DbContextOptionsBuilder<ZupuDbContext>()
                    .UseSqlite($"Data Source={appDatabasePath};Pooling=False").Options;
                var coordinator = new TargetContextCoordinator(options);
                var main = new MainWindow(coordinator, database.BackupDirectory,
                    coordinateExit: scenario != "cancel", exitTimeout: scenario == "timeout" ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(10))
                { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
                Exception? failure = null;
                var verified = false;
                Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        var timer = Stopwatch.StartNew();
                        while (!main.IsEnabled && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
                        if (!main.IsEnabled) throw new TimeoutException("主窗口初始化超时。");
                        await VerifyScenarioAsync(app, main, coordinator, root, scenario);
                        verified = true;
                    }
                    catch (Exception ex) { failure = ex; app.Shutdown(-1); }
                });
                app.Run(main);
                if (failure != null) throw failure;
                if (!verified) throw new InvalidOperationException("应用退出前测试未完成。");
                if (scenario is "native-gedcom" or "native-collaboration")
                {
                    if (_nativeExportFailure is not null) throw _nativeExportFailure;
                    if (!_nativeExportObserved) throw new InvalidOperationException("未验证原生导出对话框竞争。");
                }
        if (scenario is "preview-pdf-cancel" or "preview-pdf-save" or "preview-xps-cancel" or "preview-xps-save" or "preview-xps-error" or "preview-print-cancel")
                {
                    if (_nativeExportFailure is not null) throw _nativeExportFailure;
                    if (!_nativeExportObserved) throw new InvalidOperationException("未观察到预览输出对话框退出竞争。");
                    if (scenario == "preview-pdf-save")
                    {
                        var output = Path.Combine(root, "preview.pdf");
                        if (!File.Exists(output) || !File.ReadAllBytes(output).AsSpan().StartsWith("%PDF"u8))
                            throw new InvalidOperationException("未成功写入测试 PDF。");
                    }
                    if (scenario == "preview-xps-save")
                    {
                        var output = Path.Combine(root, "preview.xps");
                        if (!File.Exists(output) || !File.ReadAllBytes(output).AsSpan().StartsWith("PK"u8))
                            throw new InvalidOperationException("未成功写入测试 XPS。");
                    }
                    if (scenario == "preview-xps-error")
                    {
                        var output = Path.Combine(root, "preview.xps");
                        if (File.Exists(output) || !_xpsFailureStatusObserved)
                            throw new InvalidOperationException("XPS 写入失败时未保留失败状态或意外生成文件。");
                    }
                }
                if (scenario is "modal-photo-cancel" or "modal-excel-import-cancel" or "modal-gedcom-import-cancel" or "modal-collaboration-import-cancel" or "modal-collaboration-confirm-cancel")
                {
                    if (_modalExitFailure is not null) throw _modalExitFailure;
                    if (!_modalExitObserved) throw new InvalidOperationException("未观察到模态入口的正常退出竞争。");
                }
                if (scenario.StartsWith("complete-", StringComparison.Ordinal))
                {
                    if (_nativeExportFailure is not null) throw _nativeExportFailure;
                    if (!_nativeExportObserved) throw new InvalidOperationException("未观察到导出写入期间的退出等待。");
                    var isError = scenario.EndsWith("-error", StringComparison.Ordinal);
                    var isCollaboration = scenario.Contains("collaboration", StringComparison.Ordinal);
                    if ((isError || isCollaboration) && !_exportMessageObserved)
                        throw new InvalidOperationException("未观察到导出结果提示。");
                    var output = Path.Combine(root, isCollaboration ? "ui-output.json" : "ui-output.ged");
                    if (isError && File.Exists(output)) throw new InvalidOperationException("模拟失败时意外创建文件。");
                    if (!isError && (!File.Exists(output) || !File.ReadAllText(output).Contains("虚构人物")))
                        throw new InvalidOperationException("确认保存后导出文件不正确。");
                    if (!isCollaboration && !_exportStatusObserved)
                        throw new InvalidOperationException("GEDCOM 导出状态未更新。");
                }
                if (_fileWork is not null)
                {
                    if (scenario is "excel-error" or "gedcom-error" or "collaboration-error")
                    {
                        if (!_fileWork.IsFaulted || _fileWork.Exception?.GetBaseException().Message != "模拟文件写入失败")
                            throw new InvalidOperationException("文件失败未传播或退出前未结束。", _fileWork.Exception);
                    }
                    else if (!_fileWork.IsCompletedSuccessfully) throw new InvalidOperationException("应用退出时文件操作尚未成功完成。", _fileWork.Exception);
                    if (scenario == "excel" && !File.Exists(Path.Combine(root, "output.xlsx")))
                        throw new InvalidOperationException("Excel 文件未写入。");
                    if (scenario is "gedcom" or "collaboration")
                    {
                        var path = Path.Combine(root, scenario == "gedcom" ? "output.ged" : "output.json");
                        if (!File.Exists(path) || !File.ReadAllText(path).Contains("虚构人物"))
                            throw new InvalidOperationException("导出文件内容不正确。");
                    }
                }
                if (scenario is "native-dialog-accept" or "modal-backup-accept")
                {
                    var files = Directory.GetFiles(database.BackupDirectory, "*.zip");
                    if (files.Length != 1) throw new InvalidOperationException("确认文件后未完成备份。");
                    OfflineBackupPackageService.ValidateAsync(files[0]).GetAwaiter().GetResult();
                }
                using var reacquired = DataDirectoryLease.Acquire(root);
                result.TrySetResult(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); result.TrySetResult(1); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await result.Task;
    }

    private static async Task VerifyScenarioAsync(App app, MainWindow main, TargetContextCoordinator coordinator,
        string root, string scenario)
    {
        if (scenario is "modal-photo-cancel" or "modal-excel-import-cancel" or "modal-gedcom-import-cancel" or "modal-collaboration-import-cancel" or "modal-collaboration-confirm-cancel")
        {
            const string photoTitle = "选择照片";
            const string excelTitle = "导入 Excel 数据";
            const string gedcomTitle = "导入 GEDCOM 数据";
            const string collaborationTitle = "选择协作文件";
            const string confirmTitle = "确认导入";
            Window? child = null;
            string title;
            Action open;
            if (scenario == "modal-photo-cancel")
            {
                var editor = new MemberEditViewModel(new FamilyMember { Name = "虚构人物" });
                editor.PhotoDialogFactory = () => new Microsoft.Win32.OpenFileDialog { Title = photoTitle };
                editor.ModalOperationFactory = coordinator.EnterOperation;
                child = new MemberEditDialog(editor) { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
                child.Show();
                title = photoTitle;
                open = () => editor.BrowsePhotoCommand.Execute(null);
            }
            else if (scenario is "modal-excel-import-cancel" or "modal-gedcom-import-cancel")
            {
                var viewModel = (MainViewModel)main.DataContext;
                if (scenario == "modal-excel-import-cancel")
                {
                    viewModel.ExcelImportDialogFactory = () => new Microsoft.Win32.OpenFileDialog { Title = excelTitle };
                    title = excelTitle;
                    open = () => _ = viewModel.ImportFromExcelCommand.ExecuteAsync(null);
                }
                else
                {
                    viewModel.GedcomImportDialogFactory = () => new Microsoft.Win32.OpenFileDialog { Title = gedcomTitle };
                    title = gedcomTitle;
                    open = () => _ = viewModel.ImportFromGedcomCommand.ExecuteAsync(null);
                }
            }
            else
            {
                var data = new DataService(coordinator);
                var parent = await data.AddMemberAsync(new FamilyMember { Name = "虚构父" });
                var import = new CollaborationImportDialog([parent], data)
                { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
                child = import;
                child.Show();
                if (scenario == "modal-collaboration-import-cancel")
                {
                    import.OpenFileDialogFactory = () => new Microsoft.Win32.OpenFileDialog { Title = collaborationTitle };
                    title = collaborationTitle;
                    open = () => typeof(CollaborationImportDialog).GetMethod("BtnSelectFile_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(import, [import, new RoutedEventArgs()]);
                }
                else
                {
                    typeof(CollaborationImportDialog).GetField("_selectedFilePath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(import, Path.Combine(root, "fictional.json"));
                    typeof(CollaborationImportDialog).GetField("_targetParentId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(import, parent.Id);
                    title = confirmTitle;
                    open = () => typeof(CollaborationImportDialog).GetMethod("BtnImport_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(import, [import, new RoutedEventArgs()]);
                }
            }

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            var lastPost = Stopwatch.StartNew();
            timer.Tick += (_, _) =>
            {
                var dialog = FindOwnDialog(title);
                if (dialog == IntPtr.Zero) return;
                try
                {
                    if (!_modalExitObserved)
                    {
                        main.Close();
                        if (!main.IsVisible || main.IsEnabled)
                            throw new InvalidOperationException("模态文件选择或确认期间未等待正常退出。");
                        AssertLeaseHeld(root);
                        AssertNewOperationRejected(coordinator);
                        _modalExitObserved = true;
                        lastPost.Restart();
                    }
                    if (lastPost.Elapsed >= TimeSpan.FromMilliseconds(500))
                    {
                        var cancelMessage = scenario == "modal-collaboration-confirm-cancel" ? 0x0111u : 0x0010u;
                        var cancelCommand = scenario == "modal-collaboration-confirm-cancel" ? (IntPtr)7 : IntPtr.Zero;
                        if (!PostMessage(dialog, cancelMessage, cancelCommand, IntPtr.Zero))
                            throw new InvalidOperationException("无法取消测试模态对话框。");
                        lastPost.Restart();
                    }
                }
                catch (Exception ex)
                {
                    _modalExitFailure = ex;
                    app.Shutdown(-1);
                }
            };
            timer.Start();
            _ = main.Dispatcher.BeginInvoke(() =>
            {
                try { open(); }
                catch (Exception ex) { _modalExitFailure = ex; app.Shutdown(-1); }
            }, DispatcherPriority.Background);
            return;
        }

                if (scenario is "preview-pdf-cancel" or "preview-pdf-save" or "preview-xps-cancel" or "preview-xps-save" or "preview-xps-error" or "preview-print-cancel")
        {
            var data = new DataService(coordinator);
            await data.AddMemberAsync(new FamilyMember { Name = "虚构人物" });
            var preview = new PrintPreviewWindow(data, new PrintService())
            { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            var childClosed = false;
            preview.Closed += (_, _) => childClosed = true;
            main.Closed += (_, _) => { if (!childClosed) throw new InvalidOperationException("主窗口关闭时预览仍未关闭。"); };
            preview.Show();
            var viewModel = (PreviewViewModel)preview.DataContext;
            if (scenario == "preview-xps-error")
            {
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PreviewViewModel.StatusText) && viewModel.StatusText == "导出失败")
                        _xpsFailureStatusObserved = true;
                };
            }
            if (scenario == "preview-pdf-save")
            {
                var path = Path.Combine(root, "preview.pdf");
                viewModel.PdfSaveDialogFactory = () => new Microsoft.Win32.SaveFileDialog
                { Title = "导出为 PDF", FileName = path, Filter = "PDF 文件|*.pdf", OverwritePrompt = false };
                viewModel.PdfExportOverride = (layout, settings, output) =>
                {
                    if (!string.Equals(Path.GetFullPath(output), path, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("PDF 目标不是指定的隔离测试路径。");
                    return new PdfExportService().ExportToPdf(layout, settings, output);
                };
            }
            if (scenario is "preview-xps-save" or "preview-xps-error")
            {
                var path = Path.Combine(root, "preview.xps");
                viewModel.XpsSaveDialogFactory = () => new Microsoft.Win32.SaveFileDialog
                { Title = "导出为 XPS", FileName = path, Filter = "XPS 文件|*.xps", OverwritePrompt = false };
                if (scenario == "preview-xps-error")
                {
                    viewModel.XpsExportOverride = (pages, output) =>
                    {
                        if (!string.Equals(Path.GetFullPath(output), path, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("XPS 目标不是指定的隔离测试路径。");
                        return false;
                    };
                }
            }
            await viewModel.InitializeAsync();
            Console.WriteLine($"Preview initialized: {scenario}");
            if (viewModel.PreviewContent is null) throw new InvalidOperationException("测试预览未生成页面。");
            var lastPost = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                var dialog = scenario switch
                {
                    "preview-pdf-cancel" or "preview-pdf-save" => FindOwnDialog("导出为 PDF"),
                    "preview-xps-cancel" or "preview-xps-save" or "preview-xps-error" => FindOwnDialog("导出为 XPS"),
                    _ => FindOwnPrintDialog()
                };
                if (dialog == IntPtr.Zero) return;
                if (!_nativeExportObserved && _nativeExportFailure is null)
                {
                    try
                    {
                        main.Close();
                        if (!main.IsVisible || main.IsEnabled || !preview.IsVisible)
                            throw new InvalidOperationException("预览输出对话框期间未等待退出。");
                        AssertLeaseHeld(root);
                        AssertNewOperationRejected(coordinator);
                        _nativeExportObserved = true;
                    }
                    catch (Exception ex) { _nativeExportFailure = ex; }
                    lastPost.Restart();
                }
                if (lastPost.Elapsed >= TimeSpan.FromMilliseconds(500))
                {
                    var accept = scenario is "preview-pdf-save" or "preview-xps-save" or "preview-xps-error";
                    var message = accept ? 0x0111u : 0x0010u;
                    if (!PostMessage(dialog, message, accept ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero))
                        _nativeExportFailure ??= new InvalidOperationException("无法取消测试输出对话框。");
                    lastPost.Restart();
                }
            };
            timer.Start();
            _ = main.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    Console.WriteLine($"Opening preview output: {scenario}");
                    if (scenario is "preview-pdf-cancel" or "preview-pdf-save") await viewModel.ExportPdfCommand.ExecuteAsync(null);
                    else if (scenario is "preview-xps-cancel" or "preview-xps-save" or "preview-xps-error") viewModel.ExportXpsCommand.Execute(null);
                    else viewModel.PrintCommand.Execute(null);
                }
                catch (Exception ex) { _nativeExportFailure = ex; app.Shutdown(-1); }
                finally
                {
                    timer.Stop();
                    if (!_nativeExportObserved && _nativeExportFailure is null)
                    {
                        _nativeExportFailure = new InvalidOperationException($"{scenario} 返回但未找到文件或打印对话框。");
                        app.Shutdown(-1);
                    }
                }
            }, DispatcherPriority.Background);
            return;
        }

        if (scenario.StartsWith("complete-", StringComparison.Ordinal))
        {
            var data = new DataService(coordinator);
            var member = await data.AddMemberAsync(new FamilyMember { Name = "虚构人物" });
            var isCollaboration = scenario.Contains("collaboration", StringComparison.Ordinal);
            var isError = scenario.EndsWith("-error", StringComparison.Ordinal);
            var path = Path.Combine(root, isCollaboration ? "ui-output.json" : "ui-output.ged");
            var fileTitle = isCollaboration ? "协作导出" : "导出 GEDCOM 数据";
            var messageTitle = isError ? "错误" : "协作导出";
            var started = false;
            var childOpened = false;
            var childClosed = false;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var lastPost = Stopwatch.StartNew();
            var viewModel = (MainViewModel)main.DataContext;
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.StatusText) && viewModel.StatusText.Contains(isError ? "导出失败" : "已导出到"))
                    _exportStatusObserved = true;
            };
            Microsoft.Win32.SaveFileDialog CreateDialog() => new()
            {
                Title = fileTitle, FileName = path, OverwritePrompt = false,
                Filter = isCollaboration ? "协作 JSON 文件|*.json" : "GEDCOM 文件|*.ged"
            };
            async Task PauseAsync() { started = true; await release.Task; if (isError) throw new IOException("模拟文件写入失败"); }
            void AssertTestOutput(string output)
            {
                if (!string.Equals(Path.GetFullPath(output), path, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("原生对话框返回的路径不在指定测试输出位置。");
            }
            CollaborationExportDialog? child = null;
            if (isCollaboration)
            {
                child = new CollaborationExportDialog([member], data)
                { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
                  SaveDialogFactory = CreateDialog,
                  ExportOverride = async (id, members, output) => { AssertTestOutput(output); await PauseAsync(); await new CollaborationService().ExportSubtreeJson(id, members, output); } };
                typeof(CollaborationExportDialog).GetField("_selectedMemberId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(child, member.Id);
                child.Closed += (_, _) => childClosed = true;
                main.Closed += (_, _) => { if (!childClosed) throw new InvalidOperationException("协作模态窗口未关闭。"); };
            }
            else
            {
                viewModel.GedcomSaveDialogFactory = CreateDialog;
                viewModel.GedcomExportOverride = async (members, output) => { AssertTestOutput(output); await PauseAsync(); await new GedcomService().ExportToGedcomAsync(members, output); };
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                try
                {
                    if (isCollaboration && !childOpened && child!.IsVisible)
                    {
                        childOpened = true;
                        _ = main.Dispatcher.BeginInvoke(() =>
                        {
                            try { typeof(CollaborationExportDialog).GetMethod("BtnExport_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(child, [child, new RoutedEventArgs()]); }
                            catch (Exception ex) { _nativeExportFailure = ex; app.Shutdown(-1); }
                        });
                    }
                    if (!started)
                    {
                        var native = FindOwnDialog(fileTitle);
                        if (native != IntPtr.Zero && lastPost.Elapsed >= TimeSpan.FromMilliseconds(500))
                        {
                            if (!PostMessage(native, 0x0111, (IntPtr)1, IntPtr.Zero)) throw new InvalidOperationException("无法确认测试文件对话框。");
                            lastPost.Restart();
                        }
                        return;
                    }
                    if (!_nativeExportObserved)
                    {
                        main.Close();
                        if (!main.IsVisible || main.IsEnabled) throw new InvalidOperationException("在途导出未阻止退出。");
                        AssertLeaseHeld(root);
                        AssertNewOperationRejected(coordinator);
                        _nativeExportObserved = true;
                        release.TrySetResult();
                    }
                    if (isError || isCollaboration)
                    {
                        var message = FindOwnDialog(messageTitle);
                        if (message != IntPtr.Zero && lastPost.Elapsed >= TimeSpan.FromMilliseconds(500))
                        {
                            AssertLeaseHeld(root);
                            _exportMessageObserved = true;
                            if (!PostMessage(message, 0x0010, IntPtr.Zero, IntPtr.Zero)) throw new InvalidOperationException("无法关闭测试提示框。");
                            lastPost.Restart();
                        }
                    }
                }
                catch (Exception ex) { _nativeExportFailure = ex; timer.Stop(); release.TrySetResult(); app.Shutdown(-1); }
            };
            timer.Start();
            _ = main.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    if (isCollaboration) child!.ShowDialog();
                    else await viewModel.ExportToGedcomCommand.ExecuteAsync(null);
                }
                catch (Exception ex) { _nativeExportFailure = ex; app.Shutdown(-1); }
                finally { timer.Stop(); }
            }, DispatcherPriority.Background);
            return;
        }

        if (scenario is "native-gedcom" or "native-collaboration")
        {
            var data = new DataService(coordinator);
            var member = await data.AddMemberAsync(new FamilyMember { Name = "虚构人物" });
            CollaborationExportDialog? child = null;
            if (scenario == "native-collaboration")
            {
                child = new CollaborationExportDialog([member], data)
                { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
                child.Show();
                typeof(CollaborationExportDialog).GetField("_selectedMemberId", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(child, member.Id);
            }
            var title = scenario == "native-gedcom" ? "导出 GEDCOM 数据" : "协作导出";
            var lastPost = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                var dialog = FindOwnDialog(title);
                if (dialog == IntPtr.Zero) return;
                if (!_nativeExportObserved && _nativeExportFailure is null)
                {
                    try
                    {
                        main.Close();
                        if (!main.IsVisible || main.IsEnabled) throw new InvalidOperationException("导出文件对话框期间未等待退出。");
                        AssertLeaseHeld(root);
                        AssertNewOperationRejected(coordinator);
                        _nativeExportObserved = true;
                    }
                    catch (Exception ex) { _nativeExportFailure = ex; }
                    lastPost.Restart();
                }
                if (lastPost.Elapsed >= TimeSpan.FromMilliseconds(500))
                {
                    if (!PostMessage(dialog, 0x0010, IntPtr.Zero, IntPtr.Zero))
                        _nativeExportFailure ??= new InvalidOperationException("无法关闭测试文件对话框。");
                    lastPost.Restart();
                }
            };
            timer.Start();
            _ = main.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    if (scenario == "native-gedcom")
                        await ((MainViewModel)main.DataContext).ExportToGedcomCommand.ExecuteAsync(null);
                    else
                    {
                        var click = typeof(CollaborationExportDialog).GetMethod("BtnExport_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        click.Invoke(child, [child, new RoutedEventArgs()]);
                    }
                }
                catch (Exception ex) { _nativeExportFailure = ex; app.Shutdown(-1); }
                finally { timer.Stop(); }
            }, DispatcherPriority.Background);
            return;
        }

        if (scenario is "modal-backup-cancel" or "modal-backup-accept")
        {
            var database = new TestDatabase(root);
            Directory.CreateDirectory(database.BackupDirectory);
            var backupOpened = false;
            var fileReturned = false;
            var closeObserved = false;
            var childClosed = false;
            Exception? failure = null;
            var lastPost = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                var native = FindOwnDialog("保存数据库专用备份");
                if (native != IntPtr.Zero)
                {
                    if (!closeObserved && failure is null)
                    {
                        Console.WriteLine("Modal: native dialog detected.");
                        try
                        {
                            main.Close();
                            Console.WriteLine("Modal: close requested.");
                            if (!main.IsVisible || main.IsEnabled) throw new InvalidOperationException("模态文件选择期间未等待退出。");
                            AssertLeaseHeld(root);
                            AssertNewOperationRejected(coordinator);
                            closeObserved = true;
                        }
                        catch (Exception ex) { failure = ex; }
                        lastPost.Restart();
                    }
                    // The native shell window may be visible before its controls are ready.
                    // Retry only against the same-process, same-title dialog until it returns.
                    if (lastPost.Elapsed >= TimeSpan.FromMilliseconds(500))
                    {
                        var message = scenario == "modal-backup-accept" ? 0x0111u : 0x0010u;
                        if (!PostMessage(native, message, scenario == "modal-backup-accept" ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero))
                            failure ??= new InvalidOperationException("无法向测试进程的文件对话框发送消息。");
                        lastPost.Restart();
                    }
                    return;
                }
                if (backupOpened) return;
                var child = app.Windows.OfType<DatabaseBackupDialog>().SingleOrDefault();
                if (child is null || !child.IsVisible) return;
                backupOpened = true;
                Console.WriteLine("Modal: backup dialog detected.");
                child.Closed += (_, _) => childClosed = true;
                main.Closed += (_, _) =>
                {
                    if (!childClosed) throw new InvalidOperationException("主窗口关闭时模态备份窗口仍未关闭。");
                };
                // Return from the timer tick before entering the next nested modal loop,
                // otherwise DispatcherTimer cannot schedule the tick that finds that dialog.
                _ = main.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var create = typeof(DatabaseBackupDialog).GetMethod("CreateBackup_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                            ?? throw new InvalidOperationException("无法找到备份按钮事件。");
                        create.Invoke(child, [child, new RoutedEventArgs()]);
                        fileReturned = true;
                        timer.Stop();
                        Console.WriteLine("Modal: file handler returned.");
                    }
                    catch (Exception ex) { failure = ex; timer.Stop(); app.Shutdown(-1); }
                });
            };
            timer.Start();
            var open = typeof(MainWindow).GetMethod("DatabaseBackup_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("无法找到主窗口备份入口。");
            open.Invoke(main, [main, new RoutedEventArgs()]); // Real handler enters the WPF ShowDialog nested loop.
            Console.WriteLine("Modal: backup handler returned.");
            timer.Stop();
            if (failure != null) throw failure;
            if (!backupOpened || !fileReturned || !closeObserved || !childClosed)
                throw new InvalidOperationException("未完成真实模态窗口的关闭竞争验证。");
            if (scenario == "modal-backup-cancel" && Directory.GetFiles(database.BackupDirectory, "*.zip").Length != 0)
                throw new InvalidOperationException("取消选择后意外创建备份。");
            return;
        }

        if (scenario is "excel" or "excel-error" or "gedcom" or "gedcom-error" or "collaboration" or "collaboration-error" or "backup-validate" or "backup-preflight")
        {
            var database = new TestDatabase(root);
            Directory.CreateDirectory(database.BackupDirectory);
            var backup = new DatabaseBackupDialog(database.DatabasePath, database.BackupDirectory, coordinator)
            { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            backup.Show();
            var package = Path.Combine(database.BackupDirectory, "test.zip");
            if (scenario is "backup-validate" or "backup-preflight") await OfflineBackupPackageService.CreateAsync(database.DatabasePath, package);
            var people = new List<FamilyMember> { new() { Id = 1, Name = "虚构人物" } };
            var collaboration = new CollaborationExportDialog(people, new DataService(coordinator));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task PauseAsync() { entered.TrySetResult(); await release.Task; }
            _fileWork = scenario switch
            {
                "excel" => ((MainViewModel)main.DataContext).ExportExcelFileAsync(
                    [new FamilyMember { Name = "虚构人物" }], Path.Combine(root, "output.xlsx"),
                    async (members, path) => { await PauseAsync(); await new ExcelService().ExportToExcelAsync(members, path); }),
                "excel-error" => ((MainViewModel)main.DataContext).ExportExcelFileAsync(
                    [new FamilyMember { Name = "虚构人物" }], Path.Combine(root, "output.xlsx"),
                    async (_, _) => { await PauseAsync(); throw new IOException("模拟文件写入失败"); }),
                "gedcom" => ((MainViewModel)main.DataContext).ExportGedcomFileAsync(people, Path.Combine(root, "output.ged"),
                    async (members, path) => { await PauseAsync(); await new GedcomService().ExportToGedcomAsync(members, path); }),
                "gedcom-error" => ((MainViewModel)main.DataContext).ExportGedcomFileAsync(people, Path.Combine(root, "output.ged"),
                    async (_, _) => { await PauseAsync(); throw new IOException("模拟文件写入失败"); }),
                "collaboration" => collaboration.ExportFileAsync(1, Path.Combine(root, "output.json"),
                    async (id, members, path) => { await PauseAsync(); await new CollaborationService().ExportSubtreeJson(id, members, path); }),
                "collaboration-error" => collaboration.ExportFileAsync(1, Path.Combine(root, "output.json"),
                    async (_, _, _) => { await PauseAsync(); throw new IOException("模拟文件写入失败"); }),
                "backup-validate" => backup.ValidateBackupAsync(package, async path =>
                { await PauseAsync(); return await OfflineBackupPackageService.ValidateAsync(path); }),
                _ => backup.PreflightBackupAsync(package, async path =>
                { await PauseAsync(); return await new BackupRestorePreflightService(database.BackupDirectory).CheckAsync(path); })
            };
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            main.Close();
            if (!main.IsVisible || main.IsEnabled || _fileWork.IsCompleted)
                throw new InvalidOperationException("应用未等待在途文件操作。");
            AssertLeaseHeld(root);
            AssertNewOperationRejected(coordinator);
            // Release only after this method returns and the caller has recorded verification.
            _ = main.Dispatcher.BeginInvoke(() => release.TrySetResult(), DispatcherPriority.Background);
            return;
        }

        if (scenario is "native-dialog" or "native-dialog-idle" or "native-dialog-accept")
        {
            var database = new TestDatabase(root);
            Directory.CreateDirectory(database.BackupDirectory);
            var child = new DatabaseBackupDialog(database.DatabasePath, database.BackupDirectory, coordinator)
            { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            var childClosed = false;
            child.Closed += (_, _) => childClosed = true;
            main.Closed += (_, _) =>
            {
                if (!childClosed) throw new InvalidOperationException("主窗口关闭时备份窗口仍未关闭。");
            };
            child.Show();
            var dialogHold = scenario == "native-dialog" ? coordinator.CreateContext() : null;
            if (dialogHold is not null) await dialogHold.Database.OpenConnectionAsync();
            Exception? timerFailure = null;
            var observed = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                var dialog = FindOwnDialog("保存数据库专用备份");
                if (dialog == IntPtr.Zero) return;
                timer.Stop();
                Console.WriteLine("Native dialog detected; requesting normal close.");
                try
                {
                    main.Close();
                    Console.WriteLine("Normal close returned; checking drain and lock.");
                    if (!main.IsVisible || main.IsEnabled) throw new InvalidOperationException("原生对话框期间未等待排空。");
                    AssertLeaseHeld(root);
                    AssertNewOperationRejected(coordinator);
                    observed = true;
                }
                catch (Exception ex) { timerFailure = ex; }
                finally
                {
                    if (scenario == "native-dialog-accept") PostMessage(dialog, 0x0111, (IntPtr)1, IntPtr.Zero);
                    else PostMessage(dialog, 0x0010, IntPtr.Zero, IntPtr.Zero);
                }
            };
            timer.Start();
            var handler = typeof(DatabaseBackupDialog).GetMethod("CreateBackup_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("无法找到备份按钮事件。");
            handler.Invoke(child, [child, new RoutedEventArgs()]);
            Console.WriteLine("Native dialog returned.");
            timer.Stop();
            if (timerFailure != null) throw timerFailure;
            if (!observed || !child.IsVisible || (scenario != "native-dialog-accept" && Directory.GetFiles(database.BackupDirectory, "*.zip").Length != 0))
                throw new InvalidOperationException("未观察到文件对话框取消与退出等待的预期状态。");
            // Record the assertions before the final lease permits App.OnExit to stop the dispatcher.
            if (dialogHold is not null)
                _ = main.Dispatcher.BeginInvoke(() => dialogHold.Dispose(), DispatcherPriority.Background);
            return;
        }

        if (scenario == "success")
        {
            var child = new Window { Owner = main, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            var childClosed = false;
            child.Closed += (_, _) => childClosed = true;
            main.Closed += (_, _) =>
            {
                if (!childClosed) throw new InvalidOperationException("主窗口关闭时子窗口仍未关闭。");
            };
            child.Show();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = Task.Run(async () =>
            {
                using var operation = coordinator.EnterOperation();
                await using var context = coordinator.CreateContext();
                await context.Database.OpenConnectionAsync();
                entered.TrySetResult();
                await release.Task;
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var finalHold = coordinator.CreateContext();
            main.Close();
            if (!main.IsVisible || main.IsEnabled) throw new InvalidOperationException("在途操作期间主窗口未保持等待。");
            AssertLeaseHeld(root);
            AssertNewOperationRejected(coordinator);
            release.SetResult();
            await work;
            // Let the caller record the assertions before allowing App.OnExit to stop the dispatcher.
            _ = main.Dispatcher.BeginInvoke(() => finalHold.Dispose(), DispatcherPriority.Background);
            return;
        }

        if (scenario is "child-refusal" or "child-error")
        {
            var child = new Window { Owner = main, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            var refuse = true;
            System.ComponentModel.CancelEventHandler onClosing = (_, e) =>
            {
                if (refuse && scenario == "child-error") throw new InvalidOperationException("模拟子窗口关闭异常。");
                e.Cancel = refuse;
            };
            child.Closing += onClosing;
            child.Show();
            main.Close();
            await WaitForFailureAsync(main);
            if (scenario == "child-refusal" && !child.IsVisible)
                throw new InvalidOperationException("拒绝关闭的子窗口意外关闭。");
            AssertLeaseHeld(root);
            AssertNewOperationRejected(coordinator);
            refuse = false;
            child.Closing -= onClosing;
            if (child.IsVisible) child.Close();
            _ = app.Dispatcher.BeginInvoke(() => app.Shutdown(), DispatcherPriority.Background); // Test cleanup only.
            return;
        }

        await using var held = coordinator.CreateContext();
        if (scenario == "cancel")
        {
            using var cancellation = new CancellationTokenSource();
            var stopping = main.PrepareForExitAsync(cancellation.Token);
            cancellation.Cancel();
            if (await stopping) throw new InvalidOperationException("取消后不应报告成功。");
        }
        else if (scenario == "timeout")
        {
            main.Close();
            await WaitForFailureAsync(main);
        }
        else throw new InvalidOperationException("未知测试场景。");
        if (!main.IsVisible || main.IsEnabled) throw new InvalidOperationException("取消或超时后窗口未保持失败关闭。");
        AssertLeaseHeld(root);
        AssertNewOperationRejected(coordinator);
        await held.DisposeAsync();
        _ = app.Dispatcher.BeginInvoke(() => app.Shutdown(), DispatcherPriority.Background); // Test cleanup only.
    }

    private static IntPtr FindOwnDialog(string title)
    {
        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var pid);
            if (pid != (uint)Environment.ProcessId) return true;
            var text = new System.Text.StringBuilder(512);
            GetWindowText(window, text, text.Capacity);
            if (text.ToString() != title) return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static IntPtr FindOwnPrintDialog()
    {
        var titled = FindOwnDialog("打印");
        if (titled != IntPtr.Zero) return titled;
        titled = FindOwnDialog("Print");
        if (titled != IntPtr.Zero) return titled;

        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var pid);
            if (pid != (uint)Environment.ProcessId) return true;
            var className = new System.Text.StringBuilder(256);
            if (GetClassName(window, className, className.Capacity) == 0 || className.ToString() != "#32770") return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static async Task WaitForFailureAsync(MainWindow main)
    {
        var timer = Stopwatch.StartNew();
        while (!((MainViewModel)main.DataContext).StatusText.Contains("未完成") && timer.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        if (!((MainViewModel)main.DataContext).StatusText.Contains("未完成"))
            throw new TimeoutException("未观察到失败关闭状态。");
    }

    private static void AssertLeaseHeld(string root)
    {
        try { using var unexpected = DataDirectoryLease.Acquire(root); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("使用")) { return; }
        throw new InvalidOperationException("数据目录锁过早释放。");
    }

    private static void AssertNewOperationRejected(TargetContextCoordinator coordinator)
    {
        try { using var unexpected = coordinator.EnterOperation(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("停用")) { return; }
        throw new InvalidOperationException("排空后新操作未被拒绝。");
    }
}
