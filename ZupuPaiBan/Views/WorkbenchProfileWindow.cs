using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Data;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.Views;

/// <summary>Workbench with profile drafts and a bounded, conservative genealogy canvas.</summary>
public sealed class WorkbenchProfileWindow : Window
{
    // With the layout's 24-pixel row gap, a 52-pixel node gives a 76-pixel
    // pitch: a 30-pixel name card, an 8-pixel gap, and one 30-pixel spouse card.
    private const double CanvasNodeWidth = 100, CanvasNodeHeight = 52, CanvasNameHeight = 30;
    internal static double ProjectedNameTop(HorizontalLayoutNode node) => node.Y + 40 + (node.Height - CanvasNameHeight) / 2;
    private sealed record Option(Guid Id, string Name);
    private sealed record OrderGroup(string Key, IReadOnlyList<Guid> People, Guid? ParentId);
    private sealed record PartnerOption(Guid UnionId, Guid? Id, string Name, FamilyUnionKind Kind, PersonSex Sex)
    {
        public string Label => $"{(Kind == FamilyUnionKind.Marriage ? "婚配" : Kind == FamilyUnionKind.Partnership ? "伴侣" : "类型未详")}：{Name}";
    }
    private PuJiang.V1.Persistence.IsolatedSample? _v1Sample;
    private bool _v1Opening;
    private readonly TargetContextCoordinator _coordinator;
    private readonly WorkbenchDraftSession _session;
    private readonly ListBox _genealogies = new() { DisplayMemberPath = nameof(Option.Name) };
    private readonly ComboBox _charts = new() { DisplayMemberPath = nameof(Option.Name) };
    private readonly TextBox _newChartName = new();
    private readonly ListBox _people = new() { DisplayMemberPath = nameof(Option.Name) };
    private readonly ListBox _diagnostics = new() { Height = 90 };
    private readonly TextBox _name = new();
    private readonly TextBox _generation = new();
    private readonly TextBox _displayRank = new();
    private readonly Button _applyRank = new() { Content = "设置显示顺序", Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button _saveChartOrder = new() { Content = "保存当前图表顺序", Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button _discardChartOrders = new() { Content = "放弃未保存的图表顺序", Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _recordedGeneration = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };
    private readonly TextBlock _draftParents = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _unionList = new() { DisplayMemberPath = nameof(UnionOption.Label) };
    private readonly ComboBox _unionKind = new() { ItemsSource = Enum.GetValues<FamilyUnionKind>() };
    private readonly TextBox _unionStart = new(), _unionEnd = new(), _unionNote = new() { AcceptsReturn = true, Height = 55, TextWrapping = TextWrapping.Wrap };
    private readonly Button _unionLocate = new() { Content = "定位配偶", Padding = new Thickness(6), Margin = new Thickness(0, 6, 4, 0) };
    private readonly Button _unionAdd = new() { Content = "新增配偶", Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0) };
    private sealed record UnionOption(Guid Id, Guid? PartnerId, string Label);
    private sealed record ChildParentOption(Guid? Id, string Label);
    private readonly StackPanel _childrenPanel = new();
    private Guid? _unionOwner;
    private readonly TextBox _birth = new();
    private readonly TextBox _death = new();
    private readonly TextBox _biography = new() { AcceptsReturn = true, Height = 120, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _sex = new() { ItemsSource = Enum.GetValues<PersonSex>() };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private InlineNameEditor? _inlineNameEditor;
    private Func<Point?>? _inlineLocation;
    private bool _inlineInvalid;
    private readonly Canvas _canvas = new() { Background = Brushes.White, Width = 650, Height = 360, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ScrollViewer _canvasScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ScaleTransform _canvasScale = new(1, 1);
    private Point? _panStart;
    private Point _panOffset;
    private bool _panDragged;
    private readonly DispatcherTimer _canvasMonitor = new() { Interval = TimeSpan.FromSeconds(1) };
    private WorkbenchProjectionSession? _projection;
    private long _projectionRevision;
    private readonly Dictionary<Guid, ProjectionReadSession.PersonRow> _canvasPeople = new();
    private readonly Dictionary<Guid, IReadOnlyList<PartnerOption>> _canvasPartners = new();
    private readonly HashSet<Guid> _incompleteUnions = new();
    private readonly Dictionary<Guid, IReadOnlyList<ProjectionReadSession.SiblingRow>> _canvasSiblings = new();
    private readonly HashSet<Guid> _noParents = new();
    private readonly List<HorizontalLayoutRelationship> _canvasEdges = [];
    private readonly Dictionary<Guid, OrderGroup> _orderGroups = new();
    private readonly Dictionary<string, Dictionary<Guid, int>> _sessionOrders = new();
    private readonly Dictionary<Guid, int> _layoutOrders = new();
    private Point? _orderDragStart;
    private Point? _spouseDragStart;
    private readonly Dictionary<Guid, Guid> _createdChildDisplayParents = new();
    private readonly Dictionary<Guid, int> _createdChildDisplayOrder = new();
    private int _nextCreatedChildOrder;
    private int _unresolvedCount;
    private bool _canvasTruncated;
    private bool _generationEdited;
    private readonly List<string> _diagnosticMessages = [];
    private bool _birthEdited;
    private bool _deathEdited;
    private bool _biographyEdited;
    private Func<Guid, Task>? _afterCommit;
    private Guid? _genealogy;
    private Guid? _chart;
    private long? _chartRevision;
    private Guid? _person;
    private bool _updating;
    private bool _busy;
    private bool _approvedClose;
    private bool _canvasPolling;
    private bool _canvasValid;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Func<DraftLeaveChoice>? ChooseForTest { get; set; }
    internal Func<bool>? DiscardOrdersOnCloseForTest { get; set; }
    internal WorkbenchDraftSession SessionForTest => _session;
    internal string StatusForTest => _status.Text;
    internal string DraftParentsForTest => _draftParents.Text;
    internal int UnionCountForTest => _unionList.Items.Count;
    internal Guid? SelectedUnionForTest => (_unionList.SelectedItem as UnionOption)?.Id;
    internal void SelectUnionForTest(int index) => _unionList.SelectedIndex = index;
    internal void SetUnionForTest(FamilyUnionKind kind, string start, string end, string note)
    { _unionKind.SelectedItem = kind; _unionStart.Text = start; _unionEnd.Text = end; _unionNote.Text = note; }
    internal string UnionNoteForTest => _unionNote.Text;
    internal bool UnionLocateEnabledForTest => _unionLocate.IsEnabled;
    internal void LocateUnionForTest() => _unionLocate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal int ChildCountForTest => _session.Draft?.Original.Children?.Count ?? 0;
    internal Guid? ChildOtherParentForTest(Guid childId) => _session.Draft!.ChildOtherParent(childId);
    internal void SetChildOtherParentForTest(Guid childId, Guid? parentId)
    {
        var box = _childrenPanel.Children.OfType<StackPanel>().SelectMany(row => row.Children.OfType<ComboBox>()).Single(c => Equals(c.Tag, childId));
        box.SelectedItem = box.Items.OfType<ChildParentOption>().Single(option => option.Id == parentId);
    }
    internal Func<Guid, Task>? AfterCommitAsync { set => _afterCommit = value; }
    internal Task InitializeForTest() => _loaded.Task;
    internal Task OpenV1SampleForTest() => OpenV1SampleAsync();
    internal async Task SelectGenealogyForTest(Guid id)
    {
        _updating = true; _genealogies.SelectedItem = _genealogies.Items.OfType<Option>().Single(x => x.Id == id); _updating = false;
        await ChangeGenealogyAsync();
    }
    internal async Task SelectPersonForTest(Guid id)
    {
        _updating = true; _people.SelectedItem = _people.Items.OfType<Option>().Single(x => x.Id == id); _updating = false;
        await ChangePersonAsync();
    }
    internal async Task SelectChartForTest(Guid id)
    {
        _updating = true; _charts.SelectedItem = _charts.Items.OfType<Option>().Single(x => x.Id == id); _updating = false;
        await ChangeChartAsync();
    }
    internal void SetNameForTest(string name) => _name.Text = name;
    internal void SetSexForTest(PersonSex sex) => _sex.SelectedItem = sex;
    internal Task SaveForTest() => SaveAsync();
    internal bool CanvasContainsForTest(Guid id) => _canvasPeople.ContainsKey(id);
    internal string? CanvasNameForTest(Guid id) => _canvasPeople.GetValueOrDefault(id)?.Name;
    internal int CanvasColumnForTest(Guid id) => _canvasPeople.TryGetValue(id, out var person) && RecordedGenerationParser.TryParse(person.RecordedGeneration, out var generation) ? generation : 0;
    internal int CanvasEdgesForTest => _canvasEdges.Count;
    internal IReadOnlyList<string> DiagnosticsForTest => _diagnosticMessages.ToArray();
    internal IReadOnlyList<string> CanvasPartnersForTest(Guid id) => _canvasPartners.TryGetValue(id, out var partners) ? partners.Select(p => p.Label).ToArray() : [];
    internal IReadOnlyList<string> CanvasSiblingsForTest(Guid id) => _canvasSiblings.TryGetValue(id, out var siblings) ? siblings.Select(p => p.Name).ToArray() : [];
    internal Task BeginNewForTest(Guid? parentId = null, PersonRelationshipKind? kind = null) => BeginNewAsync(parentId, kind);
    internal Task BeginInlineRootForTest(int generation) => BeginInlineRootAsync(generation);
    internal void SetGenerationForTest(string value) => _generation.Text = value;
    internal Task PollCanvasForTest() => PollCanvasAsync();
    internal Task ReopenCanvasForTest() => ReopenCanvasAsync();
    internal Task SelectCanvasForTest(Guid id) => SelectCanvasPersonAsync(id);
    internal Task ClearCanvasSelectionForTest() => ClearCanvasSelectionAsync();
    internal Border CanvasCardForTest(Guid id) => _canvas.Children.OfType<Border>().Single(item => item.Tag is Guid value && value == id);
    internal Canvas CanvasForTest => _canvas;
    internal ScrollViewer CanvasScrollForTest => _canvasScroll;
    internal void RenderCanvasForTest() => RenderCanvas();
    internal void BeginProjectedNameInput(Func<HorizontalLayoutNode> project, Action<string> confirmed) =>
        ShowInlineNameEditor(() => { var node = project(); return new Point(node.X + 20, ProjectedNameTop(node)); }, "", confirmed);
    internal Task SetDisplayRankForTest(int rank) { _displayRank.Text = rank.ToString(); return SetDisplayRankAsync(); }
    internal Task MoveForTest(int delta) => MoveSiblingAsync(delta);
    internal Task SaveChartOrderForTest() => SaveChartOrderAsync();
    internal Task MoveSpouseToForTest(Guid owner, Guid source, Guid target) => MoveSpouseToAsync(owner, source, target);

    public WorkbenchProfileWindow(TargetContextCoordinator coordinator) : this(coordinator, WorkbenchDraftSession.Create(coordinator)) { }
    internal WorkbenchProfileWindow(TargetContextCoordinator coordinator, WorkbenchDraftSession session)
    {
        _coordinator = coordinator; _session = session;
        Title = "世系编修工作台 — 画布试用"; Width = 1100; Height = 720; MinWidth = 760; MinHeight = 480;
        _session.AfterCommitAsync = RefreshAfterCommitAsync;
        _session.BeforeCommit = draft =>
        {
            if (_chart is not null && _sessionOrders.Count > 0)
                throw new InvalidOperationException("请先保存或放弃未保存的图表顺序，再保存人物草稿。人物草稿仍保留。");
            if (!draft.Original.IsNew && (draft.Values.RecordedGeneration != draft.Original.Values.RecordedGeneration ||
                draft.Values.Sex != draft.Original.Values.Sex || draft.HasChildParentChanges) &&
                _chart is Guid chart && _chartRevision is long revision)
                draft.AttachChart(chart, revision);
        };
        var outer = new DockPanel(); DockPanel.SetDock(_status, Dock.Bottom); outer.Children.Add(_status);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var left = new DockPanel { Margin = new Thickness(12) };
        var title = new TextBlock { Text = "谱系管理", FontSize = 17, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(title, Dock.Top); left.Children.Add(title);
        var chartPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        chartPanel.Children.Add(new TextBlock { Text = "当前图表" }); chartPanel.Children.Add(_charts);
        chartPanel.Children.Add(new TextBlock { Text = "新图表名称", Margin = new Thickness(0, 6, 0, 0) }); chartPanel.Children.Add(_newChartName);
        var createChart = new Button { Content = "新建图表", Margin = new Thickness(0, 4, 0, 0) };
        createChart.Click += async (_, _) => await CreateChartAsync(); chartPanel.Children.Add(createChart);
        DockPanel.SetDock(chartPanel, Dock.Bottom); left.Children.Add(chartPanel); left.Children.Add(_genealogies);
        columns.Children.Add(left);
        var center = new DockPanel { Margin = new Thickness(8) };
        var notice = new TextBlock { Text = "右键画布新建人物，右键人物编辑或新增子女。未识别世代及未决关系保留在辅助列表；排列不代表谱载排行。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(notice, Dock.Top); center.Children.Add(notice);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var create = new Button { Content = "新建人物", Padding = new Thickness(6) };
        create.Click += async (_, _) => await BeginInlineRootAsync(1); tools.Children.Add(create);
        var isolated = new Button { Content = "V1 隔离样本", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
        isolated.Click += async (_, _) => await OpenV1SampleAsync(); tools.Children.Add(isolated);
        var reopen = new Button { Content = "重新打开画布", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
        reopen.Click += async (_, _) => await ReopenCanvasAsync(); tools.Children.Add(reopen);
        DockPanel.SetDock(tools, Dock.Top); center.Children.Add(tools);
        _people.Height = 100;
        var peoplePanel = new Expander { Header = "人物辅助列表（含未识别世代）", Content = _people, IsExpanded = false };
        DockPanel.SetDock(peoplePanel, Dock.Top); center.Children.Add(peoplePanel);
        var diagnosticsPanel = new Expander { Header = "未显示或待处理的世代与关系", Content = _diagnostics, IsExpanded = false };
        DockPanel.SetDock(diagnosticsPanel, Dock.Top); center.Children.Add(diagnosticsPanel);
        _canvas.LayoutTransform = _canvasScale; _canvasScroll.Content = _canvas; center.Children.Add(_canvasScroll);
        _canvasScroll.ScrollChanged += (_, _) => FillCanvasViewport();
        _canvasScroll.SizeChanged += (_, _) => FillCanvasViewport();
        Grid.SetColumn(center, 1); columns.Children.Add(center);
        var right = new StackPanel { Margin = new Thickness(12) };
        right.Children.Add(new TextBlock { Text = "人物资料", FontSize = 17 });
        AddField(right, "姓名", _name); AddField(right, "世代（1–1000）", _generation); right.Children.Add(_recordedGeneration);
        right.Children.Add(_draftParents);
        AddField(right, "同胞显示顺序", _displayRank); right.Children.Add(_applyRank); right.Children.Add(_saveChartOrder); right.Children.Add(_discardChartOrders);
        _applyRank.Click += async (_, _) => await SetDisplayRankAsync();
        _saveChartOrder.Click += async (_, _) => await SaveChartOrderAsync();
        _discardChartOrders.Click += async (_, _) =>
        {
            _sessionOrders.Clear();
            try { if (_canvasValid) await LoadCanvasAsync(); ShowDraft(); _status.Text = "已放弃未保存的图表顺序。"; }
            catch (Exception ex) { ClearCanvas(); _canvasMonitor.Stop(); ShowDraft(); _status.Text = $"已放弃顺序草稿，画布需要重新打开：{ex.Message}"; }
        };
        AddField(right, "性别", _sex); AddField(right, "出生", _birth);
        AddField(right, "逝世", _death); AddField(right, "生平", _biography);
        var save = new Button { Content = "保存档案", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8) };
        save.Click += async (_, _) => await SaveAsync(); right.Children.Add(save);
        var unionPanel = new StackPanel();
        unionPanel.Children.Add(new TextBlock { Text = "选择图表后按该图表保存的配偶顺序显示。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray });
        AddField(unionPanel, "配偶／关系", _unionList); AddField(unionPanel, "关系类型", _unionKind);
        AddField(unionPanel, "开始时间", _unionStart); AddField(unionPanel, "结束时间", _unionEnd); AddField(unionPanel, "备注", _unionNote);
        var unionButtons = new StackPanel { Orientation = Orientation.Horizontal }; unionButtons.Children.Add(_unionLocate); unionButtons.Children.Add(_unionAdd); unionPanel.Children.Add(unionButtons);
        right.Children.Add(new Expander { Header = "配偶与婚配资料", Content = unionPanel, Margin = new Thickness(0, 10, 0, 0) });
        right.Children.Add(new Expander { Header = "子女信息", Content = _childrenPanel, Margin = new Thickness(0, 10, 0, 0) });
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(rightScroll, 2); columns.Children.Add(rightScroll); outer.Children.Add(columns); Content = outer;
        _genealogies.SelectionChanged += async (_, _) => await ChangeGenealogyAsync();
        _charts.SelectionChanged += async (_, _) => await ChangeChartAsync();
        _people.SelectionChanged += async (_, _) => await ChangePersonAsync();
        _name.TextChanged += (_, _) => UpdateDraft();
        _generation.TextChanged += (_, _) => { if (!_updating) _generationEdited = true; UpdateDraft(); if (!_updating && _inlineNameEditor is null) RenderCanvas(); };
        _birth.TextChanged += (_, _) => { if (!_updating) _birthEdited = true; UpdateDraft(); };
        _death.TextChanged += (_, _) => { if (!_updating) _deathEdited = true; UpdateDraft(); };
        _biography.TextChanged += (_, _) => { if (!_updating) _biographyEdited = true; UpdateDraft(); };
        _sex.SelectionChanged += (_, _) => UpdateDraft();
        _unionList.SelectionChanged += (_, _) => ShowSelectedUnion();
        _unionKind.SelectionChanged += (_, _) => UpdateUnionDraft();
        _unionStart.TextChanged += (_, _) => UpdateUnionDraft(); _unionEnd.TextChanged += (_, _) => UpdateUnionDraft(); _unionNote.TextChanged += (_, _) => UpdateUnionDraft();
        _unionLocate.Click += async (_, _) => { if (_unionList.SelectedItem is UnionOption { PartnerId: Guid id }) await SelectCanvasPersonAsync(id); };
        _unionAdd.Click += async (_, _) => { if (_person is Guid id) await BeginInlineSpouseFromCanvasAsync(id); };
        Loaded += async (_, _) => { try { await LoadGenealogiesAsync(); } finally { _loaded.TrySetResult(); } };
        _canvasMonitor.Tick += async (_, _) => await PollCanvasAsync();
        _canvas.MouseRightButtonDown += (_, e) => { if (e.OriginalSource != _canvas) return; e.Handled = true; ShowCanvasMenu(e.GetPosition(_canvas)); };
        _canvasScroll.PreviewMouseWheel += OnCanvasWheel;
        _canvas.MouseLeftButtonDown += OnCanvasDown; _canvas.MouseMove += OnCanvasMove; _canvas.MouseLeftButtonUp += OnCanvasUp;
        _canvas.LostMouseCapture += (_, _) => _panStart = null;
        PreviewKeyDown += async (_, e) =>
        {
            if (_inlineNameEditor is not null)
            {
                if (IsCanvasShortcutBlocked(e.Key, Keyboard.Modifiers))
                    e.Handled = true;
                return;
            }
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; await BeginInlineRootAsync(1); }
        };
        PreviewMouseDown += (_, e) =>
        {
            if (_inlineNameEditor is not null && e.ChangedButton == MouseButton.Left && ReferenceEquals(e.OriginalSource, _canvas)) return;
            if (_inlineNameEditor is not null && e.OriginalSource is DependencyObject source &&
                !ReferenceEquals(source, _inlineNameEditor) && !_inlineNameEditor.IsAncestorOf(source))
            { e.Handled = true; BlockWhileInlineEditing(); }
        };
        Closing += OnClosing;
        ShowDraft();
    }
    private async Task OpenV1SampleAsync()
    {
        if (_v1Opening) return;
        var existing = OwnedWindows.OfType<V1FirstRootWindow>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return; }
        _v1Opening = true;
        try
        {
            _v1Sample ??= await PuJiang.V1.Persistence.IsolatedSample.CreateAsync();
            var app = new PuJiang.V1.Application.FirstRootApplication(new PuJiang.V1.Persistence.IsolatedFirstRootStore(_v1Sample.Database));
            new V1FirstRootWindow(app, _v1Sample.GenealogyId, _v1Sample.LineageId, _v1Sample.Surname) { Owner = this }.Show();
        }
        catch (Exception error) { _status.Text = $"隔离样本无法打开：{error.Message}"; }
        finally { _v1Opening = false; }
    }
    private static void AddField(Panel panel, string label, Control field)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) }); panel.Children.Add(field);
    }
    internal static bool IsCanvasShortcutBlocked(Key key, ModifierKeys modifiers) =>
        (modifiers & ModifierKeys.Alt) != 0 ||
        (modifiers == ModifierKeys.Control && (key == Key.N || key == Key.S)) || key is Key.F2 or Key.Tab;
    private void OnCanvasWheel(object sender, MouseWheelEventArgs e)
    {
        var point = e.GetPosition(_canvasScroll); var old = _canvasScale.ScaleX;
        var next = Math.Clamp(old * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.4, 2.5);
        var x = (_canvasScroll.HorizontalOffset + point.X) / old; var y = (_canvasScroll.VerticalOffset + point.Y) / old;
        _canvasScale.ScaleX = _canvasScale.ScaleY = next; _canvasScroll.UpdateLayout();
        _canvasScroll.ScrollToHorizontalOffset(x * next - point.X); _canvasScroll.ScrollToVerticalOffset(y * next - point.Y); e.Handled = true;
    }
    private void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != _canvas || _busy) return;
        _panStart = e.GetPosition(_canvasScroll); _panOffset = new Point(_canvasScroll.HorizontalOffset, _canvasScroll.VerticalOffset); _panDragged = false;
        _canvas.CaptureMouse(); e.Handled = true;
    }
    private void FillCanvasViewport()
    {
        if (_canvasScroll.ViewportWidth > 0 && _canvasScroll.ViewportHeight > 0)
        {
            var scale = _canvasScale.ScaleX;
            _canvas.MinWidth = Math.Max(0, (_canvasScroll.ViewportWidth - 1) / scale);
            _canvas.MinHeight = Math.Max(0, (_canvasScroll.ViewportHeight - 1) / scale);
        }
    }
    private void OnCanvasMove(object sender, MouseEventArgs e)
    {
        if (_panStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(_canvasScroll) - start;
        if (Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance) _panDragged = true;
        if (!_panDragged) return;
        _canvasScroll.ScrollToHorizontalOffset(_panOffset.X - delta.X); _canvasScroll.ScrollToVerticalOffset(_panOffset.Y - delta.Y);
    }
    private async void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_panStart is null) return;
        var confirm = !_panDragged && _inlineNameEditor is not null;
        _panStart = null; _canvas.ReleaseMouseCapture(); e.Handled = true;
        if (confirm) _inlineNameEditor?.Confirm();
        else if (!_panDragged && _inlineNameEditor is null) await ClearCanvasSelectionAsync();
    }
    private async Task ClearCanvasSelectionAsync()
    {
        if (_busy || _inlineNameEditor is not null || _person is null) return;
        var anchor = CaptureCanvasAnchor(_person.Value);
        _busy = true; IsEnabled = false;
        try
        {
            var prior = _session.Draft;
            var result = await _session.LeaveAsync(Choose()); SyncSavedPerson(prior);
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); ShowDraft(); return; }
            _person = null; RestoreSelection(); ShowDraft(); RenderCanvas(); RestoreCanvasAnchor(anchor); _status.Text = "已取消选择人物。";
        }
        catch (Exception ex) { _status.Text = $"无法取消选择，草稿保留：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private void ClearCanvas()
    {
        _canvasValid = false;
        var editor = _inlineNameEditor;
        var restoreFocus = editor?.IsKeyboardFocusWithin == true;
        _canvasPeople.Clear(); _canvasPartners.Clear(); _incompleteUnions.Clear(); _canvasSiblings.Clear(); _noParents.Clear(); _canvasEdges.Clear(); _createdChildDisplayParents.Clear(); _createdChildDisplayOrder.Clear(); _nextCreatedChildOrder = 0; _orderGroups.Clear(); if (_chart is null) _sessionOrders.Clear(); _layoutOrders.Clear(); _diagnosticMessages.Clear(); _diagnostics.ItemsSource = null;
        _unresolvedCount = 0; _canvasTruncated = false; _canvas.Children.Clear();
        if (editor is not null)
        {
            _inlineInvalid = true; _canvas.Children.Add(editor);
            if (restoreFocus) editor.Input.Focus();
        }
    }
    private void RenderCanvas()
    {
        var editor = _inlineNameEditor;
        var restoreFocus = editor?.IsKeyboardFocusWithin == true;
        _canvas.Children.Clear();
        if (!_canvasValid) { RestoreInlineEditor(editor, restoreFocus); return; }
        var people = CanvasLayoutPeople().ToList();
        var nodeHeight = CanvasNodeHeight;
        var edges = _canvasEdges.ToList();
        var rootDraft = IsInlineRootDraft() && _session.Draft is { } pendingRoot && _person == pendingRoot.Original.PersonId ? pendingRoot : null;
        if (rootDraft is not null && RecordedGenerationParser.TryParse(rootDraft.Values.RecordedGeneration, out var rootGeneration))
            people.Add(new HorizontalLayoutPerson(rootDraft.Original.PersonId, rootGeneration - 1, RootOrder(), CanvasNodeWidth, nodeHeight));
        if (people.Count == 0) { RestoreInlineEditor(editor, restoreFocus); return; }
        var childDraft = _session.Draft is { Original.IsOrdinaryChild: true, Original.IsNew: true } pending &&
            (_person == pending.Original.PersonId || (_inlineNameEditor is not null && _person == pending.Original.ParentId)) ? pending : null;
        var siblingDraft = _session.Draft is { Original.IsOrdinarySibling: true, Original.IsNew: true } siblingPending &&
            (_person == siblingPending.Original.PersonId || (_inlineNameEditor is not null && _person == siblingPending.Original.SiblingId)) ? siblingPending : null;
        if (childDraft is not null && childDraft.Original.ParentId is Guid parent && RecordedGenerationParser.TryParse(childDraft.Values.RecordedGeneration, out var childGeneration) && _canvasPeople.ContainsKey(parent))
        {
            people.Add(new HorizontalLayoutPerson(childDraft.Original.PersonId, childGeneration - 1, NextChildOrder(parent), CanvasNodeWidth, nodeHeight));
            if (childGeneration > ParseGeneration(_canvasPeople[parent]))
                edges.Add(new HorizontalLayoutRelationship(childDraft.Original.PersonId, parent, childDraft.Original.PersonId));
            else
                _status.Text = "子女草稿的世代不晚于父母，暂不绘制连线，请核实世代。";
        }
        if (siblingDraft is not null && RecordedGenerationParser.TryParse(siblingDraft.Values.RecordedGeneration, out var siblingGeneration))
        {
            var source = siblingDraft.Original.SiblingId!.Value;
            {
                var parentEdge = edges.FirstOrDefault(e => e.ChildId == source);
                var order = parentEdge is null ? RootOrder() : NextChildOrder(parentEdge.ParentId);
                people.Add(new HorizontalLayoutPerson(siblingDraft.Original.PersonId, siblingGeneration - 1, order, CanvasNodeWidth, nodeHeight));
                if (parentEdge is not null && _canvasPeople.TryGetValue(parentEdge.ParentId, out var siblingParent) && siblingGeneration > ParseGeneration(siblingParent))
                    edges.Add(new HorizontalLayoutRelationship(siblingDraft.Original.PersonId, parentEdge.ParentId, siblingDraft.Original.PersonId));
            }
        }
        var generations = people.ToDictionary(p => p.Id, p => p.Generation);
        var hiddenEdges = edges.RemoveAll(e => generations[e.ParentId] >= generations[e.ChildId]);
        if (hiddenEdges > 0) _status.Text = "草稿世代与父母或子女冲突，相关连线暂不显示；请核实世代。";
        var layout = HorizontalLineageLayout.Calculate(people, edges, people.Select(p => p.Id).ToHashSet(), allowSkippedGenerations: true);
        if (HasUnsafeCrossing(layout))
        {
            _unresolvedCount += edges.Count;
            _diagnosticMessages.Add("部分连线会穿越其他人物或不相关连线，已暂停绘制，请在关系视图中复核。");
            _diagnostics.ItemsSource = _diagnosticMessages.ToArray();
            edges.Clear();
            layout = HorizontalLineageLayout.Calculate(people, edges, people.Select(p => p.Id).ToHashSet(), allowSkippedGenerations: true);
        }
        foreach (var generation in layout.Nodes.Select(n => n.Generation).Distinct())
        {
            var heading = new TextBlock { Text = $"第 {generation + 1} 世", FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(heading, generation * (CanvasNodeWidth + 64) + 20); Canvas.SetTop(heading, 10); _canvas.Children.Add(heading);
        }
        foreach (var edge in layout.Edges)
        {
            var line = new System.Windows.Shapes.Polyline { Stroke = Brushes.SlateGray, StrokeThickness = 1.5, IsHitTestVisible = false };
            foreach (var point in edge.Points) line.Points.Add(new Point(point.X + 20, point.Y + 40));
            _canvas.Children.Add(line);
        }
        foreach (var node in layout.Nodes)
        {
            if (childDraft is not null && node.PersonId == childDraft.Original.PersonId && _person != node.PersonId) continue;
            if (siblingDraft is not null && node.PersonId == siblingDraft.Original.PersonId && _person != node.PersonId) continue;
            var newDraft = childDraft ?? siblingDraft ?? rootDraft;
            var person = _canvasPeople.TryGetValue(node.PersonId, out var existing) ? existing :
                new ProjectionReadSession.PersonRow(node.PersonId, newDraft!.Values.Name, newDraft.Values.RecordedGeneration, null, newDraft.Values.Sex);
            var displayedName = _person == person.Id && _session.Draft is { } draft &&
                draft.Original.PersonId == person.Id && draft.Original.GenealogyId == _genealogy
                ? draft.Values.Name : person.Name;
            var content = new StackPanel();
            if (_person == person.Id)
                content.Children.Add(new TextBlock { Text = $"世代：{(_person == _session.Draft?.Original.PersonId ? _session.Draft.Values.RecordedGeneration : person.RecordedGeneration) ?? "未记"}　排行：{person.RecordedBirthOrder ?? "未记"}", FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            _canvasPartners.TryGetValue(person.Id, out var partners);
            var spouseDraft = _session.Draft is { Original.IsNew: true, Original.PartnerId: Guid draftPartnerId } pendingSpouse &&
                draftPartnerId == person.Id && _person == pendingSpouse.Original.PersonId ? pendingSpouse : null;
            var showPartners = _person == person.Id || (_person is Guid selectedPerson && partners?.Any(p => p.Id == selectedPerson) == true) || spouseDraft is not null;
            var shownPartners = showPartners ? (partners ?? Array.Empty<PartnerOption>()).ToList() : new List<PartnerOption>();
            if (spouseDraft is not null) shownPartners.Add(new PartnerOption(Guid.Empty, spouseDraft.Original.PersonId, spouseDraft.Values.Name, FamilyUnionKind.Marriage, spouseDraft.Values.Sex));
            if (_person == person.Id && _canvasSiblings.TryGetValue(person.Id, out var siblings))
            {
                foreach (var sibling in siblings)
                {
                    var label = new TextBlock { Text = $"兄妹：{sibling.Name}", Foreground = Brushes.SteelBlue, TextTrimming = TextTrimming.CharacterEllipsis };
                    label.MouseLeftButtonDown += async (_, e) => { e.Handled = true; await SelectCanvasPersonAsync(sibling.PersonId); };
                    content.Children.Add(label);
                }
            }
            var card = new Border { Width = node.Width, Height = CanvasNameHeight, BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(3), Background = _person == person.Id ? Brushes.DarkOrange : Brushes.WhiteSmoke, Padding = new Thickness(4),
                ToolTip = displayedName, Tag = person.Id, Child = new TextBlock { Text = $"{SexIcon(_person == person.Id && _session.Draft?.Original.PersonId == person.Id ? _session.Draft.Values.Sex : person.Sex)} {displayedName}", Foreground = _person == person.Id ? Brushes.White : Brushes.Black,
                    FontWeight = _person == person.Id ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            card.AllowDrop = true;
            card.PreviewMouseLeftButtonDown += (_, e) => { _orderDragStart = e.GetPosition(card); if (_chart is not null && _person == person.Id) card.CaptureMouse(); };
            card.PreviewMouseLeftButtonUp += (_, _) => { _orderDragStart = null; if (card.IsMouseCaptured) card.ReleaseMouseCapture(); };
            card.MouseLeftButtonDown += async (_, e) => { e.Handled = true; if (_person != person.Id) await SelectCanvasPersonAsync(person.Id); };
            card.PreviewMouseMove += (_, e) =>
            {
                if (_chart is null || _person != person.Id || e.LeftButton != MouseButtonState.Pressed || _orderDragStart is not Point start) return;
                var point = e.GetPosition(card);
                if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                _orderDragStart = null;
                if (card.IsMouseCaptured) card.ReleaseMouseCapture();
                DragDrop.DoDragDrop(card, new DataObject("PuJiang.ChartPerson", person.Id.ToString("D")), DragDropEffects.Move);
            };
            card.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent("PuJiang.ChartPerson") ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            card.Drop += async (_, e) =>
            {
                e.Handled = true;
                if (e.Data.GetData("PuJiang.ChartPerson") is string value && Guid.TryParse(value, out var source)) await MoveSiblingToAsync(source, person.Id);
            };
            card.MouseRightButtonDown += async (_, e) =>
            {
                e.Handled = true; await SelectCanvasPersonAsync(person.Id);
                if (_person != person.Id || _session.Draft?.Original.IsNew != false) return;
                var menu = new ContextMenu();
                var edit = new MenuItem { Header = "编辑人物" }; edit.Click += (_, _) => _name.Focus(); menu.Items.Add(edit);
                var inline = new MenuItem { Header = "原位编辑姓名" }; inline.Click += (_, _) => BeginInlineNameEdit(person.Id); menu.Items.Add(inline);
                var child = new MenuItem { Header = "新增子女（生物关系）" }; child.Click += async (_, _) => await BeginOrdinaryChildFromCanvasAsync(person.Id); menu.Items.Add(child);
                var adopted = new MenuItem { Header = "新增子女（收养关系）" }; adopted.Click += async (_, _) => await BeginNewAsync(person.Id, PersonRelationshipKind.AdoptiveParent); menu.Items.Add(adopted);
                var sibling = new MenuItem { Header = _noParents.Contains(person.Id) ? "新增同世人物" : "新增兄妹" }; sibling.Click += async (_, _) => await BeginSiblingAsync(person.Id); menu.Items.Add(sibling);
                var up = new MenuItem { Header = "同胞上移", IsEnabled = CanMove(person.Id, -1) }; up.Click += async (_, _) => await MoveSiblingAsync(-1); menu.Items.Add(up);
                var down = new MenuItem { Header = "同胞下移", IsEnabled = CanMove(person.Id, 1) }; down.Click += async (_, _) => await MoveSiblingAsync(1); menu.Items.Add(down);
                var spouse = new MenuItem { Header = "新增配偶（婚配）" }; spouse.Click += async (_, _) => await BeginInlineSpouseFromCanvasAsync(person.Id); menu.Items.Add(spouse);
                var target = _canvas.Children.OfType<Border>().FirstOrDefault(item => item.Tag is Guid id && id == person.Id);
                if (target is null) return;
                target.ContextMenu = menu; menu.PlacementTarget = target; menu.IsOpen = true;
            };
            Canvas.SetLeft(card, node.X + 20); Canvas.SetTop(card, ProjectedNameTop(node)); _canvas.Children.Add(card);
            if (content.Children.Count > 0)
            {
                var details = new Border { Child = content, Width = CanvasNodeWidth, Background = Brushes.White };
                Canvas.SetLeft(details, node.X + 20); Canvas.SetTop(details, ProjectedNameTop(node) - 20); _canvas.Children.Add(details);
            }
            for (var index = 0; index < shownPartners.Count; index++)
            {
                var partner = shownPartners[index]; var selected = partner.Id is Guid partnerIdentity && partnerIdentity == _person;
                var top = ProjectedNameTop(node) + CanvasNameHeight + 8 + index * 38;
                _canvas.Children.Add(new System.Windows.Shapes.Line { X1 = node.X + 20 + node.Width / 2, X2 = node.X + 20 + node.Width / 2,
                    Y1 = index == 0 ? ProjectedNameTop(node) + CanvasNameHeight : top - 8, Y2 = top,
                    Stroke = Brushes.SlateGray, StrokeThickness = 1.5, IsHitTestVisible = false });
                var partnerCard = new Border { Width = node.Width, Height = CanvasNameHeight, Tag = partner.Id,
                    Background = selected ? Brushes.DarkOrange : Brushes.WhiteSmoke, CornerRadius = new CornerRadius(3), Padding = new Thickness(4),
                    ToolTip = partner.Label, Child = new TextBlock { Text = $"{SexIcon(partner.Sex)} {partner.Name}",
                        Foreground = selected ? Brushes.White : Brushes.Black, TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                if (partner.Id is Guid partnerId && spouseDraft?.Original.PersonId != partnerId)
                {
                    if (_chart is not null && _person == person.Id && shownPartners.Count > 1)
                    {
                        partnerCard.AllowDrop = true;
                        partnerCard.PreviewMouseLeftButtonDown += (_, e) => { _spouseDragStart = e.GetPosition(partnerCard); partnerCard.CaptureMouse(); e.Handled = true; };
                        partnerCard.PreviewMouseLeftButtonUp += async (_, e) =>
                        {
                            if (_spouseDragStart is null) return;
                            _spouseDragStart = null; if (partnerCard.IsMouseCaptured) partnerCard.ReleaseMouseCapture(); e.Handled = true; await SelectCanvasPersonAsync(partnerId);
                        };
                        partnerCard.PreviewMouseMove += (_, e) =>
                        {
                            if (e.LeftButton != MouseButtonState.Pressed || _spouseDragStart is not Point start) return;
                            var point = e.GetPosition(partnerCard);
                            if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                            _spouseDragStart = null;
                            if (partnerCard.IsMouseCaptured) partnerCard.ReleaseMouseCapture();
                            DragDrop.DoDragDrop(partnerCard, new DataObject("PuJiang.ChartUnion", partner.UnionId.ToString("D")), DragDropEffects.Move);
                        };
                        partnerCard.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent("PuJiang.ChartUnion") ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
                        partnerCard.Drop += async (_, e) =>
                        {
                            e.Handled = true;
                            if (e.Data.GetData("PuJiang.ChartUnion") is string value && Guid.TryParse(value, out var source)) await MoveSpouseToAsync(person.Id, source, partner.UnionId);
                        };
                    }
                    else partnerCard.MouseLeftButtonDown += async (_, e) => { e.Handled = true; await SelectCanvasPersonAsync(partnerId); };
                }
                Canvas.SetLeft(partnerCard, node.X + 20); Canvas.SetTop(partnerCard, top); _canvas.Children.Add(partnerCard);
            }
        }
        _canvas.Width = Math.Max(650, layout.Nodes.Max(n => n.X + n.Width) + 40);
        var extraBelow = people.ToDictionary(p => p.Id, p => p.ExtraBelow);
        _canvas.Height = Math.Max(360, layout.Nodes.Max(n => n.Y + n.Height + extraBelow[n.PersonId]) + 60);
        RestoreInlineEditor(editor, restoreFocus);
    }
    private static string SexIcon(PersonSex sex) => sex switch { PersonSex.Male => "♂", PersonSex.Female => "♀", _ => "○" };
    private bool IsInlineRootDraft() => _session.Draft?.Original is { IsNew: true, ParentId: null, PartnerId: null, SiblingId: null };
    private int RootOrder() => _layoutOrders.Values.DefaultIfEmpty(0).Max() + 1;
    private double CanvasExtraBelow(Guid personId)
    {
        var count = _canvasPartners.Where(pair => pair.Key == personId && (pair.Key == _person || (_person is Guid selected && pair.Value.Any(p => p.Id == selected)) ||
            _session.Draft?.Original.PartnerId == pair.Key))
            .Select(pair => pair.Value.Count + (_session.Draft is { Original.IsNew: true, Original.PartnerId: Guid partner } && partner == pair.Key ? 1 : 0))
            .DefaultIfEmpty(0).Max();
        return Math.Max(0, count - 1) * 38;
    }
    private IEnumerable<HorizontalLayoutPerson> CanvasLayoutPeople()
    {
        var descendants = new HashSet<Guid>(); var delta = 0;
        if (_person is Guid selected && _session.Draft is { Original.IsNew: false } selectedDraft && selectedDraft.Original.PersonId == selected &&
            RecordedGenerationParser.TryParse(selectedDraft.Original.Values.RecordedGeneration, out var originalGeneration) &&
            RecordedGenerationParser.TryParse(selectedDraft.Values.RecordedGeneration, out var editedGeneration))
        {
            delta = editedGeneration - originalGeneration; var queue = new Queue<Guid>(); queue.Enqueue(selected);
            while (queue.TryDequeue(out var parent)) foreach (var edge in _canvasEdges.Where(e => e.ParentId == parent))
                if (edge.ChildId != selected && descendants.Add(edge.ChildId)) queue.Enqueue(edge.ChildId);
        }
        return _canvasPeople.Values.Select(p =>
        {
            var draft = _person == p.Id && _session.Draft?.Original.PersonId == p.Id ? _session.Draft : null;
            var generation = draft is not null && RecordedGenerationParser.TryParse(draft.Values.RecordedGeneration, out var edited)
                ? edited : ParseGeneration(p) + (descendants.Contains(p.Id) ? delta : 0);
            if (generation is < 1 or > 1000) generation = ParseGeneration(p);
            var rootSortGeneration = draft is not null && !draft.Original.IsNew ? ParseGeneration(p) - 1 : (int?)null;
            return new HorizontalLayoutPerson(p.Id, generation - 1, _layoutOrders.GetValueOrDefault(p.Id), CanvasNodeWidth, CanvasNodeHeight, CanvasExtraBelow(p.Id), rootSortGeneration);
        });
    }
    private int NextChildOrder(Guid parent) => _canvasEdges.Where(e => e.ParentId == parent)
        .Select(e => _layoutOrders.GetValueOrDefault(e.ChildId)).DefaultIfEmpty(-1).Max() + 1;
    private void RestoreInlineEditor(InlineNameEditor? editor, bool restoreFocus)
    {
        if (editor is null || !ReferenceEquals(editor, _inlineNameEditor)) return;
        Point? position = null;
        try { if (!_inlineInvalid) position = _inlineLocation?.Invoke(); }
        catch (HorizontalLayoutException) { _inlineInvalid = true; }
        if (position is Point point) { Canvas.SetLeft(editor, point.X); Canvas.SetTop(editor, point.Y); }
        else { _inlineInvalid = true; _status.Text = "输入位置已失效，请复制未应用的输入或按 Esc 取消。"; }
        _canvas.Children.Add(editor);
        if (restoreFocus) editor.Input.Focus();
    }
    private void BeginInlineNameEdit(Guid personId)
    {
        if (_person != personId || _session.Draft is null || _busy || _inlineNameEditor is not null) return;
        Point? Locate()
        {
            var card = _canvas.Children.OfType<Border>().FirstOrDefault(item => item.Tag is Guid id && id == personId);
            return card is null ? null : new Point(Canvas.GetLeft(card), Canvas.GetTop(card));
        }
        ShowInlineNameEditor(Locate, _name.Text, value => _name.Text = value, RenderCanvas);
    }
    private void ShowInlineNameEditor(Func<Point?> locate, string initial, Action<string> confirmed, Action? afterConfirm = null, Action? afterCancel = null)
    {
        if (_inlineNameEditor is not null || locate() is not Point point) return;
        var editor = new InlineNameEditor();
        _inlineNameEditor = editor;
        _inlineLocation = locate; _inlineInvalid = false;
        editor.Input.Text = initial;
        Canvas.SetLeft(editor, point.X);
        Canvas.SetTop(editor, point.Y);
        Panel.SetZIndex(editor, 10);
        _canvas.Children.Add(editor);
        void CloseEditor()
        {
            if (!ReferenceEquals(_inlineNameEditor, editor)) return;
            _canvas.Children.Remove(editor);
            _inlineNameEditor = null;
            _inlineLocation = null; _inlineInvalid = false;
            _name.Focus();
        }
        editor.Confirmed += value =>
        {
            if (_inlineInvalid) { _status.Text = "画布已失效，输入尚未应用；请复制内容或按 Esc 取消，再重新打开画布。"; return; }
            confirmed(value); CloseEditor(); afterConfirm?.Invoke();
        };
        editor.Cancelled += () => { CloseEditor(); afterCancel?.Invoke(); };
        editor.Input.Focus(); editor.Input.SelectAll();
    }
    private bool BlockWhileInlineEditing()
    {
        if (_inlineNameEditor is null) return false;
        _status.Text = _inlineInvalid ? "画布已失效，请复制未应用的输入或按 Esc 取消。" : "请先按 Enter 确认姓名，或按 Esc 取消输入。";
        _inlineNameEditor.Input.Focus();
        return true;
    }
    private static int ParseGeneration(ProjectionReadSession.PersonRow person) =>
        RecordedGenerationParser.TryParse(person.RecordedGeneration, out var value) ? value : throw new InvalidOperationException("人物世代未识别。");
    private static bool HasUnsafeCrossing(HorizontalLayoutResult layout)
    {
        foreach (var edge in layout.Edges)
        {
            for (var i = 1; i < edge.Points.Count; i++)
            {
                var a = edge.Points[i - 1]; var b = edge.Points[i];
                foreach (var node in layout.Nodes.Where(n => n.PersonId != edge.ParentId && n.PersonId != edge.ChildId))
                {
                    if (a.X == b.X && a.X > node.X && a.X < node.X + node.Width && Math.Max(a.Y, b.Y) > node.Y && Math.Min(a.Y, b.Y) < node.Y + node.Height) return true;
                    if (a.Y == b.Y && a.Y > node.Y && a.Y < node.Y + node.Height && Math.Max(a.X, b.X) > node.X && Math.Min(a.X, b.X) < node.X + node.Width) return true;
                }
                foreach (var other in layout.Edges.Where(e => e.RelationshipId.CompareTo(edge.RelationshipId) > 0 && e.ParentId != edge.ParentId && e.ChildId != edge.ChildId && e.ParentId != edge.ChildId && e.ChildId != edge.ParentId))
                for (var j = 1; j < other.Points.Count; j++)
                {
                    var c = other.Points[j - 1]; var d = other.Points[j];
                    if (a.X == b.X && c.Y == d.Y && a.X >= Math.Min(c.X, d.X) && a.X <= Math.Max(c.X, d.X) && c.Y >= Math.Min(a.Y, b.Y) && c.Y <= Math.Max(a.Y, b.Y)) return true;
                    if (a.Y == b.Y && c.X == d.X && c.X >= Math.Min(a.X, b.X) && c.X <= Math.Max(a.X, b.X) && a.Y >= Math.Min(c.Y, d.Y) && a.Y <= Math.Max(c.Y, d.Y)) return true;
                    if (a.X == b.X && c.X == d.X && a.X == c.X && Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) < Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y))) return true;
                    if (a.Y == b.Y && c.Y == d.Y && a.Y == c.Y && Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) < Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X))) return true;
                }
            }
        }
        return false;
    }
    internal static int CanvasGenerationAt(double x) => Math.Clamp((int)Math.Floor(Math.Max(0, x - 20) / (CanvasNodeWidth + 64)) + 1, 1, 1000);
    private void ShowCanvasMenu(Point position)
    {
        var menu = new ContextMenu(); var create = new MenuItem { Header = "新建人物" };
        var generation = CanvasGenerationAt(position.X);
        create.Click += async (_, _) => await BeginInlineRootAsync(generation); menu.Items.Add(create); _canvas.ContextMenu = menu; menu.PlacementTarget = _canvas; menu.IsOpen = true;
    }
    private async Task BeginInlineSpouseFromCanvasAsync(Guid personId)
    {
        if (BlockWhileInlineEditing() || _busy || _genealogy is not Guid genealogy || _person != personId) return;
        if (BlockWhileChartOrderPending()) return;
        if (_incompleteUnions.Contains(personId)) { _status.Text = "婚配记录未完整加载，请先处理后再新增配偶。"; return; }
        _busy = true; IsEnabled = false; var started = false;
        try
        {
            await ValidateCanvasIdentityAsync(personId);
            var prior = _session.Draft;
            var result = _chart is Guid chart
                ? await _session.BeginSpouseForChartAsync(genealogy, personId, chart, Choose())
                : await _session.BeginNewAsync(genealogy, Choose(), partnerId: personId, unionKind: FamilyUnionKind.Marriage);
            SyncSavedPerson(prior);
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); ShowDraft(); return; }
            started = true;
            await ValidateCanvasIdentityAsync(personId);
            var draft = _session.Draft!;
            Point? Locate()
            {
                var card = _canvas.Children.OfType<Border>().FirstOrDefault(item => item.Tag is Guid id && id == personId);
                return card is null ? null : new Point(Canvas.GetLeft(card), Canvas.GetTop(card) + card.Height + 8 + (_canvasPartners.GetValueOrDefault(personId)?.Count ?? 0) * 38);
            }
            _status.Text = "请输入配偶姓名：Enter 或单击画布空白保存，Esc 取消。";
            ShowInlineNameEditor(Locate, "", name =>
            {
                draft.Values = draft.Values with { Name = name };
                _person = draft.Original.PersonId;
                _updating = true; _people.SelectedItem = null; _updating = false;
                ShowDraft();
                _status.Text = "正在保存配偶。";
            }, async () => { RenderCanvas(); await SaveAsync(); }, async () => await CancelNewRelatedPersonAsync(genealogy, personId, draft, "配偶"));
            if (_inlineNameEditor is null) throw new InvalidOperationException("无法显示姓名输入框。");
        }
        catch (Exception ex)
        {
            if (started && _session.Draft is { } draft) await CancelNewRelatedPersonAsync(genealogy, personId, draft, "配偶");
            _status.Text = $"无法新增配偶：{ex.Message}";
        }
        finally { IsEnabled = true; _busy = false; _inlineNameEditor?.Input.Focus(); }
    }
    private async Task BeginOrdinaryChildFromCanvasAsync(Guid parentId)
    {
        if (BlockWhileInlineEditing() || _busy || _genealogy is not Guid genealogy || _person != parentId) return;
        if (BlockWhileChartOrderPending()) return;
        if (_incompleteUnions.Contains(parentId)) { _status.Text = "婚配记录未完整加载，暂不能确定默认配偶。"; return; }
        if (!_canvasPeople.TryGetValue(parentId, out var parent) || !RecordedGenerationParser.TryParse(parent.RecordedGeneration, out var generation) || generation >= 1000)
        { _status.Text = "当前人物的世代无法预填下一世，请先核实世代。"; return; }
        _busy = true; IsEnabled = false; var started = false;
        try
        {
            await ValidateCanvasIdentityAsync(parentId);
            using (var operation = _coordinator.EnterOperation())
            await using (var context = _coordinator.CreateContext())
                _ = _chart is Guid chart ? await new WorkbenchPersonProfileCommand(context).BeginOrdinaryChildForChartAsync(genealogy, parentId, chart)
                    : await new WorkbenchPersonProfileCommand(context).BeginOrdinaryChildAsync(genealogy, parentId);
            var prior = _session.Draft;
            var result = _chart is Guid selectedChart
                ? await _session.BeginOrdinaryChildForChartAsync(genealogy, parentId, selectedChart, Choose())
                : await _session.BeginOrdinaryChildAsync(genealogy, parentId, Choose());
            SyncSavedPerson(prior);
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); ShowDraft(); return; }
            started = true;
            await ValidateCanvasIdentityAsync(parentId);
            var draft = _session.Draft!;
            if (!RecordedGenerationParser.TryParse(draft.Values.RecordedGeneration, out var childGeneration))
                throw new InvalidOperationException("无法确定新子女的世代。请先核实当前人物世代。");
            HorizontalLayoutNode Project()
            {
                var people = CanvasLayoutPeople().ToList();
                people.Add(new HorizontalLayoutPerson(draft.Original.PersonId, childGeneration - 1, NextChildOrder(parentId), CanvasNodeWidth, CanvasNodeHeight));
                var relationships = _canvasEdges.Append(new HorizontalLayoutRelationship(draft.Original.PersonId, parentId, draft.Original.PersonId)).ToArray();
                return InlineNamePlacement.Project(people, relationships, people.Select(p => p.Id).ToHashSet(), draft.Original.PersonId);
            }
            var location = Project();
            _status.Text = "请输入子女姓名：Enter 或单击画布空白保存，Esc 取消。";
            ShowInlineNameEditor(() => { var node = Project(); return new Point(node.X + 20, ProjectedNameTop(node)); }, "", name =>
            {
                draft.Values = draft.Values with { Name = name };
                _person = draft.Original.PersonId;
                _createdChildDisplayParents[_person.Value] = parentId;
                _createdChildDisplayOrder[_person.Value] = _nextCreatedChildOrder++;
                _updating = true; _people.SelectedItem = null; _updating = false;
                ShowDraft();
                _status.Text = "正在保存子女。";
            }, async () => { RenderCanvas(); await SaveAsync(); }, async () => await CancelNewRelatedPersonAsync(genealogy, parentId, draft, "子女"));
            if (_inlineNameEditor is null) throw new InvalidOperationException("无法显示姓名输入框。");
            RenderCanvas();
            _canvas.Width = Math.Max(_canvas.Width, location.X + location.Width + 40);
            _canvas.Height = Math.Max(_canvas.Height, location.Y + location.Height + 60);
        }
        catch (Exception ex)
        {
            if (started && _session.Draft is { } draft) await CancelNewRelatedPersonAsync(genealogy, parentId, draft, "子女");
            _status.Text = $"无法新增子女：{ex.Message}";
        }
        finally { IsEnabled = true; _busy = false; _inlineNameEditor?.Input.Focus(); }
    }
    private async Task CancelNewRelatedPersonAsync(Guid genealogy, Guid parentId, WorkbenchPersonDraft draft, string kind)
    {
        _busy = true; IsEnabled = false;
        try
        {
            if (!ReferenceEquals(_session.Draft, draft)) return;
            var result = await _session.SelectAsync(genealogy, parentId, DraftLeaveChoice.Discard);
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); ShowDraft(); return; }
            _person = parentId; RestoreSelection(); ShowDraft(); RenderCanvas();
            _status.Text = $"已取消新增{kind}，没有创建人物。";
        }
        catch (Exception ex) { _status.Text = $"取消后无法恢复人物：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task BeginSiblingAsync(Guid siblingId)
    {
        if (BlockWhileInlineEditing() || _busy || _genealogy is not Guid genealogy || _person != siblingId) return;
        if (BlockWhileChartOrderPending()) return;
        if (!_canvasPeople.TryGetValue(siblingId, out var sibling) || !RecordedGenerationParser.TryParse(sibling.RecordedGeneration, out var generation))
        { _status.Text = "当前人物的世代无法确定，请先核实。"; return; }
        var relationLabel = _noParents.Contains(siblingId) ? "同世人物" : "兄妹";
        _busy = true; IsEnabled = false; var started = false;
        try
        {
            await ValidateCanvasIdentityAsync(siblingId);
            using (var operation = _coordinator.EnterOperation())
            await using (var context = _coordinator.CreateContext())
                _ = _chart is Guid chart ? await new WorkbenchPersonProfileCommand(context).BeginOrdinarySiblingForChartAsync(genealogy, siblingId, chart) : await new WorkbenchPersonProfileCommand(context).BeginOrdinarySiblingAsync(genealogy, siblingId);
            var prior = _session.Draft;
            var result = _chart is Guid currentChart ? await _session.BeginSiblingForChartAsync(genealogy, siblingId, currentChart, Choose()) : await _session.BeginOrdinarySiblingAsync(genealogy, siblingId, Choose());
            SyncSavedPerson(prior);
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); ShowDraft(); return; }
            started = true;
            await ValidateCanvasIdentityAsync(siblingId);
            var draft = _session.Draft!;
            HorizontalLayoutNode Project()
            {
                var people = CanvasLayoutPeople().ToList(); var edges = _canvasEdges.ToList();
                var parentEdge = edges.FirstOrDefault(e => e.ChildId == siblingId);
                var order = parentEdge is null ? RootOrder() : NextChildOrder(parentEdge.ParentId);
                people.Add(new HorizontalLayoutPerson(draft.Original.PersonId, generation - 1, order, CanvasNodeWidth, CanvasNodeHeight));
                if (parentEdge is not null) edges.Add(new HorizontalLayoutRelationship(draft.Original.PersonId, parentEdge.ParentId, draft.Original.PersonId));
                return InlineNamePlacement.Project(people, edges, people.Select(p => p.Id).ToHashSet(), draft.Original.PersonId);
            }
            var location = Project();
            _status.Text = $"请输入{relationLabel}姓名：Enter 或单击画布空白保存，Esc 取消。";
            ShowInlineNameEditor(() => { var node = Project(); return new Point(node.X + 20, ProjectedNameTop(node)); }, "", name =>
            {
                draft.Values = draft.Values with { Name = name }; _person = draft.Original.PersonId;
                var parentEdge = _canvasEdges.FirstOrDefault(e => e.ChildId == siblingId);
                if (parentEdge is not null) { _createdChildDisplayParents[_person.Value] = parentEdge.ParentId; _createdChildDisplayOrder[_person.Value] = _nextCreatedChildOrder++; }
                _updating = true; _people.SelectedItem = null; _updating = false;
                ShowDraft(); _status.Text = $"正在保存{relationLabel}。";
            }, async () => { RenderCanvas(); await SaveAsync(); }, async () => await CancelNewRelatedPersonAsync(genealogy, siblingId, draft, relationLabel));
            if (_inlineNameEditor is null) throw new InvalidOperationException("无法显示姓名输入框。");
            RenderCanvas(); _canvas.Width = Math.Max(_canvas.Width, location.X + location.Width + 40);
            _canvas.Height = Math.Max(_canvas.Height, location.Y + location.Height + 60);
        }
        catch (Exception ex)
        {
            if (started && _session.Draft is { } draft) await CancelNewRelatedPersonAsync(genealogy, siblingId, draft, relationLabel);
            _status.Text = $"无法新增{relationLabel}：{ex.Message}";
        }
        finally { IsEnabled = true; _busy = false; _inlineNameEditor?.Input.Focus(); }
    }
    private async Task BeginInlineRootAsync(int generation)
    {
        if (BlockWhileInlineEditing() || _busy || _genealogy is not Guid genealogy || _projection is null) return;
        if (BlockWhileChartOrderPending()) return;
        var previousPerson = _person; var projection = _projection; var revision = _projectionRevision;
        _busy = true; IsEnabled = false; var started = false;
        async Task CancelAsync(WorkbenchPersonDraft draft)
        {
            _busy = true; IsEnabled = false;
            try
            {
                if (!ReferenceEquals(_session.Draft, draft)) return;
                var result = previousPerson is Guid previous
                    ? await _session.SelectAsync(genealogy, previous, DraftLeaveChoice.Discard)
                    : await _session.LeaveAsync(DraftLeaveChoice.Discard);
                if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); ShowDraft(); return; }
                _person = previousPerson; RestoreSelection(); ShowDraft(); RenderCanvas(); _status.Text = "已取消新建人物，没有创建记录。";
            }
            catch (Exception ex) { _status.Text = $"取消后无法恢复人物：{ex.Message}"; }
            finally { IsEnabled = true; _busy = false; }
        }
        try
        {
            await projection.ValidateAsync(revision);
            if (!ReferenceEquals(projection, _projection) || !projection.IsCurrent(revision)) throw new InvalidOperationException("画布已失效，请重新打开。");
            var prior = _session.Draft;
            var result = await _session.BeginNewAsync(genealogy, Choose()); SyncSavedPerson(prior);
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); ShowDraft(); return; }
            started = true;
            projection = _projection ?? throw new InvalidOperationException("画布已失效，请重新打开。"); revision = _projectionRevision;
            await projection.ValidateAsync(revision);
            if (!ReferenceEquals(projection, _projection) || !projection.IsCurrent(revision)) throw new InvalidOperationException("画布已失效，请重新打开。");
            var draft = _session.Draft!;
            if (_chart is Guid chart && _chartRevision is long chartRevision) draft.AttachIndependentChart(chart, chartRevision);
            draft.Values = draft.Values with { RecordedGeneration = generation.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            HorizontalLayoutNode Project()
            {
                var people = CanvasLayoutPeople().ToList();
                people.Add(new HorizontalLayoutPerson(draft.Original.PersonId, generation - 1, RootOrder(), CanvasNodeWidth, CanvasNodeHeight));
                return InlineNamePlacement.Project(people, _canvasEdges, people.Select(p => p.Id).ToHashSet(), draft.Original.PersonId);
            }
            var location = Project();
            _status.Text = $"在第 {generation} 世输入姓名：Enter 或单击画布空白保存，Esc 取消；保存后可在右侧修改资料。";
            ShowInlineNameEditor(() => { var node = Project(); return new Point(node.X + 20, ProjectedNameTop(node)); }, "", name =>
            {
                draft.Values = draft.Values with { Name = name }; _person = draft.Original.PersonId;
                _updating = true; _people.SelectedItem = null; _updating = false; ShowDraft();
                _status.Text = "正在保存人物。";
            }, async () => { RenderCanvas(); await SaveAsync(); }, async () => await CancelAsync(draft));
            if (_inlineNameEditor is null) throw new InvalidOperationException("无法显示姓名输入框。");
            RenderCanvas(); _canvas.Width = Math.Max(_canvas.Width, location.X + location.Width + 40);
            _canvas.Height = Math.Max(_canvas.Height, location.Y + location.Height + 60);
        }
        catch (Exception ex)
        {
            if (started && _session.Draft is { } draft) await CancelAsync(draft);
            _status.Text = $"无法新建人物：{ex.Message}";
        }
        finally { IsEnabled = true; _busy = false; _inlineNameEditor?.Input.Focus(); }
    }
    private async Task BeginNewAsync(Guid? parentId = null, PersonRelationshipKind? kind = null, Guid? partnerId = null, FamilyUnionKind? unionKind = null, Guid? siblingId = null)
    {
        if (BlockWhileInlineEditing()) return;
        if (_busy || _genealogy is not Guid genealogy) return;
        if (BlockWhileChartOrderPending()) return;
        if (_chart is not null && (siblingId is not null || partnerId is not null || (parentId is not null && kind != PersonRelationshipKind.AdoptiveParent)))
        { _status.Text = "该关系的图表顺序同步尚未完成，暂不能从此入口新增。"; return; }
        _busy = true; IsEnabled = false;
        WorkbenchPersonDraft? prior = null;
        var relatedId = siblingId ?? parentId ?? partnerId;
        var started = false;
        var focusName = false;
        try
        {
            if (relatedId is Guid before) await ValidateCanvasIdentityAsync(before);
            prior = _session.Draft;
            var result = _chart is Guid chart && parentId is Guid adoptedParent && kind == PersonRelationshipKind.AdoptiveParent
                ? await _session.BeginAdoptedChildForChartAsync(genealogy, adoptedParent, chart, Choose())
                : await _session.BeginNewAsync(genealogy, Choose(), parentId, kind, partnerId, unionKind, siblingId);
            SyncSavedPerson(prior);
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); ShowDraft(); return; }
            started = true;
            if (relatedId is Guid after) await ValidateCanvasIdentityAsync(after);
            var draft = _session.Draft!;
            if (parentId is null && partnerId is null && siblingId is null && _chart is Guid independentChart && _chartRevision is long independentRevision)
                draft.AttachIndependentChart(independentChart, independentRevision);
            if (parentId is null && partnerId is null && siblingId is null) draft.Values = draft.Values with { RecordedGeneration = "1" };
            _person = draft.Original.PersonId; _updating = true; _people.SelectedItem = null; _updating = false;
            ShowDraft(); RenderCanvas(); _status.Text = siblingId is not null ? "新增兄妹草稿；已按所选人物预填世代，可修改后保存。" : partnerId is not null ? "新增配偶草稿；世代可留空，填写后保存。" :
                parentId is null ? "新建人物草稿；填写后保存。" : "新增子女草稿；世代已按父母预填，可修改后保存。";
            focusName = true;
        }
        catch (Exception ex)
        {
            if (started) { await _session.LeaveAsync(DraftLeaveChoice.Discard); _person = null; }
            if (relatedId is not null) { ClearCanvas(); _canvasMonitor.Stop(); }
            ShowDraft(); RestoreSelection();
            _status.Text = $"{(prior?.IsSaved == true ? "原草稿已保存；" : "")}未新建人物，请重新打开画布：{ex.Message}";
        }
        finally { IsEnabled = true; _busy = false; if (focusName) _name.Focus(); }
    }
    private async Task ValidateCanvasIdentityAsync(Guid id)
    {
        var projection = _projection ?? throw new InvalidOperationException("画布会话未打开。");
        var revision = _projectionRevision;
        await projection.ValidateAsync(revision);
        if (!ReferenceEquals(projection, _projection) || !projection.IsCurrent(revision) || !_canvasPeople.ContainsKey(id))
            throw new InvalidOperationException("画布人物已失效。");
        await ValidateChartRevisionAsync();
    }
    private async Task ValidateChartRevisionAsync()
    {
        if (_chart is not Guid chart || _genealogy is not Guid genealogy) return;
        using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
        var current = await context.LineageCharts.AsNoTracking().Where(c => c.Id == chart && c.GenealogyId == genealogy).Select(c => (long?)c.Revision).SingleOrDefaultAsync();
        if (_chartRevision is null || current != _chartRevision) throw new InvalidOperationException("图表已变化，请重新打开画布。");
    }
    private void SyncSavedPerson(WorkbenchPersonDraft? draft)
    {
        if (draft?.IsSaved != true) return;
        var items = _people.Items.OfType<Option>().ToList();
        var index = items.FindIndex(item => item.Id == draft.Original.PersonId);
        var updated = new Option(draft.Original.PersonId, draft.Values.Name.Trim());
        if (index < 0) items.Add(updated); else items[index] = updated;
        _updating = true; _people.ItemsSource = items; _people.SelectedItem = items.FirstOrDefault(item => item.Id == _person); _updating = false;
    }
    private async Task ReleaseProjectionAsync()
    {
        _canvasMonitor.Stop(); ClearCanvas(); _projectionRevision = 0;
        var old = _projection; _projection = null;
        if (old is not null) await old.DisposeAsync();
    }
    private async Task ResetProjectionAsync(Guid genealogyId)
    {
        await ReleaseProjectionAsync();
        var next = new WorkbenchProjectionSession(_coordinator, genealogyId);
        try { _projectionRevision = await next.ReopenAsync(); _projection = next; _canvasMonitor.Start(); }
        catch { await next.DisposeAsync(); throw; }
    }
    private async Task LoadCanvasAsync(IReadOnlyDictionary<Guid, ProjectionReadSession.PersonRow>? previous = null)
    {
        var projection = _projection ?? throw new InvalidOperationException("画布会话未打开。");
        var revision = _projectionRevision;
        var chartId = _chart;
        long? chartRevision = null;
        if (chartId is Guid selectedChart && _genealogy is Guid selectedGenealogy)
        {
            using var operation = _coordinator.EnterOperation(); await using var chartContext = _coordinator.CreateContext();
            chartRevision = await chartContext.LineageCharts.AsNoTracking().Where(c => c.Id == selectedChart && c.GenealogyId == selectedGenealogy)
                .Select(c => (long?)c.Revision).SingleOrDefaultAsync() ?? throw new InvalidOperationException("图表已不存在或不属于当前族谱。");
            if (_chartRevision is long expected && expected != chartRevision) throw new InvalidOperationException("图表已变化，请重新打开画布。");
        }
        var page = await projection.ReadPeopleAsync(revision, null, 100);
        var rows = page.Page.Items.Where(p => RecordedGenerationParser.TryParse(p.RecordedGeneration, out _)).ToDictionary(p => p.Id);
        var unresolved = page.Page.Items.Count - rows.Count;
        var diagnostics = page.Page.Items.Where(p => !RecordedGenerationParser.TryParse(p.RecordedGeneration, out _))
            .Select(p => $"{p.Name}：谱载世代 {p.RecordedGeneration ?? "未记"}，未作为主节点显示，可在辅助列表查看。").ToList();
        var candidates = new List<HorizontalLayoutRelationship>();
        var partners = new Dictionary<Guid, IReadOnlyList<PartnerOption>>();
        var incompleteUnions = new HashSet<Guid>();
        var siblings = new Dictionary<Guid, IReadOnlyList<ProjectionReadSession.SiblingRow>>();
        var parentCounts = new Dictionary<Guid, int>(); var parentIds = new Dictionary<Guid, HashSet<Guid>>(); var noParents = new HashSet<Guid>();
        foreach (var child in rows.Values)
        {
            var siblingPage = await projection.ReadSiblingsAsync(revision, child.Id, null, 100);
            siblings.Add(child.Id, siblingPage.Page.Items);
            if (siblingPage.Page.NextCursor is not null) { unresolved++; diagnostics.Add($"{child.Name}：兄妹关系未全部加载。"); }
            var unions = await projection.ReadUnionsAsync(revision, child.Id, null, 100);
            var labels = new List<PartnerOption>();
            foreach (var union in unions.Page.Items.OrderBy(u => u.Kind == FamilyUnionKind.Marriage && u.PartnerTwoId is not null ? 0 : 1)
                .ThenBy(u => u.Id.ToString("D"), StringComparer.Ordinal))
            {
                var other = union.PartnerOneId == child.Id ? union.PartnerTwoId : union.PartnerOneId;
                var related = other is Guid otherId ? (await projection.ReadPersonAsync(revision, otherId)).Person : null;
                labels.Add(new PartnerOption(union.Id, other, related?.Name ?? "未详", union.Kind, related?.Sex ?? PersonSex.Unknown));
            }
            if (chartId is Guid spouseChart && _genealogy is Guid spouseGenealogy && labels.Count > 1 && unions.Page.NextCursor is null)
            {
                using var operation = _coordinator.EnterOperation(); await using var orderContext = _coordinator.CreateContext();
                var capture = await new SpouseOrderStorageService(orderContext).CaptureAsync(spouseGenealogy, spouseChart, child.Id);
                if (capture.ChartRevision != chartRevision) throw new InvalidOperationException("图表已变化，请重新读取。");
                if (capture.NeedsReview || capture.GroupId is null || !capture.SavedOrder.ToHashSet().SetEquals(labels.Select(p => p.UnionId)))
                { unresolved++; diagnostics.Add($"{child.Name}：图表配偶顺序未设置或需要复核，暂不显示配偶排序。"); labels.Clear(); incompleteUnions.Add(child.Id); }
                else
                {
                    var byUnion = labels.ToDictionary(p => p.UnionId);
                    labels = capture.SavedOrder.Select(id => byUnion[id]).ToList();
                }
            }
            if (chartId is not null && unions.Page.NextCursor is not null) labels.Clear();
            partners.Add(child.Id, labels.AsReadOnly());
            if (unions.Page.NextCursor is not null) { incompleteUnions.Add(child.Id); unresolved++; diagnostics.Add($"{child.Name}：婚配记录未全部加载。"); }
            var parents = await projection.ReadParentsAsync(revision, child.Id, null, 100);
            parentCounts[child.Id] = parents.Page.NextCursor is not null ? -1 : parents.Page.Items.Count;
            parentIds[child.Id] = parents.Page.Items.Select(p => p.ParentId).ToHashSet();
            if (parents.Page.NextCursor is null && parents.Page.Items.Count == 0) noParents.Add(child.Id);
            var selectedParent = parents.Page.Items.Count == 1 ? parents.Page.Items[0] :
                _createdChildDisplayParents.TryGetValue(child.Id, out var explicitParent)
                    ? parents.Page.Items.SingleOrDefault(p => p.ParentId == explicitParent && p.Kind == PersonRelationshipKind.BiologicalParent)
                    : null;
            if (parents.Page.Items.Count > 1 && chartId is Guid chart)
            {
                var choice = (await projection.ReadChartChoiceAsync(revision, chart, child.Id)).Choice;
                selectedParent = choice?.ApplicableRelationshipId is Guid selected
                    ? parents.Page.Items.SingleOrDefault(p => p.RelationshipId == selected) : null;
            }
            if (parents.Page.NextCursor is not null || (parents.Page.Items.Count > 1 && selectedParent is null))
            {
                unresolved++; diagnostics.Add($"{child.Name}：父母候选存在多项或未全部加载，暂不连线。"); continue;
            }
            if (parents.Page.Items.Count == 0) continue;
            var parent = selectedParent!;
            if (!rows.TryGetValue(parent.ParentId, out var parentRow) || ParseGeneration(child) <= ParseGeneration(parentRow))
            { unresolved++; diagnostics.Add($"{child.Name}：父母 {parent.ParentName} 未显示或世代列冲突，暂不连线。"); continue; }
            candidates.Add(new HorizontalLayoutRelationship(parent.RelationshipId, parent.ParentId, child.Id));
        }
        var checkedSiblingRelations = new HashSet<Guid>();
        foreach (var id in rows.Keys)
        {
            var supported = new List<ProjectionReadSession.SiblingRow>();
            foreach (var link in siblings[id])
            {
                if (rows.ContainsKey(link.PersonId) && parentCounts[id] >= 0 && parentCounts[link.PersonId] >= 0 && parentIds[id].Overlaps(parentIds[link.PersonId]))
                    supported.Add(link);
                else if (checkedSiblingRelations.Add(link.RelationshipId))
                { unresolved++; diagnostics.Add($"{rows[id].Name}与{link.Name}：原始兄妹记录缺少完整共同父母依据，待核实。"); }
            }
            siblings[id] = supported.AsReadOnly();
        }
        var groups = new List<OrderGroup>(); var blockedParents = new HashSet<Guid>();
        foreach (var group in candidates.GroupBy(edge => edge.ParentId))
        {
            var all = await projection.ReadChildrenAsync(revision, group.Key, null, 100);
            var fullGroup = all.Page.NextCursor is null;
            var expectedLinks = new HashSet<Guid>();
            if (fullGroup)
            {
                foreach (var child in all.Page.Items)
                {
                    if (chartId is not Guid chart) { expectedLinks.Add(child.RelationshipId); continue; }
                    var parentPage = await projection.ReadParentsAsync(revision, child.ChildId, null, 100);
                    if (parentPage.Page.NextCursor is not null) { fullGroup = false; break; }
                    if (parentPage.Page.Items.Count <= 1) { expectedLinks.Add(child.RelationshipId); continue; }
                    var choice = (await projection.ReadChartChoiceAsync(revision, chart, child.ChildId)).Choice;
                    if (choice?.ApplicableRelationshipId is not Guid selected) { fullGroup = false; break; }
                    if (parentPage.Page.Items.All(p => p.RelationshipId != selected)) { fullGroup = false; break; }
                    if (selected == child.RelationshipId) expectedLinks.Add(selected);
                }
                fullGroup = fullGroup && group.Select(e => e.Id).ToHashSet().SetEquals(expectedLinks);
            }
            if (!fullGroup)
            {
                var hasCreatedChild = group.Any(e => _createdChildDisplayParents.TryGetValue(e.ChildId, out var selected) && selected == group.Key);
                unresolved += group.Count();
                if (chartId is not null || !hasCreatedChild || all.Page.NextCursor is not null)
                {
                    blockedParents.Add(group.Key);
                    diagnostics.Add($"{rows[group.Key].Name}：子女关系未完整显示或存在歧义，暂不排序和连线。"); continue;
                }
                diagnostics.Add($"{rows[group.Key].Name}：仅绘制已明确展示的子女关系，其他候选仍待复核。");
            }
            if (group.Count() > 1)
            {
                var members = group.Select(e => e.ChildId).ToArray();
                groups.Add(MakeOrderGroup(group.Key, members, group.Select(e => e.Id), rows));
            }
        }
        if (chartId is null)
        {
            foreach (var generation in rows.Values.Where(p => parentCounts[p.Id] == 0).GroupBy(ParseGeneration))
            {
                var members = generation.Select(p => p.Id).ToArray();
                if (members.Length > 1) groups.Add(MakeOrderGroup(null, members, Array.Empty<Guid>(), rows));
            }
        }
        if (chartId is Guid independentChart)
        {
            using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
            var independentGroups = await context.LineageRootSiblingOrders.AsNoTracking().Include(g => g.Items)
                .Where(g => g.ChartId == independentChart).ToListAsync();
            foreach (var stored in independentGroups)
            {
                var members = stored.Items.Select(i => i.PersonId).ToArray();
                if (members.Length > 1 && members.All(id => rows.ContainsKey(id) && parentCounts[id] == 0) &&
                    members.Select(id => ParseGeneration(rows[id])).Distinct().Count() == 1)
                    groups.Add(MakeOrderGroup(null, members, Array.Empty<Guid>(), rows));
            }
            var grouped = groups.Where(g => g.ParentId is null).SelectMany(g => g.People).ToHashSet();
            foreach (var sameGeneration in rows.Values.Where(p => parentCounts[p.Id] == 0 && !grouped.Contains(p.Id)).GroupBy(ParseGeneration))
            {
                var members = sameGeneration.Select(p => p.Id).ToArray();
                if (members.Length > 1 && !groups.Any(g => g.ParentId is null && g.People.Any(id => ParseGeneration(rows[id]) == sameGeneration.Key)))
                    groups.Add(MakeOrderGroup(null, members, Array.Empty<Guid>(), rows));
            }
        }
        var persistedOrders = new Dictionary<Guid, IReadOnlyList<Guid>>();
        var persistedRootOrders = new Dictionary<string, IReadOnlyList<Guid>>();
        if (chartId is Guid currentChart && _genealogy is Guid currentGenealogy)
        {
            await using var storage = await SiblingOrderStorageSession.OpenAsync(_coordinator, currentGenealogy);
            var saved = await storage.ListAsync(currentGenealogy, currentChart);
            foreach (var group in groups.Where(g => g.ParentId is not null))
            {
                var summary = saved.SingleOrDefault(s => s.ParentId == group.ParentId);
                if (summary is null) continue;
                var review = await storage.ReviewAsync(currentGenealogy, currentChart, summary.GroupId);
                if (!review.CanApplyAtCapturedRevision || !review.Historical.Select(i => i.Member.PersonId).ToHashSet().SetEquals(group.People))
                { unresolved++; diagnostics.Add($"{rows[group.ParentId!.Value].Name}：图表子女顺序需要复核，暂不应用。"); continue; }
                await storage.ValidateReviewAsync(review);
                var ids = review.Historical.Select(i => i.Member.PersonId).ToArray();
                try { WorkbenchOrderRules.Validate(ids.Select(id => new WorkbenchOrderRules.Member(id, rows[id].Sex)).ToArray(), true); }
                catch (InvalidOperationException ex) { unresolved++; diagnostics.Add($"{rows[group.ParentId!.Value].Name}：{ex.Message}"); continue; }
                persistedOrders[group.ParentId!.Value] = ids;
            }
            foreach (var group in groups.Where(g => g.ParentId is null))
            {
                using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
                try
                {
                    var capture = await new RootSiblingOrderService(context).CaptureAsync(currentGenealogy, currentChart, group.People[0]);
                    if (capture.Revision != chartRevision) throw new InvalidOperationException("图表已变化，请重新读取。");
                    if (capture.HasSavedOrder && capture.Members.Select(m => m.Id).ToHashSet().SetEquals(group.People))
                        persistedRootOrders[group.Key] = capture.Members.Select(m => m.Id).ToArray();
                }
                catch (InvalidOperationException ex) { unresolved++; diagnostics.Add($"{rows[group.People[0]].Name}：{ex.Message}"); }
            }
        }
        var layoutOrders = new Dictionary<Guid, int>(); var groupByPerson = new Dictionary<Guid, OrderGroup>();
        foreach (var group in groups)
        {
            foreach (var id in group.People) groupByPerson.Add(id, group);
            if (!_sessionOrders.ContainsKey(group.Key) && group.ParentId is null && persistedRootOrders.TryGetValue(group.Key, out var rootOrder))
            {
                for (var i = 0; i < rootOrder.Count; i++) layoutOrders[rootOrder[i]] = i;
                continue;
            }
            if (!_sessionOrders.ContainsKey(group.Key) && group.ParentId is Guid persistedParent && persistedOrders.TryGetValue(persistedParent, out var persisted))
            {
                for (var i = 0; i < persisted.Count; i++) layoutOrders[persisted[i]] = i;
                continue;
            }
            if (!_sessionOrders.TryGetValue(group.Key, out var order) || !IsComplete(group, order))
            {
                if (chartId is null && group.ParentId is Guid explicitParent && group.People.Any(id => _createdChildDisplayParents.TryGetValue(id, out var selected) && selected == explicitParent))
                {
                    var temporary = group.People.OrderBy(id => _createdChildDisplayOrder.ContainsKey(id) ? 1 : 0)
                        .ThenBy(id => _createdChildDisplayOrder.GetValueOrDefault(id, 0)).ThenBy(id => id).ToArray();
                    for (var i = 0; i < temporary.Length; i++) layoutOrders[temporary[i]] = i;
                    unresolved++; diagnostics.Add($"{rows[explicitParent].Name}：子女连线按临时显示顺序排列，不代表谱载排行；可在右侧明确顺序。");
                    continue;
                }
                if (group.ParentId is Guid parent) blockedParents.Add(parent);
                unresolved++; diagnostics.Add(group.ParentId is Guid p
                    ? $"{rows[p].Name}：多名子女的顺序尚未确认，请分别指定本次显示顺序。"
                    : $"{rows[group.People[0]].Name} 等 {group.People.Count} 人：请分别指定本次显示顺序。"); continue;
            }
            foreach (var (id, rank) in order) layoutOrders[id] = rank - 1;
        }
        var edges = candidates.Where(edge => !blockedParents.Contains(edge.ParentId)).ToArray();
        await projection.ValidateAsync(revision);
        if (!ReferenceEquals(projection, _projection) || !projection.IsCurrent(revision)) throw new InvalidOperationException("画布读取结果已失效。");
        if (_chart != chartId) throw new InvalidOperationException("图表选择已变化，请重新读取。");
        if (chartId is Guid finalChart && _genealogy is Guid finalGenealogy)
        {
            using var operation = _coordinator.EnterOperation(); await using var check = _coordinator.CreateContext();
            var actual = await check.LineageCharts.AsNoTracking().Where(c => c.Id == finalChart && c.GenealogyId == finalGenealogy).Select(c => (long?)c.Revision).SingleOrDefaultAsync();
            if (actual != chartRevision) throw new InvalidOperationException("图表已变化，请重新读取。");
        }
        if (previous is not null)
        {
            foreach (var old in previous.Values)
            {
                if (!rows.TryGetValue(old.Id, out var current)) throw new InvalidOperationException("画布人物已变化，请重新加载。");
                var isEdited = old.Id == _person;
                var expectedName = isEdited ? _name.Text.Trim() : old.Name;
                var expectedGeneration = isEdited && _generationEdited ? _generation.Text.Trim() : old.RecordedGeneration;
                if (current.Name != expectedName || current.RecordedGeneration != expectedGeneration || current.RecordedBirthOrder != old.RecordedBirthOrder)
                    throw new InvalidOperationException("画布人物资料已变化，请重新加载。");
            }
        }
        _canvasPeople.Clear(); foreach (var row in rows.Values) _canvasPeople.Add(row.Id, row);
        _canvasPartners.Clear(); foreach (var pair in partners) _canvasPartners.Add(pair.Key, pair.Value);
        _incompleteUnions.Clear(); _incompleteUnions.UnionWith(incompleteUnions);
        _canvasSiblings.Clear(); foreach (var pair in siblings) _canvasSiblings.Add(pair.Key, pair.Value);
        _noParents.Clear(); _noParents.UnionWith(noParents);
        _orderGroups.Clear(); foreach (var pair in groupByPerson) _orderGroups.Add(pair.Key, pair.Value);
        _layoutOrders.Clear(); foreach (var pair in layoutOrders) _layoutOrders.Add(pair.Key, pair.Value);
        if (chartId is null) foreach (var key in _sessionOrders.Keys.Except(groups.Select(g => g.Key)).ToArray()) _sessionOrders.Remove(key);
        _canvasEdges.Clear(); _canvasEdges.AddRange(edges); _unresolvedCount = unresolved; _canvasTruncated = page.Page.NextCursor is not null;
        _chartRevision = chartRevision;
        _diagnosticMessages.Clear(); _diagnosticMessages.AddRange(diagnostics); _diagnostics.ItemsSource = _diagnosticMessages.ToArray();
        _canvasValid = true;
        RenderCanvas();
    }
    private static OrderGroup MakeOrderGroup(Guid? parent, IEnumerable<Guid> people, IEnumerable<Guid> relations, IReadOnlyDictionary<Guid, ProjectionReadSession.PersonRow> rows)
    {
        var members = people.OrderBy(id => id).ToArray();
        var key = System.Text.Json.JsonSerializer.Serialize(new { Parent = parent, Relations = relations.OrderBy(id => id).ToArray(),
            People = members.Select(id => new { Id = id, rows[id].RecordedGeneration, rows[id].RecordedBirthOrder }).ToArray() });
        return new OrderGroup(key, members, parent);
    }
    private static bool IsComplete(OrderGroup group, IReadOnlyDictionary<Guid, int> order) =>
        order.Count == group.People.Count && group.People.All(order.ContainsKey) && order.Values.Order().SequenceEqual(Enumerable.Range(1, group.People.Count));
    private bool CanMove(Guid person, int delta)
    {
        if (!_orderGroups.TryGetValue(person, out var group)) return false;
        var order = CurrentCompleteOrder(group);
        if (order is null || !order.TryGetValue(person, out var rank) || rank + delta < 1 || rank + delta > group.People.Count) return false;
        var other = order.Single(pair => pair.Value == rank + delta).Key;
        return _chart is null || (_canvasPeople[person].Sex == _canvasPeople[other].Sex && _canvasPeople[person].Sex is PersonSex.Male or PersonSex.Female);
    }
    private Dictionary<Guid, int>? CurrentCompleteOrder(OrderGroup group)
    {
        if (_sessionOrders.TryGetValue(group.Key, out var draft)) return IsComplete(group, draft) ? new(draft) : null;
        if (_chart is not null && group.People.All(_layoutOrders.ContainsKey))
            return group.People.ToDictionary(id => id, id => _layoutOrders[id] + 1);
        return null;
    }
    private async Task SetDisplayRankAsync()
    {
        if (BlockWhileInlineEditing()) return;
        if (_busy || _person is not Guid person || !_orderGroups.TryGetValue(person, out var group)) return;
        if (!int.TryParse(_displayRank.Text, out var rank) || rank < 1 || rank > group.People.Count)
        { _status.Text = $"请输入 1 至 {group.People.Count} 的显示位置。"; return; }
        var order = _sessionOrders.TryGetValue(group.Key, out var prior) ? new Dictionary<Guid, int>(prior) : new Dictionary<Guid, int>();
        if (order.Any(pair => pair.Key != person && pair.Value == rank))
        { _status.Text = "该位置已指定给另一人，请输入未使用的位置，或在全部指定后使用上移、下移。"; return; }
        order[person] = rank;
        await ApplyOrderAsync(person, group, order);
    }
    private async Task MoveSiblingAsync(int delta)
    {
        if (BlockWhileInlineEditing()) return;
        if (_busy || _person is not Guid person || !CanMove(person, delta) || !_orderGroups.TryGetValue(person, out var group)) return;
        var order = CurrentCompleteOrder(group)!; var rank = order[person];
        var other = order.Single(pair => pair.Value == rank + delta).Key; order[person] = rank + delta; order[other] = rank;
        await ApplyOrderAsync(person, group, order);
    }
    private async Task MoveSiblingToAsync(Guid source, Guid target)
    {
        if (BlockWhileInlineEditing() || _busy || _chart is null || _person != source ||
            !_orderGroups.TryGetValue(source, out var group) || !_orderGroups.TryGetValue(target, out var targetGroup) || group.Key != targetGroup.Key) return;
        var order = CurrentCompleteOrder(group);
        if (order is null) { _status.Text = "请先完整指定同胞顺序，再拖动调整。"; return; }
        try
        {
            var members = order.OrderBy(p => p.Value).Select(p => new WorkbenchOrderRules.Member(p.Key, _canvasPeople[p.Key].Sex)).ToArray();
            var moved = WorkbenchOrderRules.Move(members, source, order[target] - 1, true);
            await ApplyOrderAsync(source, group, moved.Select((m, i) => (m.Id, Rank: i + 1)).ToDictionary(x => x.Id, x => x.Rank));
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
    }
    private async Task MoveSpouseToAsync(Guid owner, Guid source, Guid target)
    {
        if (BlockWhileInlineEditing() || _busy || _person != owner || _chart is not Guid chart || _genealogy is not Guid genealogy ||
            !_canvasPartners.TryGetValue(owner, out var partners) || _incompleteUnions.Contains(owner)) return;
        var order = partners.Select(p => p.UnionId).ToList(); var from = order.IndexOf(source); var to = order.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        order.RemoveAt(from); order.Insert(to, source);
        _busy = true; IsEnabled = false; var committed = false;
        try
        {
            await ValidateCanvasIdentityAsync(owner);
            using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
            var storage = new SpouseOrderStorageService(context); var capture = await storage.CaptureAsync(genealogy, chart, owner);
            if (capture.ChartRevision != _chartRevision || capture.NeedsReview || !capture.SavedOrder.SequenceEqual(partners.Select(p => p.UnionId)))
                throw new InvalidOperationException("图表或配偶顺序已变化，请重新读取并复核。");
            await storage.SaveAsync(capture, order); committed = true; _chartRevision = capture.ChartRevision + 1;
            await ResetProjectionAsync(genealogy); await LoadCanvasAsync(); ShowDraft(); _status.Text = "已保存当前图表的配偶顺序。";
        }
        catch (Exception ex) { _status.Text = committed ? $"配偶顺序已保存，但画布刷新失败：{ex.Message}" : $"配偶顺序未保存：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task ApplyOrderAsync(Guid person, OrderGroup group, Dictionary<Guid, int> order)
    {
        if (_chart is not null && IsComplete(group, order))
        {
            try { WorkbenchOrderRules.Validate(order.OrderBy(p => p.Value).Select(p => new WorkbenchOrderRules.Member(p.Key, _canvasPeople[p.Key].Sex)).ToArray(), true); }
            catch (InvalidOperationException ex) { _status.Text = ex.Message; return; }
        }
        _busy = true; IsEnabled = false;
        try
        {
            await ValidateCanvasIdentityAsync(person);
            _sessionOrders[group.Key] = order;
            await LoadCanvasAsync(); ShowDraft();
            _status.Text = IsComplete(group, order) ? (_chart is null ? "已调整本次显示顺序，未修改谱载排行。" : "已调整顺序草稿，请保存当前图表顺序。") : $"已指定 {order.Count}/{group.People.Count} 人；请继续指定其余人物。";
        }
        catch (Exception ex) { ClearCanvas(); _canvasMonitor.Stop(); ShowDraft(); _status.Text = $"画布已失效，草稿保留：{ex.Message}"; }
        finally { _busy = false; IsEnabled = true; }
    }
    private async Task SaveChartOrderAsync()
    {
        if (BlockWhileInlineEditing() || _busy || _chart is not Guid chart || _genealogy is not Guid genealogy) return;
        var group = _person is Guid selected && _orderGroups.TryGetValue(selected, out var selectedGroup) && _sessionOrders.ContainsKey(selectedGroup.Key)
            ? selectedGroup : _orderGroups.Values.FirstOrDefault(g => _sessionOrders.ContainsKey(g.Key));
        if (group is null) { _status.Text = "排序草稿对应的组已变化；请放弃该草稿后重新调整。"; return; }
        var person = group.People[0];
        if (!_sessionOrders.TryGetValue(group.Key, out var order) || !IsComplete(group, order))
        { _status.Text = "请先完整调整当前同胞组的顺序。"; return; }
        _busy = true; IsEnabled = false;
        var committed = false;
        try
        {
            await ValidateCanvasIdentityAsync(person);
            var ordered = order.OrderBy(p => p.Value).Select(p => p.Key).ToArray();
            if (group.ParentId is Guid parent)
            {
                await using var storage = await SiblingOrderStorageSession.OpenAsync(_coordinator, genealogy);
                var summaries = await storage.ListAsync(genealogy, chart);
                var existing = summaries.SingleOrDefault(s => s.ParentId == parent);
                var capture = await storage.CaptureSelectedAsync(genealogy, chart, parent, existing?.GroupId);
                if (capture.ChartRevision != _chartRevision) throw new InvalidOperationException("图表已变化，请重新读取。");
                await storage.SaveGroupedAsync(capture, ordered);
            }
            else
            {
                using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
                var storage = new RootSiblingOrderService(context); var capture = await storage.CaptureAsync(genealogy, chart, person);
                if (capture.Revision != _chartRevision || !capture.Members.Select(m => m.Id).ToHashSet().SetEquals(group.People)) throw new InvalidOperationException("图表或兄妹组已变化，请重新读取。");
                await storage.SaveAsync(capture, ordered);
            }
            committed = true; _sessionOrders.Remove(group.Key); _chartRevision = checked(_chartRevision!.Value + 1);
            await ResetProjectionAsync(genealogy); await LoadCanvasAsync(); ShowDraft(); _status.Text = "已保存当前图表的同胞顺序。";
        }
        catch (Exception ex) { _status.Text = committed ? $"顺序已保存，但画布刷新失败：{ex.Message}" : $"顺序未保存，草稿保留：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task ReopenCanvasAsync()
    {
        if (BlockWhileInlineEditing()) return;
        if (_busy || _genealogy is not Guid genealogy) return;
        _busy = true; IsEnabled = false;
        try { _chartRevision = null; await ResetProjectionAsync(genealogy); await LoadCanvasAsync(); _status.Text = CanvasStatus(); }
        catch (Exception ex) { _status.Text = $"画布无法重新打开：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task SelectCanvasPersonAsync(Guid id)
    {
        if (BlockWhileInlineEditing()) return;
        if (_busy || !CanvasContainsIdentity(id) || _projection is null) return;
        var anchor = CaptureCanvasAnchor(id);
        var projection = _projection; var revision = _projectionRevision;
        _busy = true; IsEnabled = false;
        try
        {
            await projection.ValidateAsync(revision);
            await ValidateChartRevisionAsync();
            if (!ReferenceEquals(projection, _projection) || !projection.IsCurrent(revision) || !CanvasContainsIdentity(id)) return;
            var target = _people.Items.OfType<Option>().FirstOrDefault(item => item.Id == id);
            if (target is null) { ClearCanvas(); _status.Text = "人物列表已变化，请重新选择谱系。"; return; }
            _updating = true; _people.SelectedItem = target; _updating = false;
            _busy = false;
            await ChangePersonAsync();
            if (_person == id) RestoreCanvasAnchor(anchor);
        }
        catch (Exception ex) { ClearCanvas(); _status.Text = $"画布已失效，草稿保留：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private (Guid Id, Point Position, double Horizontal, double Vertical)? CaptureCanvasAnchor(Guid id)
    {
        var card = _canvas.Children.OfType<Border>().FirstOrDefault(item => item.Tag is Guid value && value == id);
        return card is null ? null : (id, new Point(Canvas.GetLeft(card), Canvas.GetTop(card)), _canvasScroll.HorizontalOffset, _canvasScroll.VerticalOffset);
    }
    private void RestoreCanvasAnchor((Guid Id, Point Position, double Horizontal, double Vertical)? anchor)
    {
        if (anchor is not { } before) return;
        var card = _canvas.Children.OfType<Border>().FirstOrDefault(item => item.Tag is Guid value && value == before.Id);
        if (card is null) return;
        _canvasScroll.UpdateLayout();
        _canvasScroll.ScrollToHorizontalOffset(before.Horizontal + (Canvas.GetLeft(card) - before.Position.X) * _canvasScale.ScaleX);
        _canvasScroll.ScrollToVerticalOffset(before.Vertical + (Canvas.GetTop(card) - before.Position.Y) * _canvasScale.ScaleY);
    }
    private bool CanvasContainsIdentity(Guid id) => _canvasPeople.ContainsKey(id) || _canvasPartners.Values.Any(items => items.Any(item => item.Id == id));
    private async Task PollCanvasAsync()
    {
        if (_busy || _canvasPolling || (_canvasPeople.Count == 0 && _inlineNameEditor is null && !IsInlineRootDraft()) || _projection is null) return;
        _canvasPolling = true;
        var projection = _projection; var revision = _projectionRevision;
        try { await projection.ValidateAsync(revision); await ValidateChartRevisionAsync(); }
        catch (Exception ex)
        {
            if (ReferenceEquals(projection, _projection) && revision == _projectionRevision)
            { ClearCanvas(); _canvasMonitor.Stop(); _status.Text = _inlineNameEditor is null
                    ? $"数据已变化，画布已清空；草稿保留。请重新打开画布：{ex.Message}"
                    : $"数据已变化，画布已清空；尚未确认的姓名输入保留但不能应用，请复制内容或按 Esc 取消：{ex.Message}"; }
        }
        finally { _canvasPolling = false; }
    }
    private async Task RefreshAfterCommitAsync(Guid genealogyId)
    {
        var previous = _canvasPeople.ToDictionary(pair => pair.Key, pair => pair.Value);
        var committed = _session.CommittedForRefresh;
        var committedValues = _session.CommittedValuesForRefresh;
        if (committed is { IsNew: false } && committedValues is not null &&
            RecordedGenerationParser.TryParse(committed.Values.RecordedGeneration, out var oldGeneration) &&
            RecordedGenerationParser.TryParse(committedValues.RecordedGeneration, out var newGeneration) && oldGeneration != newGeneration)
        {
            var seen = new HashSet<Guid> { committed.PersonId }; var queue = new Queue<Guid>(); queue.Enqueue(committed.PersonId);
            while (queue.TryDequeue(out var parent)) foreach (var edge in _canvasEdges.Where(e => e.ParentId == parent))
                if (seen.Add(edge.ChildId))
                {
                    queue.Enqueue(edge.ChildId);
                    if (previous.TryGetValue(edge.ChildId, out var child) && RecordedGenerationParser.TryParse(child.RecordedGeneration, out var generation))
                        previous[edge.ChildId] = child with { RecordedGeneration = (generation + newGeneration - oldGeneration).ToString(System.Globalization.CultureInfo.InvariantCulture) };
                }
        }
        var card = _canvas.Children.OfType<Border>().FirstOrDefault(b => b.Tag is Guid id && id == _person);
        var anchor = card is null ? (Point?)null : new Point(Canvas.GetLeft(card), Canvas.GetTop(card));
        var horizontalOffset = _canvasScroll.HorizontalOffset; var verticalOffset = _canvasScroll.VerticalOffset;
        try
        {
            if (_afterCommit is not null) await _afterCommit(genealogyId);
            if (_projection is null || _genealogy != genealogyId) return;
            var projection = _projection; var revision = await projection.ReopenAsync();
            _projectionRevision = revision;
            if (_chart is Guid chart)
            {
                var expected = committed?.ChartId == chart && committed.ChartRevision == _chartRevision
                    ? checked(_chartRevision!.Value + 1) : _chartRevision;
                using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
                var actual = await context.LineageCharts.AsNoTracking().Where(c => c.Id == chart && c.GenealogyId == genealogyId)
                    .Select(c => (long?)c.Revision).SingleOrDefaultAsync();
                if (expected is null || actual != expected) throw new InvalidOperationException("图表已变化，请重新打开画布。");
                _chartRevision = actual;
            }
            await LoadCanvasAsync(previous);
            var refreshed = _canvas.Children.OfType<Border>().FirstOrDefault(b => b.Tag is Guid id && id == _person);
            if (anchor is Point before && refreshed is not null)
            {
                _canvasScroll.UpdateLayout();
                _canvasScroll.ScrollToHorizontalOffset(horizontalOffset + (Canvas.GetLeft(refreshed) - before.X) * _canvasScale.ScaleX);
                _canvasScroll.ScrollToVerticalOffset(verticalOffset + (Canvas.GetTop(refreshed) - before.Y) * _canvasScale.ScaleY);
            }
            _canvasMonitor.Start();
        }
        catch { ClearCanvas(); _canvasMonitor.Stop(); throw; }
    }
    private string CanvasStatus() => $"画布显示 {_canvasPeople.Count} 人；{_unresolvedCount} 项世代或关系待处理。" +
        (_canvasTruncated ? " 仅显示前 100 人，其他人物可在辅助列表查看。" : "") +
        (_diagnosticMessages.Any(message => message.Contains("临时显示顺序", StringComparison.Ordinal)) ? " 子女按临时显示顺序排列，不代表谱载排行。" : "");
    private DraftLeaveChoice Choose() => _session.Draft?.IsDirty != true ? DraftLeaveChoice.Discard :
        ChooseForTest?.Invoke() ?? MessageBox.Show(this, "人物档案有未保存修改。是否保存？\n选择“否”放弃，选择“取消”继续编辑。", "未保存的档案", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        { MessageBoxResult.Yes => DraftLeaveChoice.Save, MessageBoxResult.No => DraftLeaveChoice.Discard, _ => DraftLeaveChoice.Cancel };
    private void UpdateDraft()
    {
        if (_updating || _session.Draft is not { } draft || _sex.SelectedItem is not PersonSex sex) return;
        var recorded = _generationEdited ? (string.IsNullOrWhiteSpace(_generation.Text) ? null : _generation.Text.Trim()) : draft.Values.RecordedGeneration;
        draft.Values = new(_name.Text, sex,
            _birthEdited ? _birth.Text : draft.Values.BirthDate,
            _deathEdited ? _death.Text : draft.Values.DeathDate,
            _biographyEdited ? _biography.Text : draft.Values.Biography, recorded);
        if (_inlineNameEditor is null && _person == draft.Original.PersonId)
        {
            foreach (var card in _canvas.Children.OfType<Border>().Where(b => b.Tag is Guid id && id == _person && b.Child is TextBlock))
            {
                ((TextBlock)card.Child).Text = $"{SexIcon(sex)} {_name.Text}";
                card.ToolTip = _name.Text;
            }
        }
    }
    private void ShowDraft()
    {
        var unionOwner = _session.Draft?.Original.PersonId;
        var selectedUnion = unionOwner is not null && unionOwner == _unionOwner ? (_unionList.SelectedItem as UnionOption)?.Id : null;
        _updating = true;
        try
        {
            var v = _session.Draft?.Values;
            _name.Text = v?.Name ?? ""; _sex.SelectedItem = v?.Sex; _birth.Text = v?.BirthDate ?? "";
            _death.Text = v?.DeathDate ?? ""; _biography.Text = v?.Biography ?? "";
            _generation.Text = RecordedGenerationParser.TryParse(v?.RecordedGeneration, out var generation) ? generation.ToString() : "";
            _generationEdited = _birthEdited = _deathEdited = _biographyEdited = false;
            _recordedGeneration.Text = $"谱载原文：{_session.Draft?.Original.Values.RecordedGeneration ?? "未记"}";
            _draftParents.Text = "";
            if (_session.Draft?.Original is { IsOrdinaryChild: true, ParentId: Guid parentId } child)
            {
                var parentName = _canvasPeople.TryGetValue(parentId, out var parent) ? parent.Name : "当前人物";
                var otherName = child.SecondParentId is Guid other && _canvasPartners.TryGetValue(parentId, out var partners)
                    ? partners.FirstOrDefault(p => p.Id == other)?.Name ?? "已选配偶" : "未确定";
                _draftParents.Text = child.ParentSex == PersonSex.Male
                    ? $"生父：{parentName}　生母：{otherName}（草稿）"
                    : $"生母：{parentName}　生父：{otherName}（草稿）";
            }
            else if (_session.Draft?.Original.Parents is { } recordedParents)
            {
                var biological = recordedParents.Where(p => p.Kind == PersonRelationshipKind.BiologicalParent).ToArray();
                string Names(PersonSex sex) => biological.Where(p => p.Sex == sex).Select(p => p.Name).DefaultIfEmpty("未确定").Aggregate((a, b) => $"{a}、{b}");
                var unresolved = biological.Where(p => p.Sex is not (PersonSex.Male or PersonSex.Female)).Select(p => p.Name).ToArray();
                _draftParents.Text = $"生父：{Names(PersonSex.Male)}　生母：{Names(PersonSex.Female)}";
                if (unresolved.Length != 0) _draftParents.Text += $"\n生物父母角色未确定：{string.Join("、", unresolved)}";
                var special = recordedParents.Where(p => p.Kind != PersonRelationshipKind.BiologicalParent).Select(p =>
                    $"{(p.Kind == PersonRelationshipKind.AdoptiveParent ? "养父母" : "继嗣父母")}：{p.Name}").ToArray();
                if (special.Length != 0) _draftParents.Text += "\n" + string.Join("　", special);
            }
            _displayRank.Text = ""; _displayRank.IsEnabled = _applyRank.IsEnabled = false;
            if (_person is Guid person && _orderGroups.TryGetValue(person, out var group))
            {
                _displayRank.IsEnabled = _applyRank.IsEnabled = true;
                var order = CurrentCompleteOrder(group) ?? (_sessionOrders.TryGetValue(group.Key, out var partial) ? partial : null);
                if (order is not null && order.TryGetValue(person, out var rank)) _displayRank.Text = rank.ToString();
            }
            _saveChartOrder.IsEnabled = _chart is not null && _sessionOrders.Count > 0;
            _discardChartOrders.IsEnabled = _chart is not null && _sessionOrders.Count > 0;
            _name.IsEnabled = _generation.IsEnabled = _sex.IsEnabled = _birth.IsEnabled = _death.IsEnabled = _biography.IsEnabled = v is not null;
            var unions = _session.Draft?.Original.Unions ?? [];
            var unionOptions = unions.Select(u => new UnionOption(u.Id, u.PartnerId, $"{u.PartnerName}（{u.Values.Kind switch { FamilyUnionKind.Marriage => "婚配", FamilyUnionKind.Partnership => "伴侣", _ => "类型未详" }}）")).ToArray();
            if (_chart is not null && unionOwner is Guid owner && _canvasPartners.TryGetValue(owner, out var chartPartners) && chartPartners.Count == unionOptions.Length)
            {
                var byId = unionOptions.ToDictionary(u => u.Id);
                if (chartPartners.All(p => byId.ContainsKey(p.UnionId))) unionOptions = chartPartners.Select(p => byId[p.UnionId]).ToArray();
            }
            _unionList.ItemsSource = unionOptions;
            _unionOwner = unionOwner;
            _unionList.SelectedItem = _unionList.Items.OfType<UnionOption>().FirstOrDefault(u => u.Id == selectedUnion);
            if (_unionList.SelectedItem is null && unions.Count > 0) _unionList.SelectedIndex = 0;
            _unionAdd.IsEnabled = _person is Guid current && _canvasPeople.ContainsKey(current) && _session.Draft?.Original.IsNew == false && !_incompleteUnions.Contains(current);
        }
        finally { _updating = false; }
        ShowSelectedUnion();
        ShowChildren();
    }
    private void ShowChildren()
    {
        _childrenPanel.Children.Clear();
        if (_session.Draft is not { } draft || draft.Original.IsNew) { _childrenPanel.Children.Add(new TextBlock { Text = "保存人物后可查看子女。", TextWrapping = TextWrapping.Wrap }); return; }
        var children = draft.Original.Children ?? [];
        if (children.Count == 0) { _childrenPanel.Children.Add(new TextBlock { Text = "暂无已记录的生物子女。" }); return; }
        var currentSex = draft.Original.Values.Sex;
        var otherSex = currentSex == PersonSex.Male ? PersonSex.Female : PersonSex.Male;
        var label = currentSex == PersonSex.Male ? "生母" : currentSex == PersonSex.Female ? "生父" : "另一位生父母";
        _childrenPanel.Children.Add(new TextBlock { Text = $"选择每位子女的{label}，修改后请保存档案。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray });
        foreach (var child in children)
        {
            var row = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
            row.Children.Add(new TextBlock { Text = child.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var options = new List<ChildParentOption> { new(null, "未确定") };
            options.AddRange((draft.Original.Unions ?? []).Where(u => u.Values.Kind == FamilyUnionKind.Marriage && u.PartnerId is not null && u.PartnerSex == otherSex)
                .GroupBy(u => u.PartnerId).Select(g => g.First()).Select(u => new ChildParentOption(u.PartnerId, u.PartnerName)));
            if (child.OtherParentId is Guid old && options.All(o => o.Id != old)) options.Add(new(old, $"{child.OtherParentName}（已有记录）"));
            var selected = draft.ChildOtherParent(child.PersonId);
            var box = new ComboBox { Tag = child.PersonId, DisplayMemberPath = nameof(ChildParentOption.Label), ItemsSource = options, IsEnabled = child.BlockReason is null };
            box.SelectedItem = options.FirstOrDefault(o => o.Id == selected);
            box.SelectionChanged += (_, _) =>
            {
                if (_updating || !ReferenceEquals(_session.Draft, draft) || box.SelectedItem is not ChildParentOption choice) return;
                try { draft.SetChildOtherParent(child.PersonId, choice.Id); _status.Text = "子女父母归属已修改，请显式保存。"; }
                catch (Exception ex) { _status.Text = $"无法更正：{ex.Message}"; }
            };
            row.Children.Add(box);
            if (child.BlockReason is not null) row.Children.Add(new TextBlock { Text = child.BlockReason, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange });
            _childrenPanel.Children.Add(row);
        }
    }
    private void ShowSelectedUnion()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var selected = _unionList.SelectedItem as UnionOption;
            var values = selected is not null && _session.Draft is { } draft ? draft.UnionValues(selected.Id) : null;
            _unionKind.SelectedItem = values?.Kind; _unionStart.Text = values?.StartDateText ?? ""; _unionEnd.Text = values?.EndDateText ?? ""; _unionNote.Text = values?.Note ?? "";
            _unionKind.IsEnabled = _unionStart.IsEnabled = _unionEnd.IsEnabled = _unionNote.IsEnabled = values is not null;
            _unionLocate.IsEnabled = selected?.PartnerId is Guid id && CanvasContainsIdentity(id);
        }
        finally { _updating = false; }
    }
    private void UpdateUnionDraft()
    {
        if (_updating || _session.Draft is not { } draft || _unionList.SelectedItem is not UnionOption selected || _unionKind.SelectedItem is not FamilyUnionKind kind) return;
        static string? TextOrNull(string text) => string.IsNullOrWhiteSpace(text) ? null : text;
        draft.SetUnionValues(selected.Id, new(kind, TextOrNull(_unionStart.Text), TextOrNull(_unionEnd.Text), TextOrNull(_unionNote.Text)));
    }
    private async Task LoadGenealogiesAsync()
    {
        try
        {
            using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
            var list = await context.Genealogies.AsNoTracking().OrderBy(g => g.Name).Select(g => new Option(g.Id, g.Name)).ToListAsync();
            _updating = true; _genealogies.ItemsSource = list; _updating = false;
            _status.Text = "请选择谱系和人物。";
        }
        catch (Exception ex) { _status.Text = $"读取谱系失败：{ex.Message}"; }
    }
    private async Task LoadChartOptionsAsync(Guid genealogy)
    {
        using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
        var charts = await context.LineageCharts.AsNoTracking().Where(c => c.GenealogyId == genealogy).OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new Option(c.Id, c.Name)).ToListAsync();
        charts.Insert(0, new Option(Guid.Empty, "不使用图表"));
        _updating = true; _charts.ItemsSource = charts; _charts.SelectedItem = charts.FirstOrDefault(c => c.Id == (_chart ?? Guid.Empty)); _updating = false;
    }
    private async Task CreateChartAsync()
    {
        if (BlockWhileInlineEditing() || _busy || _genealogy is not Guid genealogy) return;
        var name = _newChartName.Text.Trim();
        if (name.Length is 0 or > 200) { _status.Text = "请输入 1 至 200 字的图表名称。"; return; }
        _busy = true; IsEnabled = false;
        try
        {
            using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
            var graph = await new GenealogyDataService(context).GetGraphSnapshotAsync(genealogy);
            await new LineageChartService(context).SaveAsync(genealogy, null, name, null, graph, new Dictionary<Guid, Guid>());
            await LoadChartOptionsAsync(genealogy); _newChartName.Text = ""; _status.Text = "图表已创建，请在左侧选择。";
        }
        catch (Exception ex) { _status.Text = $"创建图表失败：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task ChangeChartAsync()
    {
        if (_updating || _busy || _genealogy is not Guid genealogy || _charts.SelectedItem is not Option next) return;
        var nextId = next.Id == Guid.Empty ? (Guid?)null : next.Id;
        if (nextId == _chart) return;
        if (BlockWhileInlineEditing() || _sessionOrders.Count > 0)
        {
            if (_sessionOrders.Count > 0) _status.Text = "当前有本次会话的显示顺序，请先处理后再切换图表。";
            RestoreChartSelection(); return;
        }
        _busy = true; IsEnabled = false;
        try
        {
            var prior = _session.Draft; var result = await _session.LeaveAsync(Choose()); SyncSavedPerson(prior);
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreChartSelection(); ShowDraft(); return; }
            _person = null; _chart = nextId; _chartRevision = null; RestoreSelection(); ShowDraft();
            await LoadCanvasAsync(); _status.Text = CanvasStatus();
        }
        catch (Exception ex) { _chart = null; _chartRevision = null; ClearCanvas(); RestoreChartSelection(); ShowDraft(); _status.Text = $"切换图表失败：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private void RestoreChartSelection()
    {
        _updating = true; _charts.SelectedItem = _charts.Items.OfType<Option>().FirstOrDefault(c => c.Id == (_chart ?? Guid.Empty)); _updating = false;
    }
    private async Task ChangeGenealogyAsync()
    {
        if (!_updating && BlockWhileInlineEditing()) { RestoreSelection(); return; }
        if (_updating || _busy || _genealogies.SelectedItem is not Option next || next.Id == _genealogy) return;
        if (_chart is not null && _sessionOrders.Count > 0) { _status.Text = "请先保存或放弃图表顺序草稿，再切换谱系。"; RestoreSelection(); return; }
        _busy = true; IsEnabled = false;
        var dirty = _session.Draft?.IsDirty == true;
        var choice = Choose(); var saved = false;
        try
        {
            var result = await _session.LeaveAsync(choice);
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); return; }
            saved = dirty && choice == DraftLeaveChoice.Save;
            _person = null; ShowDraft();
            using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
            var people = await context.People.AsNoTracking().Where(p => p.GenealogyId == next.Id).OrderBy(p => p.Name)
                .Select(p => new Option(p.Id, p.Name)).ToListAsync();
            await ResetProjectionAsync(next.Id);
            _genealogy = next.Id; _person = null; _chart = null; _chartRevision = null; _updating = true; _people.ItemsSource = people; _people.SelectedItem = null; _updating = false;
            await LoadChartOptionsAsync(next.Id);
            await LoadCanvasAsync(); ShowDraft(); _status.Text = CanvasStatus();
        }
        catch (Exception ex) { _status.Text = saved ? $"档案已保存，但切换谱系失败：{ex.Message}" : $"切换谱系失败：{ex.Message}"; RestoreSelection(); }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task ChangePersonAsync()
    {
        if (!_updating && BlockWhileInlineEditing()) { RestoreSelection(); return; }
        if (_updating || _busy || _genealogy is not Guid genealogy || _people.SelectedItem is not Option next || next.Id == _person) return;
        _busy = true; IsEnabled = false;
        try
        {
            var prior = _session.Draft;
            var choice = Choose();
            var result = await _session.SelectAsync(genealogy, next.Id, choice);
            if (prior?.IsSaved == true && choice == DraftLeaveChoice.Save && result is DraftTransitionResult.Completed or DraftTransitionResult.SavedThenReadFailed)
            {
                var items = _people.Items.OfType<Option>().Select(item => item.Id == prior.Original.PersonId ? item with { Name = prior.Values.Name.Trim() } : item).ToList();
                if (items.All(item => item.Id != prior.Original.PersonId)) items.Add(new Option(prior.Original.PersonId, prior.Values.Name.Trim()));
                _updating = true; _people.ItemsSource = items; _people.SelectedItem = items.FirstOrDefault(item => item.Id == next.Id); _updating = false;
            }
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); ShowDraft(); return; }
            _person = next.Id; ShowDraft(); RenderCanvas(); _status.Text = "修改后请显式保存。";
        }
        catch (Exception ex) { _status.Text = $"选择人物失败：{ex.Message}"; RestoreSelection(); }
        finally { IsEnabled = true; _busy = false; }
    }
    private void RestoreSelection()
    {
        _updating = true;
        _genealogies.SelectedItem = _genealogies.Items.OfType<Option>().FirstOrDefault(o => o.Id == _genealogy);
        _people.SelectedItem = _people.Items.OfType<Option>().FirstOrDefault(o => o.Id == _person);
        _updating = false;
    }
    private void Report(DraftTransitionResult result)
    {
        _status.Text = result switch
        {
            DraftTransitionResult.Cancelled => "已取消，保留当前草稿。",
            DraftTransitionResult.SaveFailed => $"未保存，草稿保留：{_session.Error}",
            DraftTransitionResult.SavedRefreshFailed => $"已保存，但刷新失败，请重新选择人物：{_session.Error}",
            DraftTransitionResult.ReadFailed => $"读取人物失败：{_session.Error}",
            DraftTransitionResult.SavedThenReadFailed => $"档案已保存，但读取所选人物失败：{_session.Error}",
            _ => "操作完成。"
        };
        if (result == DraftTransitionResult.SavedRefreshFailed)
        {
            _genealogy = null; _updating = true; _people.ItemsSource = null; _updating = false;
            _status.Text += " 请重新选择谱系。";
        }
        if (result is DraftTransitionResult.SavedRefreshFailed or DraftTransitionResult.ReadFailed or DraftTransitionResult.SavedThenReadFailed) { _person = null; RestoreSelection(); }
    }
    private async Task SaveAsync()
    {
        if (BlockWhileInlineEditing()) return;
        if (_busy || _session.Draft is null) return;
        _busy = true; IsEnabled = false;
        try
        {
            var dirty = _session.Draft.IsDirty;
            var result = await _session.SaveAsync(); Report(result);
            if (result == DraftTransitionResult.Completed && dirty && _genealogy is Guid genealogy)
            {
                try
                {
                    using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
                    var people = await context.People.AsNoTracking().Where(p => p.GenealogyId == genealogy).OrderBy(p => p.Name)
                        .Select(p => new Option(p.Id, p.Name)).ToListAsync();
                    _updating = true; _people.ItemsSource = people; _people.SelectedItem = people.FirstOrDefault(p => p.Id == _person); _updating = false;
                    RenderCanvas();
                    _status.Text = "档案已保存。 " + CanvasStatus();
                }
                catch (Exception ex)
                {
                    _updating = true; _people.ItemsSource = null; _updating = false;
                    _status.Text = $"档案已保存，但人物列表或焦点刷新失败：{ex.Message}";
                }
            }
            else if (result == DraftTransitionResult.Completed) _status.Text = "没有需要保存的修改。 " + CanvasStatus();
            ShowDraft();
        }
        catch (Exception ex) { _status.Text = $"保存失败：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    internal async Task<bool> PrepareForOwnerExitAsync()
    {
        if (_v1Opening) { _status.Text = "请等待隔离样本打开完成。"; return false; }
        if (OwnedWindows.OfType<V1FirstRootWindow>().Any(window => !window.CanCloseForOwner()))
        { _status.Text = "请先完成隔离样本中的输入。"; return false; }
        if (BlockWhileInlineEditing()) return false;
        if (_approvedClose) return true;
        if (_busy) { _status.Text = "请等待当前操作结束后再关闭。"; return false; }
        if (_chart is not null && _sessionOrders.Count > 0)
        {
            var discard = DiscardOrdersOnCloseForTest?.Invoke() ?? (MessageBox.Show(this,
                "当前图表有未保存的顺序草稿。选择“是”放弃排序并继续关闭；选择“否”返回，以便保存排序。",
                "未保存的图表顺序", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
            if (!discard) { _status.Text = "顺序草稿已保留；可保存、放弃或继续编辑。"; return false; }
            _sessionOrders.Clear();
        }
        _busy = true; IsEnabled = false;
        try
        {
            var result = await _session.LeaveAsync(Choose());
            if (result != DraftTransitionResult.Completed) { Report(result); ShowDraft(); return false; }
            await ReleaseProjectionAsync();
            _approvedClose = true; return true;
        }
        catch (Exception ex) { _status.Text = $"无法关闭：{ex.Message}"; return false; }
        finally { if (!_approvedClose) IsEnabled = true; _busy = false; }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_approvedClose) return;
        e.Cancel = true;
        if (await PrepareForOwnerExitAsync()) _ = Dispatcher.BeginInvoke(new Action(Close));
    }
    private bool BlockWhileChartOrderPending()
    {
        if (_chart is null || _sessionOrders.Count == 0) return false;
        _status.Text = "请先保存或放弃未保存的图表顺序，再新建人物或关系。";
        return true;
    }
}
