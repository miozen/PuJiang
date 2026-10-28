using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.ViewModels;

public partial class MemberTreeNode : ObservableObject
{
    [ObservableProperty] private FamilyMember _member = null!;
    [ObservableProperty] private bool _isExpanded = true;
    public ObservableCollection<MemberTreeNode> Children { get; } = new();
}

public partial class MainViewModel : ObservableObject
{
    private readonly DataService _dataService;
    internal Func<OpenFileDialog>? ExcelImportDialogFactory { get; set; }
    internal Func<OpenFileDialog>? GedcomImportDialogFactory { get; set; }
    internal Func<SaveFileDialog>? GedcomSaveDialogFactory { get; set; }
    internal Func<List<FamilyMember>, string, Task>? GedcomExportOverride { get; set; }

    [ObservableProperty]
    private ObservableCollection<MemberTreeNode> _treeNodes = new();

    [ObservableProperty]
    private MemberTreeNode? _selectedNode;

    [ObservableProperty]
    private string _statusText = "就绪";

    public MainViewModel(DataService dataService)
    {
        _dataService = dataService;
    }

    public async Task<List<FamilyMember>> GetAllMembersAsync()
    {
        return await _dataService.GetAllMembersAsync();
    }

    public async Task LoadDataAsync()
    {
        using var operation = _dataService.EnterOperation();
        try
        {
            var allMembers = await _dataService.GetAllMembersAsync();
            var memberDict = allMembers.ToDictionary(m => m.Id);
            var nodeDict = new Dictionary<int, MemberTreeNode>();

            // 创建所有节点
            foreach (var member in allMembers)
            {
                nodeDict[member.Id] = new MemberTreeNode { Member = member };
            }

            // 构建树
            var roots = new ObservableCollection<MemberTreeNode>();
            foreach (var member in allMembers)
            {
                if (member.FatherId != null && nodeDict.ContainsKey(member.FatherId.Value))
                {
                    nodeDict[member.FatherId.Value].Children.Add(nodeDict[member.Id]);
                }
                else
                {
                    roots.Add(nodeDict[member.Id]);
                }
            }

            TreeNodes = roots;
            StatusText = $"已加载 {allMembers.Count} 位成员";
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddRootMemberAsync()
    {
        using var operation = _dataService.EnterOperation();
        var member = new FamilyMember
        {
            Name = "新成员",
            Generation = 1,
            BirthOrder = await _dataService.GetNextBirthOrderAsync(null)
        };

        await _dataService.AddMemberAsync(member);
        await LoadDataAsync();
        StatusText = $"已添加根成员: {member.Name}";
    }

    [RelayCommand]
    private async Task AddChildMemberAsync()
    {
        using var operation = _dataService.EnterOperation();
        if (SelectedNode == null)
        {
            MessageBox.Show("请先选择一个成员", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var parent = SelectedNode.Member;
        var member = new FamilyMember
        {
            Name = "新成员",
            FatherId = parent.Id,
            Generation = parent.Generation + 1,
            BirthOrder = await _dataService.GetNextBirthOrderAsync(parent.Id)
        };

        await _dataService.AddMemberAsync(member);
        SelectedNode.IsExpanded = true;
        await LoadDataAsync();
        StatusText = $"已添加子成员: {member.Name}";
    }

    [RelayCommand]
    private async Task EditMemberAsync()
    {
        if (SelectedNode == null)
        {
            MessageBox.Show("请先选择一个成员", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 实际编辑在 View 层通过对话框完成
        OnEditMemberRequested?.Invoke(this, SelectedNode.Member);
    }

    [RelayCommand]
    private async Task DeleteMemberAsync()
    {
        using var operation = _dataService.EnterOperation();
        if (SelectedNode == null)
        {
            MessageBox.Show("请先选择一个成员", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"确定要删除成员 \"{SelectedNode.Member.Name}\" 吗？\n其子成员将变为根成员。",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _dataService.DeleteMemberAsync(SelectedNode.Member.Id);
            await LoadDataAsync();
            StatusText = "已删除成员";
        }
    }

    // 事件：请求编辑成员（由 View 层处理对话框显示）
    public event EventHandler<FamilyMember>? OnEditMemberRequested;

    [RelayCommand]
    private void OpenPreview()
    {
        OnOpenPreviewRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OnOpenPreviewRequested;

    [RelayCommand]
    private async Task ImportFromExcelAsync()
    {
        using var dialogOperation = _dataService.EnterOperation();
        var dialog = ExcelImportDialogFactory?.Invoke() ?? new OpenFileDialog
        {
            Title = "导入 Excel 数据",
            Filter = "Excel 文件|*.xlsx;*.xls"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            using var operation = _dataService.EnterOperation();
            var excelService = new ExcelService();
            var members = await excelService.ImportFromExcelAsync(dialog.FileName);

            await _dataService.ReplaceMembersAsync(members);

            await LoadDataAsync();
            StatusText = $"已导入 {members.Count} 位成员";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "导入失败";
        }
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
    {
        using var operation = _dataService.EnterOperation();
        var allMembers = await _dataService.GetAllMembersAsync();
        if (allMembers.Count == 0)
        {
            MessageBox.Show("没有成员数据可导出", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出 Excel 数据",
            Filter = "Excel 文件|*.xlsx",
            FileName = "族谱数据.xlsx"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await ExportExcelFileAsync(allMembers, dialog.FileName);
            StatusText = $"已导出到: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "导出失败";
        }
    }

    internal async Task ExportExcelFileAsync(List<FamilyMember> members, string path,
        Func<List<FamilyMember>, string, Task>? export = null)
    {
        using var operation = _dataService.EnterOperation();
        await (export ?? ((items, file) => new ExcelService().ExportToExcelAsync(items, file)))(members, path);
    }

    [RelayCommand]
    private async Task ImportFromGedcomAsync()
    {
        using var dialogOperation = _dataService.EnterOperation();
        var dialog = GedcomImportDialogFactory?.Invoke() ?? new OpenFileDialog
        {
            Title = "导入 GEDCOM 数据",
            Filter = "GEDCOM 文件|*.ged;*.gedcom"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            using var operation = _dataService.EnterOperation();
            var gedcomService = new GedcomService();
            var members = await gedcomService.ImportFromGedcomAsync(dialog.FileName);

            await _dataService.ReplaceMembersAsync(members);

            await LoadDataAsync();
            StatusText = $"已导入 {members.Count} 位成员";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "导入失败";
        }
    }

    [RelayCommand]
    private async Task ExportToGedcomAsync()
    {
        using var operation = _dataService.EnterOperation();
        var allMembers = await _dataService.GetAllMembersAsync();
        if (allMembers.Count == 0)
        {
            MessageBox.Show("没有成员数据可导出", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = GedcomSaveDialogFactory?.Invoke() ?? new SaveFileDialog
        {
            Title = "导出 GEDCOM 数据",
            Filter = "GEDCOM 文件|*.ged",
            FileName = "族谱数据.ged"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await ExportGedcomFileAsync(allMembers, dialog.FileName, GedcomExportOverride);
            StatusText = $"已导出到: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "导出失败";
        }
    }

    internal async Task ExportGedcomFileAsync(List<FamilyMember> members, string path,
        Func<List<FamilyMember>, string, Task>? export = null)
    {
        using var operation = _dataService.EnterOperation();
        await (export ?? ((items, file) => new GedcomService().ExportToGedcomAsync(items, file)))(members, path);
    }

    [RelayCommand]
    private void ExportCollaboration()
    {
        OnExportCollaborationRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ImportCollaboration()
    {
        OnImportCollaborationRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OnExportCollaborationRequested;
    public event EventHandler? OnImportCollaborationRequested;
}
