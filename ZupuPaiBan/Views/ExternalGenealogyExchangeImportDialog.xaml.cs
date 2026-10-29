using System.IO;
using System.Windows;
using ZupuPaiBan.Data;

namespace ZupuPaiBan.Views;

/// <summary>Fail-closed UI for external compatibility imports; it never targets an existing genealogy.</summary>
public partial class ExternalGenealogyExchangeImportDialog : Window
{
    private readonly TargetContextCoordinator _coordinator;
    private string? _selectedPath;
    private ExternalGenealogyExchangePreflightResult? _preflight;

    internal Func<Microsoft.Win32.OpenFileDialog>? OpenFileDialogFactory { get; set; }
    internal Guid? ImportedGenealogyId { get; private set; }

    public ExternalGenealogyExchangeImportDialog(TargetContextCoordinator coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    private async void SelectFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var operation = _coordinator.EnterOperation();
            var dialog = OpenFileDialogFactory?.Invoke() ?? new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择外部 Excel 或 GEDCOM 文件", Filter = "Excel 文件|*.xlsx|GEDCOM 文件|*.ged;*.gedcom"
            };
            if (dialog.ShowDialog(this) != true) return;
            _selectedPath = dialog.FileName;
            SelectedFileText.Text = Path.GetFileName(_selectedPath);
            await RefreshPreflightAsync();
        }
        catch (Exception ex) { ShowFailure("无法选择或预检文件", ex); }
    }

    private async Task RefreshPreflightAsync()
    {
        if (_selectedPath is null) return;
        var extension = Path.GetExtension(_selectedPath);
        _preflight = extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? await ExternalGenealogyExchangePreflightService.PreflightExcelAsync(_selectedPath)
            : extension.Equals(".ged", StringComparison.OrdinalIgnoreCase) || extension.Equals(".gedcom", StringComparison.OrdinalIgnoreCase)
                ? await ExternalGenealogyExchangePreflightService.PreflightGedcomAsync(_selectedPath)
                : throw new InvalidDataException("仅支持 .xlsx、.ged 或 .gedcom 文件。");
        PreflightText.Text = ToDisplayText(_preflight.Report);
        if (string.IsNullOrWhiteSpace(GenealogyNameText.Text) && _preflight.Target is not null)
            GenealogyNameText.Text = _preflight.Target.Genealogy.Name;
        AcceptLossesCheck.IsEnabled = _preflight.Report.CanMap && _preflight.Report.Losses.Count > 0;
        AcceptLossesCheck.IsChecked = _preflight.Report.Losses.Count == 0;
        UpdateConfirmState();
    }

    private async void ConfirmImport_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPath is null || _preflight?.Target is null || !_preflight.Report.CanMap) return;
        try
        {
            using var operation = _coordinator.EnterOperation();
            var name = GenealogyNameText.Text.Trim();
            var acceptLosses = _preflight.Report.Losses.Count == 0 || AcceptLossesCheck.IsChecked == true;
            await using var context = _coordinator.CreateContext();
            var importer = new ExternalGenealogyExchangeImportService(context);
            var extension = Path.GetExtension(_selectedPath);
            var result = extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? await importer.ImportExcelAsync(_selectedPath, name, acceptLosses)
                : await importer.ImportGedcomAsync(_selectedPath, name, acceptLosses);
            if (!result.Imported || result.GenealogyId is not Guid genealogyId)
            {
                PreflightText.Text = ToDisplayText(result.Report);
                UpdateConfirmState();
                return;
            }
            ImportedGenealogyId = genealogyId;
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { ShowFailure("导入失败", ex); }
    }

    private void GenealogyNameChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateConfirmState();
    private void AcceptLossesChanged(object sender, RoutedEventArgs e) => UpdateConfirmState();
    private void UpdateConfirmState() => ConfirmImportButton.IsEnabled = _selectedPath is not null && _preflight?.Target is not null && _preflight.Report.CanMap && GenealogyNameText.Text.Trim().Length is >= 1 and <= 200 && (_preflight.Report.Losses.Count == 0 || AcceptLossesCheck.IsChecked == true);
    private static string ToDisplayText(ExternalGenealogyExchangePreflightReport report) => $"兼容 Profile：{report.FormatProfile}\n可映射人物：{report.People}；关系：{report.Relationships}；婚配：{report.Unions}\n\n错误：{(report.Errors.Count == 0 ? "无" : string.Join("\n", report.Errors.Select(issue => $"- [{issue.Code}] {issue.Message}")))}\n\n损失报告：{(report.Losses.Count == 0 ? "无" : string.Join("\n", report.Losses.Select(issue => $"- [{issue.Code}] {issue.Message}")))}";
    private static void ShowFailure(string title, Exception exception) => MessageBox.Show($"{title}：{exception.Message}", "外部格式导入", MessageBoxButton.OK, MessageBoxImage.Error);
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
