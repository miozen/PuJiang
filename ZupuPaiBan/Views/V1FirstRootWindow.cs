using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PuJiang.V1.Application;
using PuJiang.Domain;
using ZupuPaiBan.Services;
using System.Windows.Shapes;

namespace ZupuPaiBan.Views;

// Isolated sample surface. The existing workbench and its legacy data path remain untouched.
public sealed class V1FirstRootWindow : Window
{
    private readonly FirstRootApplication _app;
    private readonly ChildApplication? _children;
    private readonly SpouseApplication? _spouses;
    private readonly PersonProfileApplication? _profiles;
    private readonly GenerationApplication? _generations;
    private readonly ChildOrderApplication? _childOrders;
    private readonly BranchApplication? _branches;
    private readonly Dictionary<Guid, ChildOrderSnapshot> _orderSnapshots = new();
    private readonly Dictionary<Guid, string> _orderIssues = new();
    private MoveChildOrder? _pendingChildOrder;
    private DetachBranch? _pendingDetach;
    private AttachBranch? _pendingAttach;
    private (Guid Lineage, Guid Source, Guid Target)? _lastAttachSelection;
    private DeleteBranch? _pendingDelete;
    private DeleteSpouse? _pendingSpouseDelete;
    private UpdateRootParents? _pendingRootParents;
    private TextBox? _rootFatherInput, _rootMotherInput;
    private bool _branchUnknown;
    private bool _childOrderUnknown;
    private Point? _childDragStart;
    private ChildOrderSnapshot? _childDragSnapshot;
    private readonly Guid _genealogy;
    private Guid _lineage;
    private readonly LineageApplication? _lineages;
    private readonly ListBox _lineageList = new() { DisplayMemberPath = "Label", Margin = new Thickness(0, 8, 0, 0), MaxHeight = 180 };
    private readonly StackPanel _home = new() { Margin = new Thickness(8), Visibility = Visibility.Collapsed };
    private DockPanel? _canvasPanel;
    private bool _navigation;
    private readonly string _surname;
    private readonly Canvas _canvas = new() { Background = Brushes.White, MinWidth = 650, MinHeight = 360 };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Canvas _generationHeader = new() { Height = 26, Background = Brushes.WhiteSmoke, ClipToBounds = true, IsHitTestVisible = false };
    private int? _headerFirstGeneration, _headerLastGeneration;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TextBlock _status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _details = new() { Text = "未选中人物", TextWrapping = TextWrapping.Wrap };
    private readonly Button _create = new() { Content = "添加首节点", Padding = new Thickness(6) };
    private readonly Button _reload = new() { Content = "重新读取", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
    private readonly Button _retry = new() { Content = "重试保存", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _abandonOrder = new() { Content = "放弃排行调整", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _rebindChild = new() { Content = "重新绑定子女草稿", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _addChild = new() { Content = "添加子女", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
    private readonly Button _addSibling = new() { Content = "添加兄弟姐妹", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
    private readonly Button _addSpouse = new() { Content = "添加配偶", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
    private readonly StackPanel _familyDetails = new();
    private TextBox? _input;
    private TextBox? _generationInput;
    private TextBlock? _generationLabel;
    private int? _draftParentGeneration;
    private StackPanel? _inputHost;
    private AddFirstRoot? _pending;
    private AddChild? _pendingChild;
    private AddSibling? _pendingSibling;
    private AddSibling? _siblingDraft;
    private AddSpouse? _pendingSpouse;
    private SetChildMother? _pendingMother;
    private UpdatePersonProfile? _pendingProfile;
    private MoveSpouse? _pendingMove;
    private bool _profileDirty;
    private PersonSex? _selectedSex;
    private int _detailsVersion;
    private enum InputKind { Root, Child, Sibling, Spouse }
    private InputKind _inputKind;
    private IReadOnlyList<RootNode> _rootNodes = Array.Empty<RootNode>();
    private IReadOnlyList<ChildNode> _childNodes = Array.Empty<ChildNode>();
    private IReadOnlyList<SpouseNode> _visibleSpouses = Array.Empty<SpouseNode>();
    private Point? _draftLocation;
    private readonly Guid _draftId = Guid.NewGuid();
    private bool HasPending => _pending is not null || _pendingChild is not null || _pendingSibling is not null || _pendingSpouse is not null || _pendingMother is not null || _pendingProfile is not null || _pendingMove is not null || _pendingChildOrder is not null || _pendingDetach is not null || _pendingAttach is not null || _pendingDelete is not null || _pendingSpouseDelete is not null || _pendingRootParents is not null;
    private bool _saving, _uncertain, _hasRoot, _loadedOk;
    private Guid? _personId, _placementId;
    private Point? _panStart;
    private Point _panOffset;
    private bool _dragged;
    private long _spouseRevision, _draftSpouseRevision, _dragSpouseRevision;
    private Point? _spouseDragStart;
    private double _contentWidth = 650, _contentHeight = 360;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal V1FirstRootWindow(FirstRootApplication app, Guid genealogy, Guid lineage, string surname, ChildApplication? children = null, SpouseApplication? spouses = null, PersonProfileApplication? profiles = null, LineageApplication? lineages = null, GenerationApplication? generations = null, ChildOrderApplication? childOrders = null, BranchApplication? branches = null)
    {
        _app = app; _genealogy = genealogy; _lineage = lineage; _surname = surname;
        _children = children; _spouses = spouses; _profiles = profiles; _lineages = lineages; _generations = generations; _childOrders = childOrders; _branches = branches;
        Title = "世系编修工作台 — V1 虚构隔离样本"; Width = 1100; Height = 720; MinWidth = 760; MinHeight = 480;
        var outer = new DockPanel(); DockPanel.SetDock(_status, Dock.Bottom); outer.Children.Add(_status);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(210) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(300) });
        var left = new StackPanel { Margin = new Thickness(12) }; left.Children.Add(new TextBlock { Text = "谱系管理", FontSize = 17 }); left.Children.Add(new TextBlock { Text = "虚构修谱项目", Margin = new Thickness(0, 12, 0, 0) });
        if (_lineages is not null) { var addLineage = new Button { Content="新建谱系", Margin=new Thickness(0,8,0,0), Padding=new Thickness(6) }; left.Children.Add(addLineage); left.Children.Add(_lineageList); addLineage.Click += async (_,_) => await CreateLineageAsync(); _lineageList.SelectionChanged += async (_,_) => { if (!_navigation && _lineageList.SelectedItem is LineageSummary item && item.Id != _lineage) await SwitchLineageAsync(item.Id); }; }
        grid.Children.Add(left);
        var center = new DockPanel { Margin = new Thickness(8) }; _canvasPanel = center; Grid.SetColumn(center, 1);
        var notice = new TextBlock { Text = "隔离虚构样本；重新进入时可打开上次使用的样本。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(notice, Dock.Top); center.Children.Add(notice);
        var tools = new WrapPanel(); tools.Children.Add(_create); tools.Children.Add(_addChild); tools.Children.Add(_addSibling); tools.Children.Add(_addSpouse); tools.Children.Add(_reload); tools.Children.Add(_retry); tools.Children.Add(_abandonOrder); tools.Children.Add(_rebindChild); DockPanel.SetDock(tools, Dock.Top); center.Children.Add(tools);
        DockPanel.SetDock(_generationHeader, Dock.Top); center.Children.Add(_generationHeader);
        _canvas.LayoutTransform = _scale; _scroll.Content = _canvas; center.Children.Add(_scroll); grid.Children.Add(center); Grid.SetColumn(_home, 1); grid.Children.Add(_home);
        _scroll.SizeChanged += (_, _) => FillCanvasViewport();
        _scroll.ScrollChanged += (_, _) => { FillCanvasViewport(); UpdateGenerationHeader(); };
        _generationHeader.SizeChanged += (_, _) => UpdateGenerationHeader();
        var right = new StackPanel { Margin = new Thickness(12) }; right.Children.Add(new TextBlock { Text = "人物资料", FontSize = 17 }); right.Children.Add(_details); right.Children.Add(_familyDetails); Grid.SetColumn(right, 2); grid.Children.Add(right);
        outer.Children.Add(grid); Content = outer;
        _create.Click += (_, _) => BeginNamed();
        _reload.Click += async (_, _) => await ReloadAsync();
        _addChild.Click += (_, _) => BeginFamilyInput(InputKind.Child);
        _addSibling.Click += (_, _) => BeginSiblingInput();
        _addSpouse.Click += (_, _) => BeginFamilyInput(InputKind.Spouse);
        _retry.Click += async (_, _) => { if (_pendingDetach is not null || _pendingAttach is not null || _pendingDelete is not null || _pendingSpouseDelete is not null || _pendingRootParents is not null) await SubmitBranchAsync(); else if (_pendingChildOrder is not null) await SubmitChildOrderAsync(); else if (_pending is not null) await SubmitPendingAsync(); else await SubmitFamilyAsync(); };
        _abandonOrder.Click += async (_, _) => { if (_saving || _childOrderUnknown) return; _pendingChildOrder=null; UpdateButtons(); try { await ReloadCoreAsync(); _status.Text="已放弃排行调整并重新读取。"; } catch(Exception error) { _status.Text=$"重新读取失败：{error.Message}"; } };
        _rebindChild.Click += async (_, _) => await RebindChildAsync();
        _canvas.MouseRightButtonDown += (_, e) => { if (e.OriginalSource != _canvas || _hasRoot || _saving || _pending is not null) return; e.Handled = true; var menu = new ContextMenu(); var item = new MenuItem { Header = "添加首节点" }; item.Click += (_, _) => BeginNamed(); menu.Items.Add(item); menu.PlacementTarget = _canvas; menu.IsOpen = true; };
        _scroll.PreviewMouseWheel += (_, e) => { var point = e.GetPosition(_scroll); var old = _scale.ScaleX; var next = Math.Clamp(old * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.4, 2.5); var x = (_scroll.HorizontalOffset + point.X) / old; var y = (_scroll.VerticalOffset + point.Y) / old; _scale.ScaleX = _scale.ScaleY = next; _scroll.UpdateLayout(); _scroll.ScrollToHorizontalOffset(x * next - point.X); _scroll.ScrollToVerticalOffset(y * next - point.Y); UpdateGenerationHeader(); e.Handled = true; };
        _canvas.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource != _canvas || _saving) return; _panStart = e.GetPosition(_scroll); _panOffset = new(_scroll.HorizontalOffset, _scroll.VerticalOffset); _dragged = false; _canvas.CaptureMouse(); e.Handled = true; };
        _canvas.MouseMove += (_, e) => { if (_panStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return; var delta = e.GetPosition(_scroll) - start; if (Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance) _dragged = true; if (_dragged) { _scroll.ScrollToHorizontalOffset(_panOffset.X - delta.X); _scroll.ScrollToVerticalOffset(_panOffset.Y - delta.Y); } };
        _canvas.MouseLeftButtonUp += async (_, e) => { if (_panStart is null) return; _panStart = null; _canvas.ReleaseMouseCapture(); e.Handled = true; if (_dragged) return; if (_input is not null && !_uncertain) await ConfirmNamedAsync(); else if (_input is null && !_uncertain && !_profileDirty) ClearFocus(); };
        _canvas.LostMouseCapture += (_, _) => _panStart = null;
        Loaded += async (_, _) => { try { await ReloadAsync(); if (_lineages is not null) await ReloadLineagesAsync(); } finally { _ready.TrySetResult(); } };
        Closing += OnClosing;
    }

    private async Task ReloadLineagesAsync()
    {
        if (_lineages is null) return;
        var items=await _lineages.ListAsync(_genealogy); _navigation=true;
        try { _lineageList.ItemsSource=items; _lineageList.SelectedItem=items.FirstOrDefault(x=>x.Id==_lineage); }
        finally { _navigation=false; }
    }
    private async Task<bool> PrepareNavigationAsync()
    {
        if (_saving || HasPending || _uncertain) { _status.Text="请等待或重试当前保存。"; return false; }
        if (_input is not null || _profileDirty)
        {
            if (MessageBox.Show(this,"当前有未保存信息，是否保存后切换？", "保存提示",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return false;
            if (_input is not null) await ConfirmNamedAsync();
            if (_input is not null || _profileDirty || HasPending || _saving) { _status.Text="请完成当前输入并保存后再切换。"; return false; }
        }
        return true;
    }
    private async Task CreateLineageAsync()
    {
        if (_lineages is null || _navigation || !await PrepareNavigationAsync()) return;
        var dialog=new V1LineageCreateDialog(_lineages,_genealogy,_surname) { Owner=this }; if (dialog.ShowDialog()!=true || dialog.Command is null) return;
        var previous=_lineage;
        try { _navigation=true; _lineage=dialog.CreatedId; ClearFocus(); await ReloadCoreAsync(); _home.Visibility=Visibility.Collapsed; if(_canvasPanel is not null)_canvasPanel.Visibility=Visibility.Visible; _navigation=false; await ReloadLineagesAsync(); await PromptRootAsync(); }
        catch(Exception error) { _lineage=previous; try { await ReloadCoreAsync(); await ReloadLineagesAsync(); } catch { _loadedOk=false; UpdateButtons(); } _status.Text=$"谱系已创建，但读取失败：{error.Message}。请重新选择。"; }
        finally { _navigation=false; }
    }
    private async Task SwitchLineageAsync(Guid target)
    {
        if (_lineages is null || _navigation) return;
        _navigation=true; _lineageList.IsEnabled=false;
        try { if (!await PrepareNavigationAsync()) return; var home=await _lineages.ReadHomeAsync(_genealogy,target); _lineage=target; ClearFocus(); _rootNodes=Array.Empty<RootNode>(); _childNodes=Array.Empty<ChildNode>(); _loadedOk=false; ShowLineageHome(home); }
        catch(Exception error) { _status.Text=$"切换失败：{error.Message}"; }
        finally { _navigation=false; _lineageList.IsEnabled=true; await ReloadLineagesAsync(); }
    }
    private void ShowLineageHome(LineageHome home)
    {
        _home.Children.Clear(); if(_canvasPanel is not null)_canvasPanel.Visibility=Visibility.Collapsed; _home.Visibility=Visibility.Visible;
        _home.Children.Add(new TextBlock { Text=home.Name, FontSize=20 });
        var edit=new Button { Content="修改谱系资料", HorizontalAlignment=HorizontalAlignment.Left, Padding=new Thickness(6) }; _home.Children.Add(edit);
        edit.Click += async (_,_) => { if(_lineages is null || _navigation)return; var dialog=new V1LineageEditDialog(_lineages,_genealogy,home) {Owner=this}; if(dialog.ShowDialog()==true){ try {ShowLineageHome(await _lineages.ReadHomeAsync(_genealogy,_lineage)); await ReloadLineagesAsync();}catch(Exception error){_status.Text=$"已保存，重新读取失败：{error.Message}";} } };
        _home.Children.Add(new TextBlock { Text=$"分类：{home.Category}　地区：{home.Region}\n主题：{home.Topic}　项目姓氏：{home.Surname}\n郡望堂号：{home.Hall}\n备注：{home.Notes}", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,8) });
        var male=home.Total==0?0:(double)home.Male/home.Total*100; var female=home.Total==0?0:(double)home.Female/home.Total*100;
        _home.Children.Add(new TextBlock { Text=$"人物总数：{home.Total}　男：{home.Male}（{male:0.#}%）　女：{home.Female}（{female:0.#}%）　未知：{home.Unknown}" });
        var enter=new Button { Content="进入修谱", Margin=new Thickness(0,10,0,5), Padding=new Thickness(6), HorizontalAlignment=HorizontalAlignment.Left }; _home.Children.Add(enter); enter.Click += async (_,_) => await EnterCanvasAsync(null);
        _home.Children.Add(new TextBlock { Text="首节点", Margin=new Thickness(0,8,0,4), FontWeight=FontWeights.Bold });
        if(home.Roots.Count==0) _home.Children.Add(new TextBlock { Text="暂无首节点" });
        foreach(var root in home.Roots) { var button=new Button { Content=root.Name.Length==0?"（空姓名）":root.Name, Margin=new Thickness(0,2,0,2), HorizontalAlignment=HorizontalAlignment.Left }; _home.Children.Add(button); button.Click += async (_,_) => await EnterCanvasAsync(root); }
        _home.Children.Add(new TextBlock { Text="操作日志（记录谱系、首节点创建及分支挂接）", Margin=new Thickness(0,10,0,4), TextWrapping=TextWrapping.Wrap, FontWeight=FontWeights.Bold });
        if(home.Activities.Count==0) _home.Children.Add(new TextBlock { Text="暂无记录" });
        foreach(var item in home.Activities) _home.Children.Add(new TextBlock { Text=$"{item.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}　{item.Kind}　{ActivityDetail(item)}", TextWrapping=TextWrapping.Wrap });
        _details.Text="未选中人物"; _familyDetails.Children.Clear(); _status.Text="已切换谱系。"; UpdateButtons();
    }
    private static string ActivityDetail(LineageActivityView item)
    {
        if (item.Kind != "挂接分支") return item.Detail;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(item.Detail);
            var root = doc.RootElement;
            var oldFather = root.GetProperty("PreviousParents").EnumerateArray().Where(x => x.GetProperty("Role").GetInt32() == (int)ParentRole.Father).Select(x => x.GetProperty("Name").GetString()).FirstOrDefault();
            return $"{root.GetProperty("ChildName").GetString()}：生父由 {oldFather ?? "未记录"} 改为 {root.GetProperty("NewFather").GetString()}";
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return "挂接记录无法显示详情"; }
    }
    private async Task EnterCanvasAsync(RootNode? root)
    {
        try { await ReloadCoreAsync(); _home.Visibility=Visibility.Collapsed; if(_canvasPanel is not null)_canvasPanel.Visibility=Visibility.Visible; if(root is not null) { _personId=root.PersonId; _placementId=root.PlacementId; UpdateSelection(); await RefreshFamilyDetailsAsync(); RenderCanvas(); } else if(!_hasRoot) await PromptRootAsync(); }
        catch(Exception error) { _status.Text=$"画布读取失败：{error.Message}"; }
    }
    private async Task PromptRootAsync()
    {
        var dialog=new V1RootDetailsDialog(_app,_genealogy,_lineage,_surname) { Owner=this }; if(dialog.ShowDialog()!=true || dialog.Command is null) return;
        if (dialog.Created is { } created) { _personId=created.PersonId; _placementId=created.PlacementId; }
        await ReloadCoreAsync();
    }

    private void BeginNamed()
    {
        if (!_loadedOk || _saving || _hasRoot || _pending is not null || _input is not null) return;
        _inputKind = InputKind.Root;
        ShowInput(true);
    }
    private void ShowInput(bool showSurname)
    {
        if (_inputKind is InputKind.Child or InputKind.Sibling) RenderCanvas(true);
        _input = new TextBox { Width = showSurname ? 78 : 110, Height = 34, Text = "", ToolTip = showSurname ? "输入名字（不含项目姓氏）" : "输入配偶完整姓名" };
        _inputHost = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.White, Width = 110, Height = 34 };
        if (showSurname) _inputHost.Children.Add(new TextBlock { Text = _surname, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        _inputHost.Children.Add(_input);
        var selectedCard = _canvas.Children.OfType<Border>().FirstOrDefault(card => card.Tag switch
        {
            RootNode root => root.PersonId == _personId && root.PlacementId == _placementId,
            ChildNode child => child.PersonId == _personId && child.PlacementId == _placementId,
            _ => false
        });
        var left = _inputKind is InputKind.Child or InputKind.Sibling && _draftLocation is Point d ? d.X : selectedCard is null || _inputKind == InputKind.Root ? 20 : Canvas.GetLeft(selectedCard);
        var top = _inputKind is InputKind.Child or InputKind.Sibling && _draftLocation is Point p ? p.Y : selectedCard is null || _inputKind == InputKind.Root ? 50 : Canvas.GetTop(selectedCard) + 42 + _visibleSpouses.Count * 38;
        Canvas.SetLeft(_inputHost, left); Canvas.SetTop(_inputHost, top); _canvas.Children.Add(_inputHost);
        if (_inputKind != InputKind.Spouse)
        {
            _draftParentGeneration = _inputKind switch
            {
                InputKind.Child => _rootNodes.Where(x => x.PlacementId == _placementId).Select(x => x.Generation).Concat(_childNodes.Where(x => x.PlacementId == _placementId).Select(x => x.Generation)).SingleOrDefault(),
                InputKind.Sibling => _rootNodes.Where(x => x.PlacementId == _siblingDraft?.ParentPlacementId).Select(x => x.Generation).Concat(_childNodes.Where(x => x.PlacementId == _siblingDraft?.ParentPlacementId).Select(x => x.Generation)).SingleOrDefault(),
                _ => null
            };
            if (_inputKind == InputKind.Root) {
            _generationLabel = new TextBlock { Text = "世代", Background = Brushes.White, Width = 36, Height = 26, Padding = new Thickness(3, 4, 0, 0) };
            Canvas.SetLeft(_generationLabel, left); Canvas.SetTop(_generationLabel, top + 35); _canvas.Children.Add(_generationLabel);
            _generationInput = new TextBox { Width = 74, Height = 26, ToolTip = "主线世代（正整数）", Text = _draftParentGeneration is int n && n < int.MaxValue ? (n + 1).ToString() : "" };
            Canvas.SetLeft(_generationInput, left + 36); Canvas.SetTop(_generationInput, top + 35); _canvas.Children.Add(_generationInput);
            _generationInput.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; if (!_uncertain) await ConfirmNamedAsync(); } else if (e.Key == Key.Escape && !_uncertain && !_saving) { e.Handled = true; CancelInput(); } };
            }
        }
        _input.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; if (!_uncertain) await ConfirmNamedAsync(); } else if (e.Key == Key.Escape && !_uncertain && !_saving) { e.Handled = true; CancelInput(); } };
        _input.Focus(); _status.Text = _inputKind == InputKind.Root ? "输入名字及主世系，Enter 或单击画布空白提交；Esc 取消。" : "输入名字，Enter 或单击画布空白提交；Esc 取消。";
    }
    private void FillCanvasViewport()
    {
        if (_scroll.ViewportWidth <= 0 || _scroll.ViewportHeight <= 0) return;
        _canvas.MinWidth = Math.Max(_contentWidth, (_scroll.ViewportWidth - 1) / _scale.ScaleX);
        _canvas.MinHeight = Math.Max(_contentHeight, (_scroll.ViewportHeight - 1) / _scale.ScaleY);
    }
    private void UpdateGenerationHeader()
    {
        _generationHeader.Children.Clear();
        if (_headerFirstGeneration is not int first || _headerLastGeneration is null || _generationHeader.ActualWidth <= 0) return;
        var step = 174 * _scale.ScaleX;
        var start = Math.Max(0L, (long)Math.Floor((_scroll.HorizontalOffset - 20 * _scale.ScaleX) / step) - 1);
        var end = Math.Min((long)int.MaxValue - first, (long)Math.Ceiling((_scroll.HorizontalOffset + _generationHeader.ActualWidth) / step));
        for (var col = start; col <= end; col++)
        {
            var text = new TextBlock { Text = $"第 {(long)first + col} 世", FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(text, (20 + col * 174) * _scale.ScaleX - _scroll.HorizontalOffset); Canvas.SetTop(text, 4); _generationHeader.Children.Add(text);
        }
    }
    private async Task ConfirmNamedAsync()
    {
        if (_input is null || _saving || _uncertain) return;
        var name = _input.Text.Trim();
        if (name.Length == 0) { _status.Text = "请输入姓名后保存，或按 Esc 取消。"; _input.Focus(); return; }
        int generation = 0;
        if (_inputKind == InputKind.Root && (!int.TryParse(_generationInput?.Text, out generation) || generation <= 0)) { _status.Text = "请输入有效的主世系。输入已保留。"; _generationInput?.Focus(); return; }
        if (_inputKind is InputKind.Child or InputKind.Sibling) { if (_draftParentGeneration is null or <= 0 or int.MaxValue) { _status.Text = "父亲世代无法确定下一世，输入已保留。"; return; } generation = _draftParentGeneration.Value + 1; }
        if (_inputKind == InputKind.Root) { _pending = new(Guid.NewGuid(), _genealogy, _lineage, RootNameMode.Named, _surname + name, generation); await SubmitPendingAsync(); }
        else if (_personId is Guid person && _placementId is Guid placement)
        {
            if (_inputKind == InputKind.Child) _pendingChild = new(Guid.NewGuid(), _genealogy, _lineage, person, placement, RootNameMode.Named, _surname + name, _draftSpouseRevision, generation, _draftParentGeneration);
            else if (_inputKind == InputKind.Sibling) { if (_siblingDraft is null) { _status.Text = "兄弟姐妹草稿已失效，请取消后重新选择。"; return; } _pendingSibling = _siblingDraft with { OperationId = Guid.NewGuid(), Name = _surname + name, Generation = generation, ExpectedParentGeneration = _draftParentGeneration }; }
            else _pendingSpouse = new(Guid.NewGuid(), _genealogy, _lineage, person, placement, RootNameMode.Named, name);
            await SubmitFamilyAsync();
        }
    }
    private void BeginSiblingInput()
    {
        if (!_loadedOk || _saving || HasPending || _profileDirty || _input is not null || _children is null || _personId is not Guid person || _placementId is not Guid placement) return;
        var source = _childNodes.SingleOrDefault(x => x.PersonId == person && x.PlacementId == placement);
        if (source is null) { _status.Text = "当前为独立根节点，不能添加兄弟姐妹。"; return; }
        var parent = _rootNodes.Where(x => x.PlacementId == source.ParentPlacementId).Select(x => (Guid?)x.PersonId)
            .Concat(_childNodes.Where(x => x.PlacementId == source.ParentPlacementId).Select(x => (Guid?)x.PersonId)).SingleOrDefault();
        if (parent is null) { _status.Text = "父位置失效，请重新读取。"; return; }
        _siblingDraft = new(Guid.Empty, _genealogy, _lineage, person, placement, parent.Value, source.ParentPlacementId, source.FatherRelationshipId, source.MotherId, source.MotherExplicitlyCleared, "");
        _inputKind = InputKind.Sibling; ShowInput(true);
    }
    private void BeginFamilyInput(InputKind kind)
    {
        if (!_loadedOk || _saving || HasPending || _profileDirty || _input is not null || _personId is null || _placementId is null || _profiles is not null && _selectedSex != PersonSex.Male) return;
        if (kind == InputKind.Child && _children is null || kind == InputKind.Spouse && _spouses is null) return;
        _rebindChild.Content = "重新绑定子女草稿"; _rebindChild.Visibility = Visibility.Collapsed; _inputKind = kind; if (kind == InputKind.Child) _draftSpouseRevision = _spouseRevision; ShowInput(kind == InputKind.Child);
    }
    private async Task RebindChildAsync()
    {
        if (_inputKind == InputKind.Sibling && !_saving && !HasPending && _input is not null && _siblingDraft is { } original && _children is not null)
        {
            try
            {
                var refreshed = (await _children.ReadAsync(_genealogy, _lineage)).SingleOrDefault(x => x.PersonId == original.SourcePersonId && x.PlacementId == original.SourcePlacementId)
                    ?? throw new InvalidOperationException("来源位置已失效，请取消后重新选择。");
                if (refreshed.ParentPlacementId != original.ParentPlacementId) throw new InvalidOperationException("父位置已变化，请取消后重新选择。");
                var roots = await _app.ReadAsync(_genealogy, _lineage);
                var allChildren = await _children.ReadAsync(_genealogy, _lineage);
                _draftParentGeneration = roots.Nodes.Where(x => x.PlacementId == original.ParentPlacementId && x.PersonId == original.ParentPersonId).Select(x => x.Generation).Concat(allChildren.Where(x => x.PlacementId == original.ParentPlacementId && x.PersonId == original.ParentPersonId).Select(x => x.Generation)).SingleOrDefault() ?? throw new InvalidOperationException("父人物世代无法确认。");
                _siblingDraft = original with { ExpectedFatherRelationshipId = refreshed.FatherRelationshipId, ExpectedMotherId = refreshed.MotherId, ExpectedMotherCleared = refreshed.MotherExplicitlyCleared };
                _rebindChild.Visibility = Visibility.Collapsed; _status.Text = $"已重新读取来源资料，父亲为第 {_draftParentGeneration} 世；输入保留，请确认后提交。"; _input.Focus();
            }
            catch (Exception error) { _status.Text = $"无法重新绑定：{error.Message}。草稿已保留。"; }
            return;
        }
        if (_saving || HasPending || _input is null || _inputKind != InputKind.Child || _spouses is null || _personId is not Guid person || _placementId is not Guid place) return;
        try
        {
            var snapshot = await _spouses.ReadSnapshotAsync(_genealogy, _lineage, person, place);
            var roots = await _app.ReadAsync(_genealogy, _lineage);
            var allChildren = _children is null ? Array.Empty<ChildNode>() : await _children.ReadAsync(_genealogy, _lineage);
            _draftParentGeneration = roots.Nodes.Where(x => x.PlacementId == place && x.PersonId == person).Select(x => x.Generation).Concat(allChildren.Where(x => x.PlacementId == place && x.PersonId == person).Select(x => x.Generation)).SingleOrDefault() ?? throw new InvalidOperationException("父人物世代无法确认。");
            _spouseRevision = _draftSpouseRevision = snapshot.Revision;
            _rebindChild.Visibility = Visibility.Collapsed;
            _status.Text = $"已重新绑定配偶顺序及父亲第 {_draftParentGeneration} 世，输入保留，请确认后提交。";
            _input.Focus();
        }
        catch (Exception error) { _status.Text = $"无法重新绑定：{error.Message}。草稿已保留。"; }
    }
    private async Task SubmitFamilyAsync()
    {
        if (_saving || !HasPending) return;
        _saving = true; UpdateButtons(); if (_input is not null) _input.IsReadOnly = true; if (_generationInput is not null) _generationInput.IsReadOnly = true;
        try
        {
            try
            {
                if (_pendingChild is { } child && _children is not null)
                {
                    var saved = await _children.ExecuteAsync(child);
                    _personId = saved.PersonId; _placementId = saved.PlacementId; _pendingChild = null;
                }
                else if (_pendingSibling is { } sibling && _children is not null)
                {
                    var saved = await _children.ExecuteSiblingAsync(sibling);
                    _personId = saved.PersonId; _placementId = saved.PlacementId; _pendingSibling = null;
                }
                else if (_pendingSpouse is { } spouse && _spouses is not null) { await _spouses.AddAsync(spouse); _pendingSpouse = null; }
                else if (_pendingMother is { } mother && _spouses is not null) { await _spouses.SetMotherAsync(mother); _pendingMother = null; }
                else if (_pendingProfile is { } profile && _profiles is not null) { await _profiles.UpdateAsync(profile); _pendingProfile = null; _profileDirty = false; }
                else if (_pendingMove is { } move && _spouses is not null) { await _spouses.MoveAsync(move); _pendingMove = null; }
                else return;
            }
            catch (Exception error)
            {
                try
                {
                    var committed = _pendingChild is { } c && _children is not null ? await _children.HasReceiptAsync(c.OperationId)
                        : _pendingSibling is { } sibling && _children is not null ? await _children.HasSiblingReceiptAsync(sibling.OperationId)
                        : _pendingSpouse is { } s && _spouses is not null ? await _spouses.HasReceiptAsync(s.OperationId, false)
                        : _pendingMother is { } m && _spouses is not null ? await _spouses.HasReceiptAsync(m.OperationId, true)
                        : _pendingProfile is { } p && _profiles is not null ? await _profiles.HasReceiptAsync(p.OperationId)
                        : _pendingMove is { } order && _spouses is not null ? await _spouses.HasOrderReceiptAsync(order.OperationId)
                        : true;
                    if (!committed)
                    {
                        var failedChild = _pendingChild; var failedSibling = _pendingSibling;
                        _pendingChild = null; _pendingSibling = null; _pendingSpouse = null; _pendingMother = null; _pendingProfile = null; _pendingMove = null; _uncertain = false;
                        if (_input is not null) _input.IsReadOnly = false; if (_generationInput is not null) _generationInput.IsReadOnly = false;
                        _status.Text = $"未保存：{error.Message}。草稿已保留，可修改后重试。";
                        if (failedSibling is not null && _input is not null) { _rebindChild.Content = "重新读取兄弟姐妹草稿"; _rebindChild.Visibility = Visibility.Visible; }
                        if (failedChild is not null && _input is not null && _spouses is not null)
                        {
                            _rebindChild.Visibility = Visibility.Visible;
                            try { var current = await _spouses.ReadSnapshotAsync(_genealogy, _lineage, failedChild.ParentPersonId, failedChild.ParentPlacementId); if (current.Revision != failedChild.ExpectedSpouseRevision) { _rebindChild.Visibility = Visibility.Visible; _status.Text = "配偶顺序已变化，草稿已保留；请重新绑定后确认提交。"; } }
                            catch { /* Preserve the original error and draft if the current context cannot be read. */ }
                        }
                        return;
                    }
                }
                catch { /* Receipt lookup failed: retain the same operation for a safe retry. */ }
                _uncertain = true; _status.Text = $"保存未确认：{error.Message}。草稿已保留，请重试同一操作。"; return;
            }
            _uncertain = false;
            if (_inputHost is not null) _canvas.Children.Remove(_inputHost);
            if (_generationInput is not null) _canvas.Children.Remove(_generationInput); if (_generationLabel is not null) _canvas.Children.Remove(_generationLabel); _generationInput = null; _generationLabel = null;
            _inputHost = null; _input = null; _siblingDraft = null;
            _rebindChild.Visibility = Visibility.Collapsed;
            try { await ReloadCoreAsync(); _status.Text = "已保存并重新读取。"; }
            catch (Exception error) { _status.Text = $"已保存，但画布读取失败：{error.Message}。请重新读取。"; }
        }
        finally { _saving = false; UpdateButtons(); }
    }
    private async Task SubmitPendingAsync()
    {
        if (_saving || _pending is not { } command) return;
        _saving = true; UpdateButtons(); if (_input is not null) _input.IsReadOnly = true; if (_generationInput is not null) _generationInput.IsReadOnly = true;
        try
        {
            RootCreated saved;
            try { saved = await _app.ExecuteAsync(command); }
            catch (Exception error)
            {
                // A completed read of an empty isolated lineage proves this attempt did not commit.
                // If reading fails or a root exists, retain the operation ID and replay it unchanged.
                try
                {
                    if ((await _app.ReadAsync(_genealogy, _lineage)).Nodes.Count == 0)
                    {
                        _pending = null; _uncertain = false;
                        if (_input is not null) _input.IsReadOnly = false; if (_generationInput is not null) _generationInput.IsReadOnly = false;
                        _status.Text = $"未保存：{error.Message}。输入已保留，可修改后重试。";
                        return;
                    }
                }
                catch { /* The write outcome is still unknown; do not generate a new operation. */ }
                _uncertain = true; _status.Text = $"保存未确认：{error.Message}。输入已保留，请重试同一操作。"; return;
            }
            _pending = null; _uncertain = false; _personId = saved.PersonId; _placementId = saved.PlacementId; _hasRoot = true;
            if (_inputHost is not null) _canvas.Children.Remove(_inputHost);
            if (_generationInput is not null) _canvas.Children.Remove(_generationInput); if (_generationLabel is not null) _canvas.Children.Remove(_generationLabel); _generationInput = null; _generationLabel = null;
            _inputHost = null; _input = null;
            try { await ReloadCoreAsync(); _status.Text = "首节点已保存并选中。"; }
            catch (Exception error) { _status.Text = $"已保存，但画布读取失败：{error.Message}。请重新读取。"; }
        }
        finally { _saving = false; UpdateButtons(); }
    }
    private async Task ReloadAsync()
    {
        if (_saving || HasPending || _input is not null || _profileDirty) { _status.Text = "请先完成当前输入。"; return; }
        _saving = true; UpdateButtons();
        try { await ReloadCoreAsync(); _status.Text = "已读取隔离样本。"; }
        catch (Exception error) { _status.Text = $"读取失败：{error.Message}"; }
        finally { _saving = false; UpdateButtons(); }
    }
    private async Task ReloadCoreAsync()
    {
        var result = await _app.ReadAsync(_genealogy, _lineage);
        var children = _children is null ? Array.Empty<ChildNode>() : await _children.ReadAsync(_genealogy, _lineage);
        _rootNodes = result.Nodes; _childNodes = children;
        _orderSnapshots.Clear(); _orderIssues.Clear();
        if (_childOrders is not null) foreach (var group in children.GroupBy(x => x.ParentPlacementId))
        {
            try { var snapshot=await _childOrders.ReadAsync(_genealogy,_lineage,group.Key); if(snapshot.Items.Select(x=>(x.PlacementId,x.PersonId,x.Sex,x.Position)).SequenceEqual(group.OrderBy(x=>x.Position).Select(x=>(x.PlacementId,x.PersonId,x.Sex,x.Position)))) _orderSnapshots[group.Key]=snapshot; else _orderIssues[group.Key]="同胞资料读取期间已变化，请重新读取。"; }
            catch (InvalidOperationException error) { _orderIssues[group.Key]=error.Message; }
        }
        _hasRoot = _rootNodes.Count > 0; _loadedOk = true;
        if (_personId is Guid person && _placementId is Guid placement &&
            !_rootNodes.Any(n => n.PersonId == person && n.PlacementId == placement) &&
            !_childNodes.Any(n => n.PersonId == person && n.PlacementId == placement)) ClearFocus();
        UpdateSelection(); await RefreshFamilyDetailsAsync(); RenderCanvas();
    }
    private void RenderCanvas(bool previewChild = false)
    {
        _canvas.Children.Clear(); _draftLocation = null;
        var previewParent = _inputKind == InputKind.Sibling ? _siblingDraft?.ParentPlacementId : _placementId;
        var draftOrder = previewChild && previewParent is Guid parent
            ? _childNodes.Count(n => n.ParentPlacementId == parent && n.Sex == PersonSex.Male) + 1 : 0;
        var entries = _rootNodes.Select(n => new DisplayNode(n.PersonId, n.PlacementId, null, n.Name, 0, null, n, n.Generation))
            .Concat(_childNodes.Select(n => new DisplayNode(n.PersonId, n.PlacementId, n.ParentPlacementId, n.Name,
                previewChild && n.ParentPlacementId == previewParent && n.Position >= draftOrder ? n.Position + 1 : n.Position, n.FatherRelationshipId, n, n.Generation))).ToList();
        if (previewChild && previewParent is Guid parentPlace)
            entries.Add(new DisplayNode(_draftId, _draftId, parentPlace, "", draftOrder, _draftId, null));
        var byPlace = entries.ToDictionary(x => x.Placement);
        var generation = new Dictionary<Guid, int>();
        int GetGeneration(DisplayNode item, HashSet<Guid> seen)
        {
            if (generation.TryGetValue(item.Placement, out var existing)) return existing;
            if (!seen.Add(item.Placement)) throw new InvalidOperationException("位置存在循环，无法显示。");
            var parentGeneration = item.Parent is Guid parent ? byPlace.TryGetValue(parent, out var p) ? (int?)GetGeneration(p, seen) : throw new InvalidOperationException("父位置缺失，无法显示。") : null;
            var value = item.Generation ?? (item.Placement == _draftId && parentGeneration is int pg ? checked(pg + 1) : throw new InvalidOperationException("主线人物世代缺失，请先核实。"));
            if (parentGeneration is int previous && value <= previous) throw new InvalidOperationException("人物世代与父位置冲突，无法显示。");
            seen.Remove(item.Placement); return generation[item.Placement] = value;
        }
        var firstGeneration = entries.Count == 0 ? 0 : entries.Min(x => GetGeneration(x, new()));
        _headerFirstGeneration = entries.Count == 0 ? null : firstGeneration;
        _headerLastGeneration = entries.Count == 0 ? null : entries.Max(x => GetGeneration(x, new()));
        UpdateGenerationHeader();
        var people = entries.Select(x => new HorizontalLayoutPerson(x.Placement, GetGeneration(x, new()) - firstGeneration, x.Order, 110, 34,
            x.Placement == _placementId ? _visibleSpouses.Count * 38 : 0)).ToArray();
        var edges = entries.Where(x => x.Parent is not null).Select(x => new HorizontalLayoutRelationship(x.Relationship!.Value, x.Parent!.Value, x.Placement)).ToArray();
        var layout = HorizontalLineageLayout.Calculate(people, edges, entries.Select(x => x.Placement).ToHashSet(), allowSkippedGenerations: true);
        foreach (var edge in layout.Edges)
        {
            var line = new Polyline { Stroke = Brushes.Gray, StrokeThickness = 1.3, IsHitTestVisible = false };
            foreach (var point in edge.Points) line.Points.Add(new Point(point.X + 20, point.Y + 50));
            _canvas.Children.Add(line);
        }
        foreach (var item in layout.Nodes)
        {
            var node = byPlace[item.PersonId];
            if (previewChild && node.Placement == _draftId) { _draftLocation = new Point(item.X + 20, item.Y + 50); continue; }
            var visibleName = node.Name.Length == 0 ? "" : node.Name.StartsWith(_surname, StringComparison.Ordinal) ? node.Name[_surname.Length..] : node.Name;
            var card = new Border { Width = item.Width, Height = item.Height, Background = node.Person == _personId && node.Placement == _placementId ? Brushes.DarkOrange : Brushes.WhiteSmoke, BorderBrush = node.Name.Length == 0 ? Brushes.SlateGray : Brushes.Transparent, BorderThickness = node.Name.Length == 0 ? new Thickness(1) : new Thickness(0), Padding = new Thickness(4), Child = new TextBlock { Text = visibleName, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, Tag = node.Original };
            Canvas.SetLeft(card, item.X + 20); Canvas.SetTop(card, item.Y + 50); _canvas.Children.Add(card);
            card.MouseLeftButtonDown += async (_, e) => { e.Handled = true; if (_input is not null || HasPending || _saving || _profileDirty || (_personId==node.Person && _placementId==node.Placement)) return; _personId = node.Person; _placementId = node.Placement; UpdateSelection(); await RefreshFamilyDetailsAsync(); RenderCanvas(); };
            if (node.Original is ChildNode orderChild && _childOrders is not null && _orderSnapshots.TryGetValue(orderChild.ParentPlacementId, out var orderSnapshot))
            {
                card.AllowDrop=true;
                card.PreviewMouseLeftButtonDown += (_,e) => { if(_saving || HasPending || _input is not null || _profileDirty || _placementId!=orderChild.PlacementId) return; _childDragStart=e.GetPosition(card); _childDragSnapshot=orderSnapshot; card.CaptureMouse(); };
                card.PreviewMouseLeftButtonUp += (_,_) => { _childDragStart=null; if(card.IsMouseCaptured) card.ReleaseMouseCapture(); };
                card.PreviewMouseMove += (_,e) => { if(_childDragStart is not Point start || e.LeftButton!=MouseButtonState.Pressed) return; var pt=e.GetPosition(card); if(Math.Abs(pt.X-start.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(pt.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance) return; _childDragStart=null; if(card.IsMouseCaptured) card.ReleaseMouseCapture(); DragDrop.DoDragDrop(card,new DataObject("PuJiang.V1.ChildPlacement",orderChild.PlacementId.ToString("D")),DragDropEffects.Move); };
                card.DragOver += (_,e) => { e.Effects=e.Data.GetDataPresent("PuJiang.V1.ChildPlacement") ? DragDropEffects.Move : DragDropEffects.None; e.Handled=true; };
                card.Drop += async (_,e) => { e.Handled=true; if(_saving || HasPending || _profileDirty || _input is not null || _childDragSnapshot is null || e.Data.GetData("PuJiang.V1.ChildPlacement") is not string id || !Guid.TryParse(id,out var source)) return; await BeginChildOrderAsync(_childDragSnapshot,source,orderChild.PlacementId); };
            }
            if (_children is not null && _spouses is not null)
            {
                var menu = new ContextMenu();
                menu.Closed += (_, _) => { if (_input is null && !_saving) RenderCanvas(); };
                menu.Opened += async (_, _) =>
                {
                    if (_saving || HasPending || _profileDirty || _input is not null) { menu.IsOpen = false; return; }
                    _personId = node.Person; _placementId = node.Placement; UpdateSelection(); await RefreshFamilyDetailsAsync();
                };
                var addChild = new MenuItem { Header = "添加子女" }; addChild.Click += (_, _) => BeginFamilyInput(InputKind.Child); menu.Items.Add(addChild);
                var addSibling = new MenuItem { Header = "添加兄弟姐妹", IsEnabled = node.Parent is not null }; addSibling.Click += (_, _) => BeginSiblingInput(); menu.Items.Add(addSibling);
                var addSpouse = new MenuItem { Header = "添加配偶" }; addSpouse.Click += (_, _) => BeginFamilyInput(InputKind.Spouse); menu.Items.Add(addSpouse);
                if (_branches is not null)
                {
                    if (node.Original is RootNode)
                    {
                        var attach = new MenuItem { Header = "挂接此分支" };
                        attach.Click += async (_, _) => await BeginAttachAsync(node.Person, node.Placement);
                        menu.Items.Add(attach);
                    }
                    if (node.Original is ChildNode detachable && _orderSnapshots.TryGetValue(detachable.ParentPlacementId, out var detachGroup))
                    {
                        var detach = new MenuItem { Header = "拆支" };
                        detach.Click += async (_, _) => { if (_saving || HasPending || _profileDirty || _input is not null) return; _pendingDetach = new(Guid.NewGuid(), _genealogy, _lineage, detachable.PersonId, detachable.PlacementId, detachable.ParentPlacementId, detachGroup.Revision, detachGroup.Evidence); await SubmitBranchAsync(); };
                        menu.Items.Add(detach);
                    }
                    var delete = new MenuItem { Header = "删除此分支" };
                    delete.Click += async (_, _) => await BeginDeleteBranchAsync(node.Person, node.Placement); menu.Items.Add(delete);
                }
                if(node.Original is ChildNode childNode && _childOrders is not null && _orderSnapshots.TryGetValue(childNode.ParentPlacementId,out var group))
                {
                    var peers=group.Items.Where(x=>x.Sex==childNode.Sex).OrderBy(x=>x.Position).ToArray(); var idx=Array.FindIndex(peers,x=>x.PlacementId==childNode.PlacementId);
                    var up=new MenuItem {Header="上移",IsEnabled=idx>0}; if(idx>0) up.Click+=async(_,_)=>await BeginChildOrderAsync(group,childNode.PlacementId,peers[idx-1].PlacementId); menu.Items.Add(up);
                    var down=new MenuItem {Header="下移",IsEnabled=idx>=0 && idx+1<peers.Length}; if(idx>=0 && idx+1<peers.Length) down.Click+=async(_,_)=>await BeginChildOrderAsync(group,childNode.PlacementId,peers[idx+1].PlacementId); menu.Items.Add(down);
                }
                card.ContextMenu = menu;
            }
            if (node.Person == _personId && node.Placement == _placementId)
            {
                for (var index = 0; index < _visibleSpouses.Count; index++)
                {
                    var spouse = _visibleSpouses[index]; var top = item.Y + 50 + 34 + 8 + index * 38;
                    _canvas.Children.Add(new Line { X1 = item.X + 75, X2 = item.X + 75, Y1 = top - 8, Y2 = top, Stroke = Brushes.SlateGray, StrokeThickness = 1.5, IsHitTestVisible = false, Tag = "V1SpouseLink" });
                    var partner = new Border { Width = 110, Height = 34, Background = Brushes.WhiteSmoke, Padding = new Thickness(4), Tag = spouse,
                        Child = new TextBlock { Text = spouse.Name, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } };
                    if (_branches is not null)
                    {
                        var spouseMenu = new ContextMenu(); var deleteSpouse = new MenuItem { Header = "删除配偶" };
                        deleteSpouse.Click += async (_, _) => await BeginDeleteSpouseAsync(node.Person, node.Placement, spouse);
                        spouseMenu.Items.Add(deleteSpouse); partner.ContextMenu = spouseMenu;
                    }
                    partner.AllowDrop = true;
                    partner.PreviewMouseLeftButtonDown += (_, e) => { if (_saving || HasPending || _input is not null || _profileDirty) return; _spouseDragStart = e.GetPosition(partner); _dragSpouseRevision = _spouseRevision; partner.CaptureMouse(); };
                    partner.PreviewMouseLeftButtonUp += (_, _) => { _spouseDragStart = null; if (partner.IsMouseCaptured) partner.ReleaseMouseCapture(); };
                    partner.PreviewMouseMove += (_, e) =>
                    {
                        if (_spouseDragStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
                        var point = e.GetPosition(partner);
                        if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                        _spouseDragStart = null; if (partner.IsMouseCaptured) partner.ReleaseMouseCapture();
                        DragDrop.DoDragDrop(partner, new DataObject("PuJiang.V1.SpouseUnion", spouse.UnionId.ToString("D")), DragDropEffects.Move);
                    };
                    partner.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent("PuJiang.V1.SpouseUnion") ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
                    partner.Drop += async (_, e) =>
                    {
                        e.Handled = true;
                        if (_saving || HasPending || _input is not null || _profileDirty || _personId is not Guid father || _placementId is not Guid place) return;
                        if (e.Data.GetData("PuJiang.V1.SpouseUnion") is not string value || !Guid.TryParse(value, out var source) || source == spouse.UnionId) return;
                        _pendingMove = new(Guid.NewGuid(), _genealogy, _lineage, father, place, source, spouse.UnionId, _dragSpouseRevision);
                        await SubmitFamilyAsync();
                    };
                    Canvas.SetLeft(partner, item.X + 20); Canvas.SetTop(partner, top); _canvas.Children.Add(partner);
                }
            }
        }
        _contentWidth = Math.Max(650, layout.Nodes.Select(n => n.X + n.Width + 100).DefaultIfEmpty(0).Max());
        _contentHeight = Math.Max(360, layout.Nodes.Select(n => n.Y + n.Height + (n.PersonId == _placementId ? _visibleSpouses.Count * 38 : 0) + 100).DefaultIfEmpty(0).Max());
        FillCanvasViewport();
    }
    private sealed record DisplayNode(Guid Person, Guid Placement, Guid? Parent, string Name, int Order, Guid? Relationship, object? Original, int? Generation = null);
    private sealed record AttachTarget(Guid PersonId, Guid PlacementId, string Label);
    private async Task BeginAttachAsync(Guid person, Guid placement)
    {
        if (_branches is null || _saving || HasPending || _profileDirty || _input is not null || _personId != person || _placementId != placement) return;
        var options = _rootNodes.Select(x => new AttachTarget(x.PersonId, x.PlacementId, $"{x.Name} · {x.PlacementId.ToString()[..8]}"))
            .Concat(_childNodes.Select(x => new AttachTarget(x.PersonId, x.PlacementId, $"{x.Name} · {x.PlacementId.ToString()[..8]}")))
            .Where(x => x.PlacementId != placement).ToArray();
        if (options.Length == 0) { _status.Text = "没有可选择的新父位置。"; return; }
        var picker = new Window { Title = "选择新父亲", Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 300 };
        var box = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 8) };
        if (_lastAttachSelection is { } last && last.Lineage == _lineage && last.Source == placement)
            box.SelectedItem = options.FirstOrDefault(x => x.PlacementId == last.Target) ?? options[0];
        var confirm = new Button { Content = "查看挂接影响", Padding = new Thickness(8), IsDefault = true };
        confirm.Click += (_, _) => picker.DialogResult = true;
        panel.Children.Add(new TextBlock { Text = "选择同一谱系中的新父位置：" }); panel.Children.Add(box); panel.Children.Add(confirm); picker.Content = panel;
        if (picker.ShowDialog() != true || box.SelectedItem is not AttachTarget target) return;
        _lastAttachSelection = (_lineage, placement, target.PlacementId);
        try
        {
            var preview = await _branches.PreviewAttachAsync(_genealogy, _lineage, person, placement, target.PlacementId);
            if (_personId != person || _placementId != placement || _profileDirty || _input is not null || HasPending) { _status.Text = "选择或草稿已变化，请重新操作。"; return; }
            var text = $"挂接分支：{preview.SourceName}（共 {preview.PeopleCount} 人）\n新父亲：{preview.TargetName}\n原生父：{preview.OldFatherName ?? "未记录"}；原生母：{preview.OldMotherName ?? "未记录"}\n新生母：{preview.NewMotherName ?? "暂不记录"}\n首节点世代：第 {preview.OldGeneration} 世 → 第 {preview.NewGeneration} 世\n后代世代按相同差值调整，人物追加到目标同性别组末尾。\n\n确认挂接？";
            if (MessageBox.Show(this, text, "确认挂接影响", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { _status.Text = "已取消挂接。"; return; }
            _pendingAttach = new(Guid.NewGuid(), _genealogy, _lineage, person, placement, target.PlacementId, preview.Evidence);
            await SubmitBranchAsync();
        }
        catch (Exception error) { _status.Text = $"无法挂接：{error.Message}"; }
    }
    private async Task BeginDeleteBranchAsync(Guid person, Guid placement)
    {
        if (_branches is null || _saving || HasPending || _profileDirty || _input is not null || _personId != person || _placementId != placement) return;
        try
        {
            var preview = await _branches.PreviewDeleteAsync(_genealogy, _lineage, person, placement);
            if (_personId != person || _placementId != placement) { _status.Text = "选择已变化，请重新操作。"; return; }
            var text = "将移除所选分支及其配偶。\n人物：" + string.Join("、", preview.People) + "\n配偶：" + (preview.Spouses.Count == 0 ? "无" : string.Join("、", preview.Spouses)) + "\n因保留关系而留存档案：" + (preview.RetainedPeople.Count == 0 ? "无" : string.Join("、", preview.RetainedPeople)) + "\n失去最后引用后自动清理的关联档案：" + (preview.AutoCleanedPeople.Count == 0 ? "无" : string.Join("、", preview.AutoCleanedPeople)) + "\n\n已拆出的分支保留。确定删除？";
            if (MessageBox.Show(this, text, "确认删除分支", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { _status.Text = "已取消删除。"; return; }
            _pendingDelete = new(Guid.NewGuid(), _genealogy, _lineage, person, placement, preview.Evidence); await SubmitBranchAsync();
        }
        catch (Exception error) { _status.Text = $"无法确认删除范围：{error.Message}"; }
    }
    private async Task BeginDeleteSpouseAsync(Guid father, Guid placement, SpouseNode spouse)
    {
        if (_branches is null || _saving || HasPending || _profileDirty || _input is not null || _personId != father || _placementId != placement) return;
        try
        {
            var preview = await _branches.PreviewSpouseAsync(_genealogy, _lineage, father, placement, spouse.PersonId, spouse.UnionId);
            if (_personId != father || _placementId != placement) { _status.Text = "选择已变化，请重新操作。"; return; }
            var text = $"将删除配偶 {preview.SpouseName}，保留子女。" + (preview.AffectedChildren.Count == 0 ? "" : "\n以下子女的生母暂时留空：" + string.Join("、", preview.AffectedChildren)) + "\n\n确定删除？";
            if (MessageBox.Show(this, text, "确认删除配偶", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { _status.Text = "已取消删除。"; return; }
            _pendingSpouseDelete = new(Guid.NewGuid(), _genealogy, _lineage, father, placement, spouse.PersonId, spouse.UnionId, preview.Evidence); await SubmitBranchAsync();
        }
        catch (Exception error) { _status.Text = $"无法确认配偶删除范围：{error.Message}"; }
    }
    private async Task SubmitBranchAsync()
    {
        if (_branches is null || _saving || (_pendingDetach is null && _pendingAttach is null && _pendingDelete is null && _pendingSpouseDelete is null && _pendingRootParents is null)) return;
        _saving = true; if (_pendingRootParents is not null) { if (_rootFatherInput is not null) _rootFatherInput.IsReadOnly = true; if (_rootMotherInput is not null) _rootMotherInput.IsReadOnly = true; } UpdateButtons(); var wasDelete = _pendingDelete is not null;
        try
        {
            try
            {
                if (_pendingDetach is { } detach) await _branches.DetachAsync(detach);
                else if (_pendingAttach is { } attach) await _branches.AttachAsync(attach);
                else if (_pendingDelete is { } delete) await _branches.DeleteAsync(delete);
                else if (_pendingSpouseDelete is { } spouse) await _branches.DeleteSpouseAsync(spouse);
                else if (_pendingRootParents is { } parents) await _branches.UpdateRootParentsAsync(parents);
            }
            catch (Exception error)
            {
                bool committed;
                try { committed = _pendingDetach is { } d ? await _branches.HasDetachReceiptAsync(d.OperationId) : _pendingAttach is { } a ? await _branches.HasAttachReceiptAsync(a.OperationId) : _pendingDelete is { } b ? await _branches.HasDeleteReceiptAsync(b.OperationId) : _pendingSpouseDelete is { } s ? await _branches.HasSpouseDeleteReceiptAsync(s.OperationId) : await _branches.HasRootParentsReceiptAsync(_pendingRootParents!.OperationId); }
                catch { _branchUnknown = true; if (_rootFatherInput is not null) _rootFatherInput.IsReadOnly = true; if (_rootMotherInput is not null) _rootMotherInput.IsReadOnly = true; _status.Text = $"操作结果尚未确认：{error.Message}。请重试同一操作。"; return; }
                if (!committed) { _pendingDetach = null; _pendingAttach = null; _pendingDelete = null; _pendingSpouseDelete = null; _pendingRootParents = null; _branchUnknown = false; _status.Text = $"操作未保存：{error.Message}。输入已保留，请核实后重试。"; return; }
            }
            _pendingDetach = null; _pendingAttach = null; _pendingDelete = null; _pendingSpouseDelete = null; _pendingRootParents = null; _branchUnknown = false; _profileDirty = false;
            if (wasDelete) ClearFocus();
            try { await ReloadCoreAsync(); _status.Text = "操作已保存并重新读取。"; }
            catch (Exception error) { _loadedOk = false; _status.Text = $"操作已保存，但读取失败：{error.Message}。请重新读取。"; }
        }
        finally { _saving = false; if (!_branchUnknown) { if (_rootFatherInput is not null) _rootFatherInput.IsReadOnly = false; if (_rootMotherInput is not null) _rootMotherInput.IsReadOnly = false; } UpdateButtons(); }
    }
    private static string RankName(int rank, PersonSex sex)
    {
        string[] digits={"零","一","二","三","四","五","六","七","八","九"};
        var prefix=rank==1?"长":rank==2?"次":rank<10?digits[rank]:rank<100?(rank<20?"十":digits[rank/10]+"十")+(rank%10==0?"":digits[rank%10]):$"第{rank}";
        return prefix+(sex==PersonSex.Male?"子":"女");
    }
    private async Task BeginChildOrderAsync(ChildOrderSnapshot snapshot, Guid source, Guid target)
    {
        if(_saving || HasPending || _input is not null || _profileDirty || _childOrders is null || source==target) return;
        var a=snapshot.Items.SingleOrDefault(x=>x.PlacementId==source); var b=snapshot.Items.SingleOrDefault(x=>x.PlacementId==target);
        if(a is null || b is null || a.Sex!=b.Sex) { _status.Text="只能调整同一父位置、同性别组的排行。"; return; }
        _personId=a.PersonId; _placementId=a.PlacementId;
        _pendingChildOrder=new(Guid.NewGuid(),_genealogy,_lineage,snapshot.ParentPlacementId,source,target,snapshot.Revision,snapshot.Evidence);
        await SubmitChildOrderAsync();
    }
    private async Task SubmitChildOrderAsync()
    {
        if(_saving || _pendingChildOrder is not { } command || _childOrders is null) return;
        _saving=true; UpdateButtons();
        try
        {
            try { await _childOrders.MoveAsync(command); _pendingChildOrder=null; _childOrderUnknown=false; }
            catch(Exception error)
            {
                try { _childOrderUnknown=await _childOrders.HasReceiptAsync(command.OperationId); }
                catch { _childOrderUnknown=true; }
                _status.Text=_childOrderUnknown ? $"排行保存结果未确认：{error.Message}。请重试同一操作。" : $"排行未保存：{error.Message}。调整意图已保留，可重试或放弃后重新读取。";
                return;
            }
            try { await ReloadCoreAsync(); _status.Text="排行已保存并重新读取。"; }
            catch(Exception error) { _status.Text=$"排行已保存，但画布读取失败：{error.Message}。请重新读取。"; }
        }
        finally { _saving=false; UpdateButtons(); }
    }
    private void UpdateSelection()
    {
        foreach (var card in _canvas.Children.OfType<Border>())
        {
            var match = card.Tag switch { RootNode root => root.PersonId == _personId && root.PlacementId == _placementId, ChildNode child => child.PersonId == _personId && child.PlacementId == _placementId, _ => false };
            card.Background = match ? Brushes.DarkOrange : Brushes.WhiteSmoke;
        }
        var name = _rootNodes.FirstOrDefault(n => n.PersonId == _personId && n.PlacementId == _placementId)?.Name ??
                   _childNodes.FirstOrDefault(n => n.PersonId == _personId && n.PlacementId == _placementId)?.Name;
        _details.Text = name is null ? "未选中人物" : $"姓名：{(name.Length == 0 ? "（未填写姓名）" : name)}";
        UpdateButtons();
    }
    private sealed record MotherChoice(Guid? Id, string Label, bool Unspecified = false) { public override string ToString() => Label; }
    private void AddProfileEditor(PersonProfile profile, string heading)
    {
        var name = new TextBox { Text = profile.Name, Margin = new Thickness(0, 8, 0, 0), ToolTip = "完整姓名，Enter 或离开输入框保存" };
        var sex = new ComboBox { Margin = new Thickness(0, 6, 0, 0), ItemsSource = new[] { PersonSex.Male, PersonSex.Female }, SelectedItem = profile.Sex };
        _familyDetails.Children.Add(new TextBlock { Text = heading, Margin = new Thickness(0, 8, 0, 0) }); _familyDetails.Children.Add(name); _familyDetails.Children.Add(sex);
        void MarkDirty() { _profileDirty = name.Text != profile.Name || sex.SelectedItem is PersonSex chosen && chosen != profile.Sex; UpdateButtons(); }
        async Task SaveProfile()
        {
            if (!_profileDirty || _saving || HasPending || sex.SelectedItem is not PersonSex chosen) return;
            _pendingProfile = new(Guid.NewGuid(), _genealogy, _lineage, profile.PersonId, profile.PlacementId, name.Text.Trim(), chosen, profile.Name, profile.Sex);
            await SubmitFamilyAsync();
        }
        name.TextChanged += (_, _) => MarkDirty();
        name.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SaveProfile(); } };
        name.LostKeyboardFocus += async (_, _) => { await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background); await SaveProfile(); };
        sex.SelectionChanged += async (_, _) => { MarkDirty(); await SaveProfile(); };
    }
    private void AddRootParentEditor(Guid person, Guid placement, RootParentInfo parents)
    {
        var father = new TextBox { Text = parents.FatherName ?? "", Margin = new Thickness(0, 4, 0, 0), ToolTip = "输入新生父姓名；Enter 或离开输入框保存" };
        var mother = new TextBox { Text = parents.MotherName ?? "", Margin = new Thickness(0, 4, 0, 0), ToolTip = "输入新生母姓名；Enter 或离开输入框保存" };
        _rootFatherInput = father; _rootMotherInput = mother;
        _familyDetails.Children.Add(new TextBlock { Text = "生父", Margin = new Thickness(0, 6, 0, 0) }); _familyDetails.Children.Add(father);
        _familyDetails.Children.Add(new TextBlock { Text = "生母", Margin = new Thickness(0, 6, 0, 0) }); _familyDetails.Children.Add(mother);
        void MarkDirty() { _profileDirty = father.Text.Trim() != (parents.FatherName ?? "") || mother.Text.Trim() != (parents.MotherName ?? ""); UpdateButtons(); }
        async Task Save()
        {
            if (!_profileDirty || _saving || HasPending || _branches is null || _personId != person || _placementId != placement) return;
            _pendingRootParents = new(Guid.NewGuid(), _genealogy, _lineage, person, placement, father.Text.Trim(), mother.Text.Trim(), parents.Evidence);
            await SubmitBranchAsync();
        }
        async Task HandleParentKey(KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await Save(); } else if (e.Key == Key.Escape && !_saving && !HasPending) { e.Handled = true; father.Text = parents.FatherName ?? ""; mother.Text = parents.MotherName ?? ""; MarkDirty(); } }
        father.TextChanged += (_, _) => MarkDirty(); mother.TextChanged += (_, _) => MarkDirty();
        father.PreviewKeyDown += async (_, e) => await HandleParentKey(e); mother.PreviewKeyDown += async (_, e) => await HandleParentKey(e);
        father.LostKeyboardFocus += async (_, _) => { await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background); if (!father.IsKeyboardFocusWithin && !mother.IsKeyboardFocusWithin) await Save(); };
        mother.LostKeyboardFocus += async (_, _) => { await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background); if (!father.IsKeyboardFocusWithin && !mother.IsKeyboardFocusWithin) await Save(); };
    }
    private async Task RefreshFamilyDetailsAsync()
    {
        var version = ++_detailsVersion;
        var readingOwnOrder = false;
        _familyDetails.Children.Clear();
        _rootFatherInput = null; _rootMotherInput = null;
        _visibleSpouses = Array.Empty<SpouseNode>(); _spouseRevision = 0;
        if (_spouses is null || _personId is not Guid person || _placementId is not Guid placement) return;
        try
        {
            _selectedSex = null;
            if (_profiles is not null)
            {
                var profile = await _profiles.ReadAsync(_genealogy, _lineage, person, placement);
                if (version != _detailsVersion || _personId != person || _placementId != placement) return;
                _selectedSex = profile.Sex; UpdateButtons();
                AddProfileEditor(profile, "姓名 / 性别");
                if (_generations is not null && _rootNodes.Any(x => x.PersonId == person && x.PlacementId == placement))
                {
                    var editGeneration = new Button { Content = "更正世代", Margin = new Thickness(0, 6, 0, 0) };
                    _familyDetails.Children.Add(editGeneration);
                    editGeneration.Click += async (_, _) =>
                    {
                        if (_saving || HasPending || _profileDirty || _input is not null) return;
                        try
                        {
                            var snapshot = await _generations.ReadAsync(_genealogy, _lineage, person, placement);
                            if (version != _detailsVersion || _personId != person || _placementId != placement) return;
                            var dialog = new V1GenerationEditDialog(_generations, _genealogy, _lineage, snapshot) { Owner = this };
                            if (dialog.ShowDialog() == true) await ReloadCoreAsync();
                        }
                        catch (Exception error) { _status.Text = $"世代读取或刷新失败：{error.Message}"; }
                    };
                }
                var currentRoot=_rootNodes.FirstOrDefault(x=>x.PersonId==person&&x.PlacementId==placement);
                if(currentRoot is not null) { var editRoot=new Button {Content="修改首节点信息",Margin=new Thickness(0,6,0,0)}; _familyDetails.Children.Add(editRoot); editRoot.Click+=async(_,_)=>{if(_saving||HasPending||_profileDirty||_input is not null)return;var dialog=new V1RootEditDialog(_app,_genealogy,_lineage,currentRoot){Owner=this};if(dialog.ShowDialog()==true){try{await ReloadCoreAsync();}catch(Exception error){_status.Text=$"已保存，但重新读取失败：{error.Message}";}}}; }
                if (currentRoot is not null && _branches is not null)
                {
                    var parents = await _branches.ReadRootParentsAsync(_genealogy, _lineage, person, placement);
                    if (version != _detailsVersion || _personId != person || _placementId != placement) return;
                    if (parents.FatherName is not null || parents.MotherName is not null)
                        _familyDetails.Children.Add(new TextBlock { Text = $"生父：{parents.FatherName ?? "未记录"}\n生母：{parents.MotherName ?? "未记录"}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
                    var editParents = new Button { Content = "修改生父母", Margin = new Thickness(0, 6, 0, 0) };
                    editParents.Click += (_, _) =>
                    {
                        if (_saving || HasPending || _profileDirty || _input is not null || _personId != person || _placementId != placement) return;
                        _familyDetails.Children.Clear();
                        _familyDetails.Children.Add(new TextBlock { Text = "输入新人物姓名，回车或离开输入框后保存；Esc 放弃修改。", TextWrapping = TextWrapping.Wrap });
                        AddRootParentEditor(person, placement, parents);
                        var back = new Button { Content = "返回人物资料", Margin = new Thickness(0, 8, 0, 0) };
                        back.Click += async (_, _) => { if (!_profileDirty && !_saving && !HasPending) await RefreshFamilyDetailsAsync(); };
                        _familyDetails.Children.Add(back);
                    };
                    _familyDetails.Children.Add(editParents);
                }
            }
            readingOwnOrder = true;
            var snapshot = _selectedSex is PersonSex.Female or PersonSex.Unknown
                ? new SpouseSnapshot(0, Array.Empty<SpouseNode>())
                : await _spouses.ReadSnapshotAsync(_genealogy, _lineage, person, placement);
            readingOwnOrder = false;
            var ownSpouses = snapshot.Nodes;
            if (version != _detailsVersion || _personId != person || _placementId != placement) return;
            _visibleSpouses = ownSpouses; _spouseRevision = snapshot.Revision;
            if (ownSpouses.Count > 0)
                _familyDetails.Children.Add(new TextBlock { Text = "配偶：" + string.Join("、", ownSpouses.Select(x => x.Name.Length == 0 ? "（未填写姓名）" : x.Name)), TextWrapping = TextWrapping.Wrap });
            if (_profiles is not null)
            {
                foreach (var spouse in ownSpouses)
                {
                    var edit = new Button { Content = "编辑配偶：" + (spouse.Name.Length == 0 ? "（未填写姓名）" : spouse.Name), Margin = new Thickness(0, 5, 0, 0) };
                    edit.Click += async (_, _) =>
                    {
                        if (_saving || HasPending || _profileDirty || _input is not null) return;
                        try
                        {
                            var profile = await _profiles.ReadAsync(_genealogy, _lineage, spouse.PersonId, placement);
                            if (version != _detailsVersion || _personId != person || _placementId != placement) return;
                            _familyDetails.Children.Clear();
                            AddProfileEditor(profile, "配偶姓名 / 性别");
                            var back = new Button { Content = "返回人物资料", Margin = new Thickness(0, 8, 0, 0) };
                            back.Click += async (_, _) => { if (!_profileDirty && !_saving && !HasPending) await RefreshFamilyDetailsAsync(); };
                            _familyDetails.Children.Add(back);
                        }
                        catch (Exception error) { _status.Text = $"配偶资料读取失败：{error.Message}"; }
                    };
                    _familyDetails.Children.Add(edit);
                }
            }
            var child = _childNodes.FirstOrDefault(x => x.PersonId == person && x.PlacementId == placement);
            if (child is null) return;
            if (_orderIssues.TryGetValue(child.ParentPlacementId,out var issue)) _familyDetails.Children.Add(new TextBlock { Text="排行暂不可调整："+issue, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,0) });
            if (_orderSnapshots.TryGetValue(child.ParentPlacementId,out var rankGroup))
            {
                var group=rankGroup.Items.Where(x=>x.Sex==child.Sex).OrderBy(x=>x.Position).ToArray(); var rank=Array.FindIndex(group,x=>x.PlacementId==placement)+1;
                if(rank>0) _familyDetails.Children.Add(new TextBlock {Text="实际排行："+RankName(rank,child.Sex),Margin=new Thickness(0,8,0,0)});
            }
            var parentPerson = _rootNodes.Where(x => x.PlacementId == child.ParentPlacementId).Select(x => (Guid?)x.PersonId)
                .Concat(_childNodes.Where(x => x.PlacementId == child.ParentPlacementId).Select(x => (Guid?)x.PersonId)).SingleOrDefault()
                ?? throw new InvalidOperationException("父位置缺失。");
            var choices = await _spouses.ReadMotherCandidatesAsync(_genealogy, _lineage, parentPerson, child.ParentPlacementId, child.PersonId);
            if (version != _detailsVersion || _personId != person || _placementId != placement) return;
            var box = new ComboBox { Margin = new Thickness(0, 8, 0, 0), MinWidth = 160 };
            if (child.MotherId is null && !child.MotherExplicitlyCleared) box.Items.Add(new MotherChoice(null, "生母：未记录", true));
            box.Items.Add(new MotherChoice(null, "生母：主动清空"));
            foreach (var choice in choices) box.Items.Add(new MotherChoice(choice.PersonId, "生母：" + (choice.Name.Length == 0 ? "（未填写姓名）" : choice.Name) + " · " + choice.PersonId.ToString("N")[..8]));
            var selected = box.Items.Cast<MotherChoice>().FirstOrDefault(x => x.Id == child.MotherId && (child.MotherId is not null || x.Unspecified == !child.MotherExplicitlyCleared));
            if (selected is null)
                _familyDetails.Children.Add(new TextBlock { Text = "已记录生母不在当前父亲的有效配偶候选中；原关系保留，可选择配偶或主动清空。", TextWrapping = TextWrapping.Wrap });
            box.SelectedItem = selected;
            box.SelectionChanged += async (_, _) =>
            {
                if (_saving || HasPending || _profileDirty || _input is not null) { box.SelectedItem = selected; return; }
                if (box.SelectedItem is not MotherChoice choice) return;
                if (choice.Unspecified || choice.Id == child.MotherId && (choice.Id is not null || child.MotherExplicitlyCleared)) return;
                _pendingMother = new(Guid.NewGuid(), _genealogy, _lineage, parentPerson, child.ParentPlacementId, child.PersonId, choice.Id, child.MotherId, child.MotherExplicitlyCleared);
                await SubmitFamilyAsync();
            };
            _familyDetails.Children.Add(box);
        }
        catch (Exception error)
        {
            if (version != _detailsVersion || _personId != person || _placementId != placement) return;
            _familyDetails.Children.Add(new TextBlock { Text = $"关系资料读取失败：{error.Message}", TextWrapping = TextWrapping.Wrap });
            if (readingOwnOrder && error.Message.Contains("配偶排序"))
            {
                var repair = new Button { Content = "修复配偶顺序", Margin = new Thickness(0, 8, 0, 0) };
                repair.Click += async (_, _) => await OpenRepairAsync(person, placement);
                _familyDetails.Children.Add(repair);
            }
        }
    }
    private async Task OpenRepairAsync(Guid person, Guid placement)
    {
        if (_spouses is null || _saving || HasPending || _profileDirty || _input is not null || _personId != person || _placementId != placement) return;
        try
        {
            var snapshot = await _spouses.ReadRepairAsync(_genealogy, _lineage, person, placement);
            if (_personId != person || _placementId != placement) return;
            var dialog = new V1SpouseOrderRepairWindow(_spouses, _genealogy, _lineage, person, placement, snapshot) { Owner = this };
            if (dialog.ShowDialog() == true) { await ReloadCoreAsync(); _status.Text = "配偶顺序已修复并重新读取；原始记录已保留。"; }
        }
        catch (Exception error) { _status.Text = $"无法打开修复：{error.Message}"; }
    }
    private void Select(RootNode node) { _personId = node.PersonId; _placementId = node.PlacementId; _details.Text = $"姓名：{(node.Name.Length == 0 ? "（未填写姓名）" : node.Name)}"; foreach (var card in _canvas.Children.OfType<Border>()) if (card.Tag is RootNode value) card.Background = value.PersonId == _personId && value.PlacementId == _placementId ? Brushes.DarkOrange : Brushes.WhiteSmoke; }
    private void ClearFocus() { _detailsVersion++; _personId = null; _placementId = null; _selectedSex = null; _visibleSpouses = Array.Empty<SpouseNode>(); _details.Text = "未选中人物"; _familyDetails.Children.Clear(); RenderCanvas(); UpdateButtons(); }
    private void CancelInput() { if (_input is null) return; if (_inputHost is not null) _canvas.Children.Remove(_inputHost); if (_generationInput is not null) _canvas.Children.Remove(_generationInput); if (_generationLabel is not null) _canvas.Children.Remove(_generationLabel); _generationInput = null; _generationLabel = null; _inputHost = null; _input = null; _siblingDraft = null; _rebindChild.Visibility = Visibility.Collapsed; RenderCanvas(); _status.Text = "已取消输入，未创建人物。"; }
    private void UpdateButtons() { _create.IsEnabled = _loadedOk && !_saving && !_hasRoot && !HasPending; _reload.IsEnabled = !_saving && !HasPending && !_profileDirty; var ready = _loadedOk && !_saving && !HasPending && !_profileDirty && _input is null && _personId is not null && _placementId is not null; var canAdd = ready && (_profiles is null || _selectedSex == PersonSex.Male); _addChild.IsEnabled = canAdd && _children is not null; _addSibling.IsEnabled = ready && _children is not null && _childNodes.Any(x => x.PersonId == _personId && x.PlacementId == _placementId); _addSpouse.IsEnabled = canAdd && _spouses is not null; _retry.Visibility = _uncertain || _branchUnknown || _pendingChildOrder is not null ? Visibility.Visible : Visibility.Collapsed; _retry.IsEnabled = !_saving; _abandonOrder.Visibility = _pendingChildOrder is not null && !_childOrderUnknown ? Visibility.Visible : Visibility.Collapsed; _abandonOrder.IsEnabled=!_saving; }
    internal bool CanCloseForOwner() { if (!_saving && !HasPending && _input is null && !_profileDirty) return true; _status.Text = _uncertain || _childOrderUnknown || _branchUnknown ? "保存结果尚未确认，请重试以保留输入。" : "请先完成或取消当前输入。"; return false; }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!CanCloseForOwner()) e.Cancel = true; }

    internal Task ReadyForTest => _ready.Task;
    internal Task SwitchLineageForTest(Guid id) => SwitchLineageAsync(id);
    internal async Task DetachForTest(Guid person, Guid placement)
    {
        var node = _childNodes.Single(x => x.PersonId == person && x.PlacementId == placement); var snapshot = _orderSnapshots[node.ParentPlacementId];
        _personId = person; _placementId = placement;
        _pendingDetach = new(Guid.NewGuid(), _genealogy, _lineage, person, placement, node.ParentPlacementId, snapshot.Revision, snapshot.Evidence);
        await SubmitBranchAsync();
    }
    internal async Task DeleteBranchForTest(Guid person, Guid placement)
    {
        var preview = await _branches!.PreviewDeleteAsync(_genealogy, _lineage, person, placement);
        _personId = person; _placementId = placement;
        _pendingDelete = new(Guid.NewGuid(), _genealogy, _lineage, person, placement, preview.Evidence);
        await SubmitBranchAsync();
    }
    internal Task EnterCanvasForTest(RootNode root) => EnterCanvasAsync(root);
    internal Guid LineageForTest => _lineage;
    internal bool HomeVisibleForTest => _home.Visibility == Visibility.Visible;
    internal string HomeTextForTest => string.Join("\n",_home.Children.OfType<TextBlock>().Select(x=>x.Text));
    internal void BeginForTest() => BeginNamed();
    internal void SetInputForTest(string value) { if (_input is not null) _input.Text = value; }
    internal void SetGenerationForTest(string value) { if (_generationInput is not null) _generationInput.Text = value; }
    internal string? GenerationInputForTest => _generationInput?.Text;
    internal Canvas GenerationHeaderForTest => _generationHeader;
    internal void SetViewportForTest(double scale, double horizontalOffset) { _scale.ScaleX = _scale.ScaleY = scale; _scroll.UpdateLayout(); _scroll.ScrollToHorizontalOffset(horizontalOffset); _scroll.UpdateLayout(); UpdateGenerationHeader(); }
    internal Task ConfirmForTest() => ConfirmNamedAsync();
    internal Task RetryForTest() => SubmitPendingAsync();
    internal Task RetryFamilyForTest() => SubmitFamilyAsync();
    internal Task MoveChildOrderForTest(Guid parent, Guid source, Guid target) => BeginChildOrderAsync(_orderSnapshots[parent],source,target);
    internal Task RetryChildOrderForTest() => SubmitChildOrderAsync();
    internal bool ChildOrderPendingForTest => _pendingChildOrder is not null;
    internal string FamilyTextForTest => string.Join("\n",_familyDetails.Children.OfType<TextBlock>().Select(x=>x.Text));
    internal Task ReloadForTest() => ReloadAsync();
    internal void CancelForTest() => CancelInput();
    internal (Guid? Person, Guid? Placement) FocusForTest => (_personId, _placementId);
    internal string StatusForTest => _status.Text;
    internal string? InputForTest => _input?.Text;
    internal bool InputReadOnlyForTest => _input?.IsReadOnly ?? false;
    internal TextBox? InputControlForTest => _input;
    internal StackPanel? InputHostForTest => _inputHost;
    internal Canvas CanvasForTest => _canvas;
    internal ScrollViewer ScrollForTest => _scroll;
    internal int NodeCountForTest => _canvas.Children.OfType<Border>().Count(x => x.Tag is RootNode);
    internal int ChildCountForTest => _canvas.Children.OfType<Border>().Count(x => x.Tag is ChildNode);
    internal void BeginChildForTest() => BeginFamilyInput(InputKind.Child);
    internal void BeginSiblingForTest() => BeginSiblingInput();
    internal Button? RepairButtonForTest => _familyDetails.Children.OfType<Button>().FirstOrDefault(x => Equals(x.Content, "修复配偶顺序"));
    internal Task RebindChildForTest() => RebindChildAsync();
    internal bool CanRebindChildForTest => _rebindChild.Visibility == Visibility.Visible;
    internal void BeginRootParentEditForTest() => _familyDetails.Children.OfType<Button>().Single(x => Equals(x.Content, "修改生父母")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal TextBox? RootFatherInputForTest => _rootFatherInput;
    internal TextBox? RootMotherInputForTest => _rootMotherInput;
    internal void ClearFocusForTest() => ClearFocus();
    internal void BeginSpouseForTest() => BeginFamilyInput(InputKind.Spouse);
    internal async Task SelectForTest(Guid person, Guid placement) { _personId = person; _placementId = placement; UpdateSelection(); await RefreshFamilyDetailsAsync(); RenderCanvas(); }
    internal async Task ChooseMotherForTest(Guid? id)
    {
        var box = _familyDetails.Children.OfType<ComboBox>().Single();
        var choice = box.Items.Cast<MotherChoice>().Single(x => x.Id == id && !x.Unspecified);
        box.SelectedItem = choice;
        while (_saving) await Task.Delay(10);
    }
    internal IReadOnlyList<Guid> MotherCandidateIdsForTest => _familyDetails.Children.OfType<ComboBox>()
        .SelectMany(x => x.Items.OfType<MotherChoice>()).Where(x => x.Id is not null).Select(x => x.Id!.Value).ToArray();
    internal async Task EditProfileForTest(string name, PersonSex sex)
    {
        var text = _familyDetails.Children.OfType<TextBox>().Single();
        var box = _familyDetails.Children.OfType<ComboBox>().Single(x => x.ItemsSource is PersonSex[]);
        text.Text = name;
        if (!Equals(box.SelectedItem, sex)) box.SelectedItem = sex;
        else
        {
            var source = PresentationSource.FromVisual(text) ?? throw new InvalidOperationException("输入控件未显示。");
            text.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }
        while (_saving) await Task.Delay(10);
    }
}
