using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    private readonly DataService _dataService;
    private readonly PrintService _printService;
    internal Func<Microsoft.Win32.SaveFileDialog>? PdfSaveDialogFactory { get; set; }
    internal Func<LayoutResult, LayoutSettings, string, bool>? PdfExportOverride { get; set; }
    private readonly SettingsService _settingsService = new();
    private List<FixedPage> _pages = new();
    private LayoutResult _lastLayoutResult = new();
    private List<FamilyMember> _allMembers = new();

    // 打印范围
    private int? _printRootMemberId;
    private int _printGenerations = 20;

    // 位置偏移（拖动）
    private Dictionary<int, (double dx, double dy)> _positionOffsets = new();

    // 排除的成员
    private HashSet<int> _excludedMemberIds = new();

    [ObservableProperty] private LayoutSettings _settings = new();
    [ObservableProperty] private FrameworkElement? _previewContent;
    [ObservableProperty] private double _zoomLevel = 1.0;
    [ObservableProperty] private string _statusText = "预览就绪";
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalPages = 1;

    public Dictionary<int, (double dx, double dy)> PositionOffsets => _positionOffsets;
    public HashSet<int> ExcludedMemberIds => _excludedMemberIds;
    public LayoutResult LastLayoutResult => _lastLayoutResult;
    public List<FamilyMember> AllMembers => _allMembers;

    public PreviewViewModel(DataService dataService, PrintService printService)
    {
        _dataService = dataService;
        _printService = printService;

        // 加载上次的排版设置
        Settings = _settingsService.Load();

        Settings.PropertyChanged += (s, e) =>
        {
            _settingsService.Save(Settings);
            RefreshPreview();
        };
    }

    public async Task InitializeAsync()
    {
        await RefreshPreviewAsync();
    }

    [RelayCommand]
    private async Task RefreshPreviewAsync()
    {
        try
        {
            _allMembers = await _dataService.GetAllMembersAsync();
            if (_allMembers.Count == 0)
            {
                StatusText = "没有成员数据";
                PreviewContent = null;
                return;
            }

            // 过滤打印范围
            var filteredMembers = FilterMembersByRange(_allMembers);

            // 排除未选中成员
            if (_excludedMemberIds.Count > 0)
            {
                filteredMembers = filteredMembers.Where(m => !_excludedMemberIds.Contains(m.Id)).ToList();
            }

            if (filteredMembers.Count == 0)
            {
                StatusText = "筛选后没有可显示的成员";
                PreviewContent = null;
                return;
            }

            var layoutService = new TreeLayoutService(Settings);
            _lastLayoutResult = layoutService.CalculateLayout(filteredMembers);
            _pages = _printService.CreatePrintPages(_lastLayoutResult, Settings, _positionOffsets);

            TotalPages = _pages.Count;
            CurrentPage = 1;
            ShowPage(1);

            string rangeInfo = _printRootMemberId != null
                ? $"范围: 从第{_printRootMemberId}号起 {_printGenerations}代"
                : "全部";
            StatusText = $"预览: {filteredMembers.Count} 位成员, {TotalPages} 页, {rangeInfo}";
        }
        catch (Exception ex)
        {
            StatusText = $"预览失败: {ex.Message}";
        }
    }

    private List<FamilyMember> FilterMembersByRange(List<FamilyMember> allMembers)
    {
        if (_printRootMemberId == null)
            return allMembers;

        // BFS 从指定根成员开始，向下N代
        var result = new List<FamilyMember>();
        var root = allMembers.FirstOrDefault(m => m.Id == _printRootMemberId);
        if (root == null) return allMembers;

        var queue = new Queue<(FamilyMember member, int depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (member, depth) = queue.Dequeue();
            result.Add(member);

            if (depth < _printGenerations - 1)
            {
                var children = allMembers.Where(m => m.FatherId == member.Id);
                foreach (var child in children)
                {
                    queue.Enqueue((child, depth + 1));
                }
            }
        }

        return result;
    }

    public void SetPrintRange(int? rootMemberId, int generations)
    {
        _printRootMemberId = rootMemberId;
        _printGenerations = generations;
        RefreshPreview();
    }

    public void ClearPositionOffsets()
    {
        _positionOffsets.Clear();
        RefreshPreview();
    }

    public void UpdatePositionOffset(int memberId, double dx, double dy)
    {
        _positionOffsets[memberId] = (dx, dy);
    }

    private void ShowPage(int pageNumber)
    {
        if (pageNumber >= 1 && pageNumber <= _pages.Count)
        {
            PreviewContent = _pages[pageNumber - 1];
            CurrentPage = pageNumber;
        }
    }

    [RelayCommand]
    private void FirstPage() => ShowPage(1);

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1) ShowPage(CurrentPage - 1);
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages) ShowPage(CurrentPage + 1);
    }

    [RelayCommand]
    private void LastPage() => ShowPage(TotalPages);

    private void RefreshPreview()
    {
        _ = RefreshPreviewAsync();
    }

    private string GetOrientationText()
    {
        return Settings.Orientation == PageOrientation.Landscape ? "横向" : "纵向";
    }

    [RelayCommand]
    private void ZoomIn() => ZoomLevel = Math.Min(ZoomLevel + 0.1, 3.0);

    [RelayCommand]
    private void ZoomOut() => ZoomLevel = Math.Max(ZoomLevel - 0.1, 0.2);

    [RelayCommand]
    private void ZoomFit() => ZoomLevel = 1.0;

    [RelayCommand]
    private void Print()
    {
        using var operation = TryEnterOutputOperation();
        if (operation is null) return;
        if (_pages.Count > 0)
        {
            var result = _printService.Print(_pages, Settings.Title);
            StatusText = result ? "打印完成" : "打印已取消";
        }
    }

    [RelayCommand]
    private void ExportXps()
    {
        using var operation = TryEnterOutputOperation();
        if (operation is null) return;
        if (_pages.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出为 XPS",
            Filter = "XPS 文件|*.xps",
            FileName = $"{Settings.Title}.xps"
        };

        if (dialog.ShowDialog() == true)
        {
            var result = _printService.ExportToXps(_pages, dialog.FileName);
            StatusText = result ? $"已导出到: {dialog.FileName}" : "导出失败";
        }
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        try
        {
            using var operation = _dataService.EnterOperation();
            var allMembers = await _dataService.GetAllMembersAsync();
            if (allMembers.Count == 0)
            {
                StatusText = "没有成员数据";
                return;
            }

            var dialog = PdfSaveDialogFactory?.Invoke() ?? new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出为 PDF",
                Filter = "PDF 文件|*.pdf",
                FileName = $"{Settings.Title}.pdf"
            };

            if (dialog.ShowDialog() != true) return;

            var filteredMembers = FilterMembersByRange(allMembers);
            if (_excludedMemberIds.Count > 0)
                filteredMembers = filteredMembers.Where(m => !_excludedMemberIds.Contains(m.Id)).ToList();

            var layoutService = new TreeLayoutService(Settings);
            var layoutResult = layoutService.CalculateLayout(filteredMembers);
            var result = (PdfExportOverride ?? ((layout, settings, path) => new PdfExportService().ExportToPdf(layout, settings, path)))
                (layoutResult, Settings, dialog.FileName);
            StatusText = result ? $"已导出 PDF: {dialog.FileName}" : "导出 PDF 失败";
        }
        catch (Exception ex)
        {
            StatusText = $"导出 PDF 失败: {ex.Message}";
        }
    }

    private IDisposable? TryEnterOutputOperation()
    {
        try { return _dataService.EnterOperation(); }
        catch (InvalidOperationException ex) { StatusText = $"无法开始输出: {ex.Message}"; return null; }
    }
}
