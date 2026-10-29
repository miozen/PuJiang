using System.IO;
using System.Windows;
using ZupuPaiBan.Data;

namespace ZupuPaiBan.Views;

/// <summary>Explicit, fail-closed UI boundary for the versioned internal JSON exchange format.</summary>
public partial class GenealogyExchangeImportDialog : Window
{
    private readonly TargetContextCoordinator _coordinator;
    private string? _selectedPath;
    private GenealogyExchangeImportPreflightResult? _preflight;

    internal Func<Microsoft.Win32.OpenFileDialog>? OpenFileDialogFactory { get; set; }
    internal Func<MessageBoxResult>? CopyConfirmationOverride { get; set; }
    internal Guid? ImportedGenealogyId { get; private set; }

    public GenealogyExchangeImportDialog(TargetContextCoordinator coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    private bool IsCopyImport => CopyImportMode.IsChecked == true;

    private async void SelectFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var operation = _coordinator.EnterOperation();
            var dialog = OpenFileDialogFactory?.Invoke() ?? new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择内部 JSON 文件",
                Filter = "谱匠内部 JSON 文件|*.json|JSON 文件|*.json"
            };
            if (dialog.ShowDialog(this) != true) return;
            _selectedPath = dialog.FileName;
            SelectedFileText.Text = Path.GetFileName(_selectedPath);
            await RefreshPreflightAsync();
        }
        catch (Exception ex)
        {
            ShowFailure("无法选择或预检文件", ex);
        }
    }

    private async void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        CopyNameText.IsEnabled = IsCopyImport;
        if (_selectedPath is null) { UpdateConfirmState(); return; }
        try
        {
            using var operation = _coordinator.EnterOperation();
            await RefreshPreflightAsync();
        }
        catch (Exception ex)
        {
            ShowFailure("无法重新预检文件", ex);
        }
    }

    private void CopyNameChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateConfirmState();

    private async Task RefreshPreflightAsync()
    {
        if (_selectedPath is null) return;
        await using var context = _coordinator.CreateContext();
        var importer = new GenealogyExchangeImportService(context);
        _preflight = IsCopyImport
            ? await importer.PreflightCopyAsync(_selectedPath)
            : await importer.PreflightAsync(_selectedPath);
        PreflightText.Text = _preflight.Report.ToDisplayText();
        if (IsCopyImport && string.IsNullOrWhiteSpace(CopyNameText.Text) && _preflight.Document is not null)
            CopyNameText.Text = _preflight.Document.Genealogy.Name;
        UpdateConfirmState();
    }

    private async void ConfirmImport_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPath is null || _preflight?.Report.CanImport != true) return;
        try
        {
            using var operation = _coordinator.EnterOperation();
            var copyName = CopyNameText.Text.Trim();
            if (IsCopyImport)
            {
                if (copyName.Length is < 1 or > 200)
                    throw new InvalidOperationException("副本族谱名称须为 1 至 200 个字符。");
                var confirmation = CopyConfirmationOverride?.Invoke() ?? MessageBox.Show(this,
                    $"确定导入为新族谱副本“{copyName}”吗？\n系统将生成全新内部 ID，不会覆盖、删除或自动合并已有数据。",
                    "确认副本导入", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirmation != MessageBoxResult.Yes) return;
            }

            await using var context = _coordinator.CreateContext();
            var importer = new GenealogyExchangeImportService(context);
            var result = IsCopyImport
                ? await importer.ImportCopyAsync(_selectedPath, copyName)
                : await importer.ImportAsync(_selectedPath);
            if (!result.Imported || result.GenealogyId is not Guid genealogyId)
            {
                _preflight = new(null, result.Report);
                PreflightText.Text = result.Report.ToDisplayText();
                UpdateConfirmState();
                return;
            }

            ImportedGenealogyId = genealogyId;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowFailure("导入失败", ex);
        }
    }

    private void UpdateConfirmState()
    {
        ConfirmImportButton.IsEnabled = _selectedPath is not null && _preflight?.Report.CanImport == true &&
            (!IsCopyImport || CopyNameText.Text.Trim().Length is >= 1 and <= 200);
    }

    private static void ShowFailure(string title, Exception exception) =>
        MessageBox.Show($"{title}：{exception.Message}", "内部 JSON 导入", MessageBoxButton.OK, MessageBoxImage.Error);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
