using System.IO;
using System.Windows;
using Microsoft.Win32;
using ZupuPaiBan.Data;

namespace ZupuPaiBan.Views;

public partial class DatabaseBackupDialog : Window
{
    private readonly string _databasePath;
    private readonly string _backupDirectory;
    private readonly TargetContextCoordinator _coordinator;

    public DatabaseBackupDialog(string databasePath, string backupDirectory, TargetContextCoordinator coordinator)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _backupDirectory = backupDirectory;
        _coordinator = coordinator;
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存数据库专用备份",
            Filter = "数据库备份包 (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = false,
            InitialDirectory = _backupDirectory,
            FileName = $"pujiang-database-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip"
        };
        if (dialog.ShowDialog(this) != true) return;
        IsEnabled = false;
        ResultText.Text = "正在创建并校验数据库备份…";
        try
        {
            using var operation = _coordinator.EnterOperation();
            await OfflineBackupPackageService.CreateAsync(_databasePath, dialog.FileName);
            ResultText.Text = $"数据库备份已创建并校验：\n{dialog.FileName}\n不包含照片、附件或配置。";
        }
        catch (Exception ex) { ResultText.Text = $"创建失败，未覆盖已有目标文件：\n{ex.Message}"; }
        finally { IsEnabled = true; }
    }

    private async void ValidateBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择数据库备份包",
            Filter = "数据库备份包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
            InitialDirectory = Directory.Exists(_backupDirectory) ? _backupDirectory : null,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        IsEnabled = false;
        ResultText.Text = "正在校验备份包…";
        try
        {
            var manifest = await OfflineBackupPackageService.ValidateAsync(dialog.FileName);
            ResultText.Text = $"校验通过：版本 {manifest.FormatVersion}，数据库 {manifest.DatabaseBytes:N0} 字节。\n不包含照片、附件或配置；校验不证明备份来源。";
        }
        catch (Exception ex) { ResultText.Text = $"校验失败：\n{ex.Message}"; }
        finally { IsEnabled = true; }
    }

    private async void PreflightBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要预检的数据库备份包",
            Filter = "数据库备份包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
            InitialDirectory = Directory.Exists(_backupDirectory) ? _backupDirectory : null,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        IsEnabled = false;
        ResultText.Text = "正在隔离检查数据库备份，不会替换当前数据库…";
        try
        {
            var report = await new BackupRestorePreflightService(_backupDirectory).CheckAsync(dialog.FileName);
            ResultText.Text = FormatPreflightReport(report);
        }
        catch (AggregateException ex)
        {
            ResultText.Text = $"预检未完成：\n{ex.Message}\n{string.Join("\n", ex.Flatten().InnerExceptions.Select(error => error.Message))}";
        }
        catch (Exception ex) { ResultText.Text = $"预检失败：\n{ex.Message}"; }
        finally { IsEnabled = true; }
    }

    internal static string FormatPreflightReport(BackupRestorePreflightReport report) =>
        $"预检通过：备份格式版本 {report.FormatVersion}，数据库结构和迁移版本符合当前程序。\n" +
        $"数据库：{(report.IncludesDatabase ? "包含" : "不包含")}；照片和附件：{(report.IncludesAttachments ? "包含" : "不包含")}；配置：{(report.IncludesConfiguration ? "包含" : "不包含")}。\n" +
        "仅完成预检，未恢复或替换数据库；不证明数据内容正确或备份来源真实性。";
}
