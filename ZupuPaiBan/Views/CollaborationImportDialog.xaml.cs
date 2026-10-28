using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.Views;

public partial class CollaborationImportDialog : Window
{
    private readonly List<FamilyMember> _allMembers;
    private readonly DataService _dataService;
    private string? _selectedFilePath;
    private int? _targetParentId;
    private CollaborationPackage? _package;
    internal Func<Microsoft.Win32.OpenFileDialog>? OpenFileDialogFactory { get; set; }

    public CollaborationImportDialog(List<FamilyMember> allMembers, DataService dataService)
    {
        InitializeComponent();
        _allMembers = allMembers;
        _dataService = dataService;
        BuildTargetTree();
    }

    private void BuildTargetTree()
    {
        treeTarget.Items.Clear();
        var nodeDict = _allMembers.ToDictionary(m => m.Id, m => new TreeViewItem
        {
            Header = $"{m.Name} (第{m.Generation}代)",
            Tag = m.Id
        });

        foreach (var member in _allMembers)
        {
            var node = nodeDict[member.Id];
            if (member.FatherId != null && nodeDict.ContainsKey(member.FatherId.Value))
                nodeDict[member.FatherId.Value].Items.Add(node);
            else
                treeTarget.Items.Add(node);
        }

        foreach (TreeViewItem item in treeTarget.Items)
            item.IsExpanded = true;
    }

    private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
    {
        IDisposable operation;
        try { operation = _dataService.EnterOperation(); }
        catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "无法选择文件", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        using var operationLease = operation;
        var dialog = OpenFileDialogFactory?.Invoke() ?? new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择协作文件",
            Filter = "协作 JSON 文件|*.json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            _package = JsonSerializer.Deserialize<CollaborationPackage>(json);
            if (_package == null || _package.Members.Count == 0)
            {
                MessageBox.Show("文件中没有成员数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _selectedFilePath = dialog.FileName;
            txtFileName.Text = Path.GetFileName(dialog.FileName);
            txtFileName.Foreground = new SolidColorBrush(Colors.Black);

            // Show preview
            listPreview.Items.Clear();
            foreach (var m in _package.Members)
            {
                var genderStr = m.Gender == "Female" ? "女" : "男";
                var tb = new TextBlock
                {
                    Text = $"  {m.Name} ({genderStr}) {m.BirthDate}  配偶: {m.SpouseName ?? "无"}",
                    Margin = new Thickness(0, 2, 0, 2)
                };
                listPreview.Items.Add(tb);
            }

            CheckEnableImport();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"读取文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TreeTarget_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is int memberId)
        {
            _targetParentId = memberId;
            CheckEnableImport();
        }
    }

    private void CheckEnableImport()
    {
        btnImport.IsEnabled = _selectedFilePath != null && _targetParentId != null;
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFilePath == null || _targetParentId == null) return;

        IDisposable operation;
        try { operation = _dataService.EnterOperation(); }
        catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "无法导入", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        using var operationLease = operation;

        var targetMember = _allMembers.FirstOrDefault(m => m.Id == _targetParentId);
        var result = MessageBox.Show(
            $"确定将 {_package?.Members.Count ?? 0} 位成员导入到 \"{targetMember?.Name}\" 旗下吗？",
            "确认导入", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            int count = await ImportAsync(_selectedFilePath, _targetParentId.Value);
            MessageBox.Show($"导入成功！共导入 {count} 位成员", "协作导入", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    internal async Task<int> ImportAsync(string path, int targetParentId)
    {
        using var operation = _dataService.EnterOperation();
        return await new CollaborationService().ImportSubtreeJson(path, targetParentId, _dataService);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
