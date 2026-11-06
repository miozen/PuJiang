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
    private sealed record Option(Guid Id, string Name);
    private sealed record OrderGroup(string Key, IReadOnlyList<Guid> People, Guid? ParentId);
    private sealed record PartnerOption(Guid? Id, string Name, FamilyUnionKind Kind)
    {
        public string Label => $"{(Kind == FamilyUnionKind.Marriage ? "婚配" : Kind == FamilyUnionKind.Partnership ? "伴侣" : "类型未详")}：{Name}";
    }
    private readonly TargetContextCoordinator _coordinator;
    private readonly WorkbenchDraftSession _session;
    private readonly ListBox _genealogies = new() { DisplayMemberPath = nameof(Option.Name) };
    private readonly ListBox _people = new() { DisplayMemberPath = nameof(Option.Name) };
    private readonly ListBox _diagnostics = new() { Height = 90 };
    private readonly TextBox _name = new();
    private readonly TextBox _generation = new();
    private readonly TextBox _displayRank = new();
    private readonly Button _applyRank = new() { Content = "设置显示顺序", Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _recordedGeneration = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };
    private readonly TextBox _birth = new();
    private readonly TextBox _death = new();
    private readonly TextBox _biography = new() { AcceptsReturn = true, Height = 120, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _sex = new() { ItemsSource = Enum.GetValues<PersonSex>() };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private InlineNameEditor? _inlineNameEditor;
    private Func<Point?>? _inlineLocation;
    private bool _inlineInvalid;
    private readonly Canvas _canvas = new() { Background = Brushes.White, Width = 650, Height = 360 };
    private readonly ScrollViewer _canvasScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ScaleTransform _canvasScale = new(1, 1);
    private Point? _panStart;
    private Point _panOffset;
    private readonly DispatcherTimer _canvasMonitor = new() { Interval = TimeSpan.FromSeconds(1) };
    private WorkbenchProjectionSession? _projection;
    private long _projectionRevision;
    private readonly Dictionary<Guid, ProjectionReadSession.PersonRow> _canvasPeople = new();
    private readonly Dictionary<Guid, IReadOnlyList<PartnerOption>> _canvasPartners = new();
    private readonly Dictionary<Guid, IReadOnlyList<ProjectionReadSession.SiblingRow>> _canvasSiblings = new();
    private readonly List<HorizontalLayoutRelationship> _canvasEdges = [];
    private readonly Dictionary<Guid, OrderGroup> _orderGroups = new();
    private readonly Dictionary<string, Dictionary<Guid, int>> _sessionOrders = new();
    private readonly Dictionary<Guid, int> _layoutOrders = new();
    private int _unresolvedCount;
    private bool _canvasTruncated;
    private bool _generationEdited;
    private readonly List<string> _diagnosticMessages = [];
    private bool _birthEdited;
    private bool _deathEdited;
    private bool _biographyEdited;
    private Func<Guid, Task>? _afterCommit;
    private Guid? _genealogy;
    private Guid? _person;
    private bool _updating;
    private bool _busy;
    private bool _approvedClose;
    private bool _canvasPolling;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Func<DraftLeaveChoice>? ChooseForTest { get; set; }
    internal WorkbenchDraftSession SessionForTest => _session;
    internal string StatusForTest => _status.Text;
    internal Func<Guid, Task>? AfterCommitAsync { set => _afterCommit = value; }
    internal Task InitializeForTest() => _loaded.Task;
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
    internal void SetNameForTest(string name) => _name.Text = name;
    internal Task SaveForTest() => SaveAsync();
    internal bool CanvasContainsForTest(Guid id) => _canvasPeople.ContainsKey(id);
    internal string? CanvasNameForTest(Guid id) => _canvasPeople.GetValueOrDefault(id)?.Name;
    internal int CanvasColumnForTest(Guid id) => _canvasPeople.TryGetValue(id, out var person) && RecordedGenerationParser.TryParse(person.RecordedGeneration, out var generation) ? generation : 0;
    internal int CanvasEdgesForTest => _canvasEdges.Count;
    internal IReadOnlyList<string> DiagnosticsForTest => _diagnosticMessages.ToArray();
    internal IReadOnlyList<string> CanvasPartnersForTest(Guid id) => _canvasPartners.TryGetValue(id, out var partners) ? partners.Select(p => p.Label).ToArray() : [];
    internal IReadOnlyList<string> CanvasSiblingsForTest(Guid id) => _canvasSiblings.TryGetValue(id, out var siblings) ? siblings.Select(p => p.Name).ToArray() : [];
    internal Task BeginNewForTest(Guid? parentId = null, PersonRelationshipKind? kind = null) => BeginNewAsync(parentId, kind);
    internal void SetGenerationForTest(string value) => _generation.Text = value;
    internal Task PollCanvasForTest() => PollCanvasAsync();
    internal Task SelectCanvasForTest(Guid id) => SelectCanvasPersonAsync(id);
    internal Border CanvasCardForTest(Guid id) => _canvas.Children.OfType<Border>().Single(item => item.Tag is Guid value && value == id);
    internal Canvas CanvasForTest => _canvas;
    internal void RenderCanvasForTest() => RenderCanvas();
    internal void BeginProjectedNameInput(Func<HorizontalLayoutNode> project, Action<string> confirmed) =>
        ShowInlineNameEditor(() => { var node = project(); return new Point(node.X + 20, node.Y + 40); }, "", confirmed);
    internal Task SetDisplayRankForTest(int rank) { _displayRank.Text = rank.ToString(); return SetDisplayRankAsync(); }
    internal Task MoveForTest(int delta) => MoveSiblingAsync(delta);

    public WorkbenchProfileWindow(TargetContextCoordinator coordinator) : this(coordinator, WorkbenchDraftSession.Create(coordinator)) { }
    internal WorkbenchProfileWindow(TargetContextCoordinator coordinator, WorkbenchDraftSession session)
    {
        _coordinator = coordinator; _session = session;
        Title = "世系编修工作台 — 画布试用"; Width = 1100; Height = 720; MinWidth = 760; MinHeight = 480;
        _session.AfterCommitAsync = RefreshAfterCommitAsync;
        var outer = new DockPanel(); DockPanel.SetDock(_status, Dock.Bottom); outer.Children.Add(_status);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var left = new DockPanel { Margin = new Thickness(12) };
        var title = new TextBlock { Text = "谱系管理", FontSize = 17, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(title, Dock.Top); left.Children.Add(title); left.Children.Add(_genealogies);
        columns.Children.Add(left);
        var center = new DockPanel { Margin = new Thickness(8) };
        var notice = new TextBlock { Text = "右键画布新建人物，右键人物编辑或新增子女。未识别世代及未决关系保留在辅助列表；排列不代表谱载排行。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(notice, Dock.Top); center.Children.Add(notice);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var create = new Button { Content = "新建人物", Padding = new Thickness(6) };
        create.Click += async (_, _) => await BeginNewAsync(); tools.Children.Add(create);
        var reopen = new Button { Content = "重新打开画布", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
        reopen.Click += async (_, _) => await ReopenCanvasAsync(); tools.Children.Add(reopen);
        DockPanel.SetDock(tools, Dock.Top); center.Children.Add(tools);
        _people.Height = 100;
        var peoplePanel = new Expander { Header = "人物辅助列表（含未识别世代）", Content = _people, IsExpanded = false };
        DockPanel.SetDock(peoplePanel, Dock.Top); center.Children.Add(peoplePanel);
        var diagnosticsPanel = new Expander { Header = "未显示或待处理的世代与关系", Content = _diagnostics, IsExpanded = false };
        DockPanel.SetDock(diagnosticsPanel, Dock.Top); center.Children.Add(diagnosticsPanel);
        _canvas.LayoutTransform = _canvasScale; _canvasScroll.Content = _canvas; center.Children.Add(_canvasScroll);
        Grid.SetColumn(center, 1); columns.Children.Add(center);
        var right = new StackPanel { Margin = new Thickness(12) };
        right.Children.Add(new TextBlock { Text = "人物资料", FontSize = 17 });
        AddField(right, "姓名", _name); AddField(right, "世代（1–1000）", _generation); right.Children.Add(_recordedGeneration);
        AddField(right, "同胞显示顺序（仅本次会话）", _displayRank); right.Children.Add(_applyRank);
        _applyRank.Click += async (_, _) => await SetDisplayRankAsync();
        AddField(right, "性别", _sex); AddField(right, "出生", _birth);
        AddField(right, "逝世", _death); AddField(right, "生平", _biography);
        var save = new Button { Content = "保存档案", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8) };
        save.Click += async (_, _) => await SaveAsync(); right.Children.Add(save);
        Grid.SetColumn(right, 2); columns.Children.Add(right); outer.Children.Add(columns); Content = outer;
        _genealogies.SelectionChanged += async (_, _) => await ChangeGenealogyAsync();
        _people.SelectionChanged += async (_, _) => await ChangePersonAsync();
        _name.TextChanged += (_, _) => UpdateDraft();
        _generation.TextChanged += (_, _) => { if (!_updating) _generationEdited = true; UpdateDraft(); };
        _birth.TextChanged += (_, _) => { if (!_updating) _birthEdited = true; UpdateDraft(); };
        _death.TextChanged += (_, _) => { if (!_updating) _deathEdited = true; UpdateDraft(); };
        _biography.TextChanged += (_, _) => { if (!_updating) _biographyEdited = true; UpdateDraft(); };
        _sex.SelectionChanged += (_, _) => UpdateDraft();
        Loaded += async (_, _) => { try { await LoadGenealogiesAsync(); } finally { _loaded.TrySetResult(); } };
        _canvasMonitor.Tick += async (_, _) => await PollCanvasAsync();
        _canvas.MouseRightButtonDown += (_, e) => { if (e.OriginalSource != _canvas) return; e.Handled = true; ShowCanvasMenu(); };
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
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; await BeginNewAsync(); }
        };
        PreviewMouseDown += (_, e) =>
        {
            if (_inlineNameEditor is not null && e.OriginalSource is DependencyObject source &&
                !ReferenceEquals(source, _inlineNameEditor) && !_inlineNameEditor.IsAncestorOf(source))
            { e.Handled = true; BlockWhileInlineEditing(); }
        };
        Closing += OnClosing;
        ShowDraft();
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
        _panStart = e.GetPosition(_canvasScroll); _panOffset = new Point(_canvasScroll.HorizontalOffset, _canvasScroll.VerticalOffset);
        _canvas.CaptureMouse(); e.Handled = true;
    }
    private void OnCanvasMove(object sender, MouseEventArgs e)
    {
        if (_panStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(_canvasScroll) - start;
        _canvasScroll.ScrollToHorizontalOffset(_panOffset.X - delta.X); _canvasScroll.ScrollToVerticalOffset(_panOffset.Y - delta.Y);
    }
    private void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_panStart is null) return;
        _panStart = null; _canvas.ReleaseMouseCapture(); e.Handled = true;
    }
    private void ClearCanvas()
    {
        var editor = _inlineNameEditor;
        var restoreFocus = editor?.IsKeyboardFocusWithin == true;
        _canvasPeople.Clear(); _canvasPartners.Clear(); _canvasSiblings.Clear(); _canvasEdges.Clear(); _orderGroups.Clear(); _sessionOrders.Clear(); _layoutOrders.Clear(); _diagnosticMessages.Clear(); _diagnostics.ItemsSource = null;
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
        if (_canvasPeople.Count == 0) { RestoreInlineEditor(editor, restoreFocus); return; }
        var people = _canvasPeople.Values.Select(p => new HorizontalLayoutPerson(p.Id, ParseGeneration(p) - 1, _layoutOrders.GetValueOrDefault(p.Id), 170,
            85 + (_canvasPartners.TryGetValue(p.Id, out var partners) ? partners.Count * 20 : 0) + (_canvasSiblings.TryGetValue(p.Id, out var siblings) ? siblings.Count * 20 : 0))).ToArray();
        var layout = HorizontalLineageLayout.Calculate(people, _canvasEdges, _canvasPeople.Keys.ToHashSet(), allowSkippedGenerations: true);
        if (HasUnsafeCrossing(layout))
        {
            _unresolvedCount += _canvasEdges.Count; _canvasEdges.Clear();
            _diagnosticMessages.Add("部分连线会穿越其他人物或不相关连线，已暂停绘制，请在关系视图中复核。");
            _diagnostics.ItemsSource = _diagnosticMessages.ToArray();
            layout = HorizontalLineageLayout.Calculate(people, _canvasEdges, _canvasPeople.Keys.ToHashSet(), allowSkippedGenerations: true);
        }
        foreach (var generation in layout.Nodes.Select(n => n.Generation).Distinct())
        {
            var heading = new TextBlock { Text = $"第 {generation + 1} 世", FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(heading, generation * 234 + 20); Canvas.SetTop(heading, 10); _canvas.Children.Add(heading);
        }
        foreach (var edge in layout.Edges)
        {
            var line = new System.Windows.Shapes.Polyline { Stroke = Brushes.SlateGray, StrokeThickness = 1.5, IsHitTestVisible = false };
            foreach (var point in edge.Points) line.Points.Add(new Point(point.X + 20, point.Y + 40));
            _canvas.Children.Add(line);
        }
        foreach (var node in layout.Nodes)
        {
            var person = _canvasPeople[node.PersonId];
            var displayedName = _person == person.Id && _session.Draft is { } draft &&
                draft.Original.PersonId == person.Id && draft.Original.GenealogyId == _genealogy
                ? draft.Values.Name : person.Name;
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = $"{displayedName}\n谱载世代：{person.RecordedGeneration ?? "未记"}\n谱载排行：{person.RecordedBirthOrder ?? "未记"}", TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis });
            if (_canvasPartners.TryGetValue(person.Id, out var partners) && (_person == person.Id || partners.Any(p => p.Id == _person)))
            {
                foreach (var partner in partners)
                {
                    var label = new TextBlock { Text = partner.Label, Foreground = Brushes.SteelBlue, TextTrimming = TextTrimming.CharacterEllipsis };
                    if (partner.Id is Guid partnerId)
                    {
                        label.MouseLeftButtonDown += async (_, e) => { e.Handled = true; await SelectCanvasPersonAsync(partnerId); };
                        label.MouseRightButtonDown += async (_, e) => { e.Handled = true; await SelectCanvasPersonAsync(partnerId); if (_person == partnerId) _name.Focus(); };
                    }
                    content.Children.Add(label);
                }
            }
            if (_person == person.Id && _canvasSiblings.TryGetValue(person.Id, out var siblings))
            {
                foreach (var sibling in siblings)
                {
                    var label = new TextBlock { Text = $"兄妹：{sibling.Name}", Foreground = Brushes.SteelBlue, TextTrimming = TextTrimming.CharacterEllipsis };
                    label.MouseLeftButtonDown += async (_, e) => { e.Handled = true; await SelectCanvasPersonAsync(sibling.PersonId); };
                    content.Children.Add(label);
                }
            }
            var card = new Border { Width = node.Width, Height = node.Height, BorderBrush = _person == person.Id ? Brushes.SteelBlue : Brushes.SlateGray,
                BorderThickness = new Thickness(_person == person.Id ? 2 : 1), CornerRadius = new CornerRadius(5), Background = Brushes.White, Padding = new Thickness(8),
                Tag = person.Id, Child = content };
            card.MouseLeftButtonDown += async (_, e) => { e.Handled = true; await SelectCanvasPersonAsync(person.Id); };
            card.MouseRightButtonDown += async (_, e) =>
            {
                e.Handled = true; await SelectCanvasPersonAsync(person.Id);
                if (_person != person.Id || _session.Draft?.Original.IsNew != false) return;
                var menu = new ContextMenu();
                var edit = new MenuItem { Header = "编辑人物" }; edit.Click += (_, _) => _name.Focus(); menu.Items.Add(edit);
                var inline = new MenuItem { Header = "原位编辑姓名" }; inline.Click += (_, _) => BeginInlineNameEdit(person.Id); menu.Items.Add(inline);
                var child = new MenuItem { Header = "新增子女（生物关系）" }; child.Click += async (_, _) => await BeginNewAsync(person.Id, PersonRelationshipKind.BiologicalParent); menu.Items.Add(child);
                var adopted = new MenuItem { Header = "新增子女（收养关系）" }; adopted.Click += async (_, _) => await BeginNewAsync(person.Id, PersonRelationshipKind.AdoptiveParent); menu.Items.Add(adopted);
                var sibling = new MenuItem { Header = "新增兄妹" }; sibling.Click += async (_, _) => await BeginSiblingAsync(person.Id); menu.Items.Add(sibling);
                var up = new MenuItem { Header = "同胞上移", IsEnabled = CanMove(person.Id, -1) }; up.Click += async (_, _) => await MoveSiblingAsync(-1); menu.Items.Add(up);
                var down = new MenuItem { Header = "同胞下移", IsEnabled = CanMove(person.Id, 1) }; down.Click += async (_, _) => await MoveSiblingAsync(1); menu.Items.Add(down);
                var spouse = new MenuItem { Header = "新增配偶（婚配）" }; spouse.Click += async (_, _) => await BeginNewAsync(partnerId: person.Id, unionKind: FamilyUnionKind.Marriage); menu.Items.Add(spouse);
                var target = _canvas.Children.OfType<Border>().FirstOrDefault(item => item.Tag is Guid id && id == person.Id);
                if (target is null) return;
                target.ContextMenu = menu; menu.PlacementTarget = target; menu.IsOpen = true;
            };
            Canvas.SetLeft(card, node.X + 20); Canvas.SetTop(card, node.Y + 40); _canvas.Children.Add(card);
        }
        _canvas.Width = Math.Max(650, layout.Nodes.Max(n => n.X + n.Width) + 40);
        _canvas.Height = Math.Max(360, layout.Nodes.Max(n => n.Y + n.Height) + 60);
        RestoreInlineEditor(editor, restoreFocus);
    }
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
    private void ShowInlineNameEditor(Func<Point?> locate, string initial, Action<string> confirmed, Action? afterConfirm = null)
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
        editor.Cancelled += CloseEditor;
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
    private void ShowCanvasMenu()
    {
        var menu = new ContextMenu(); var create = new MenuItem { Header = "新建人物" };
        create.Click += async (_, _) => await BeginNewAsync(); menu.Items.Add(create); _canvas.ContextMenu = menu; menu.PlacementTarget = _canvas; menu.IsOpen = true;
    }
    private async Task BeginSiblingAsync(Guid siblingId)
    {
        if (BlockWhileInlineEditing()) return;
        if (_busy || _projection is null) return;
        _busy = true; IsEnabled = false;
        ProjectionReadSession.ParentCandidate? parent = null;
        PersonRelationshipKind? siblingParentKind = null;
        try
        {
            await ValidateCanvasIdentityAsync(siblingId);
            var result = await _projection.ReadParentsAsync(_projectionRevision, siblingId, null, 100);
            if (result.Page.NextCursor is not null) throw new InvalidOperationException("父母候选过多，请先在关系视图处理。");
            if (result.Page.Items.Count == 1) { parent = result.Page.Items[0]; siblingParentKind = parent.Kind; }
            else if (result.Page.Items.Count > 1)
            {
                var dialog = new Window { Title = "选择共同父母", Owner = this, Width = 500, Height = 280, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                var panel = new StackPanel { Margin = new Thickness(16) };
                panel.Children.Add(new TextBlock { Text = "请选择新兄妹共同的父母及关系类型。", TextWrapping = TextWrapping.Wrap });
                var options = result.Page.Items.Select(p => new { Value = p, Label = $"{p.ParentName}｜{p.Kind switch { PersonRelationshipKind.BiologicalParent => "生物父母", PersonRelationshipKind.AdoptiveParent => "养父母", _ => "继嗣父母" }}｜{p.ParentId}" }).ToArray();
                var list = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", Margin = new Thickness(0, 12, 0, 12) }; panel.Children.Add(list);
                var kinds = new[] { new { Kind = PersonRelationshipKind.BiologicalParent, Label = "生物父母" }, new { Kind = PersonRelationshipKind.AdoptiveParent, Label = "养父母" }, new { Kind = PersonRelationshipKind.HeirParent, Label = "继嗣父母" } };
                var kindBox = new ComboBox { ItemsSource = kinds, DisplayMemberPath = "Label", Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(kindBox);
                var confirm = new Button { Content = "确定", IsEnabled = false, Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right };
                list.SelectionChanged += (_, _) => confirm.IsEnabled = list.SelectedIndex >= 0 && kindBox.SelectedIndex >= 0;
                kindBox.SelectionChanged += (_, _) => confirm.IsEnabled = list.SelectedIndex >= 0 && kindBox.SelectedIndex >= 0;
                confirm.Click += (_, _) => dialog.DialogResult = true; panel.Children.Add(confirm); dialog.Content = panel;
                if (dialog.ShowDialog() != true || list.SelectedIndex < 0) return;
                parent = options[list.SelectedIndex].Value;
                siblingParentKind = kinds[kindBox.SelectedIndex].Kind;
            }
        }
        catch (Exception ex) { ClearCanvas(); _canvasMonitor.Stop(); _status.Text = $"无法添加兄妹，请重新打开画布：{ex.Message}"; return; }
        finally { _busy = false; IsEnabled = true; }
        await BeginNewAsync(parent?.ParentId, siblingParentKind, siblingId: siblingId);
    }
    private async Task BeginNewAsync(Guid? parentId = null, PersonRelationshipKind? kind = null, Guid? partnerId = null, FamilyUnionKind? unionKind = null, Guid? siblingId = null)
    {
        if (BlockWhileInlineEditing()) return;
        if (_busy || _genealogy is not Guid genealogy) return;
        _busy = true; IsEnabled = false;
        WorkbenchPersonDraft? prior = null;
        var relatedId = siblingId ?? parentId ?? partnerId;
        var started = false;
        var focusName = false;
        try
        {
            if (relatedId is Guid before) await ValidateCanvasIdentityAsync(before);
            prior = _session.Draft;
            var result = await _session.BeginNewAsync(genealogy, Choose(), parentId, kind, partnerId, unionKind, siblingId);
            SyncSavedPerson(prior);
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); ShowDraft(); return; }
            started = true;
            if (relatedId is Guid after) await ValidateCanvasIdentityAsync(after);
            var draft = _session.Draft!;
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
        var page = await projection.ReadPeopleAsync(revision, null, 100);
        var rows = page.Page.Items.Where(p => RecordedGenerationParser.TryParse(p.RecordedGeneration, out _)).ToDictionary(p => p.Id);
        var unresolved = page.Page.Items.Count - rows.Count;
        var diagnostics = page.Page.Items.Where(p => !RecordedGenerationParser.TryParse(p.RecordedGeneration, out _))
            .Select(p => $"{p.Name}：谱载世代 {p.RecordedGeneration ?? "未记"}，未作为主节点显示，可在辅助列表查看。").ToList();
        var candidates = new List<HorizontalLayoutRelationship>();
        var partners = new Dictionary<Guid, IReadOnlyList<PartnerOption>>();
        var siblings = new Dictionary<Guid, IReadOnlyList<ProjectionReadSession.SiblingRow>>();
        var parentCounts = new Dictionary<Guid, int>();
        var incompleteSiblings = new HashSet<Guid>();
        foreach (var child in rows.Values)
        {
            var siblingPage = await projection.ReadSiblingsAsync(revision, child.Id, null, 100);
            siblings.Add(child.Id, siblingPage.Page.Items);
            if (siblingPage.Page.NextCursor is not null) { incompleteSiblings.Add(child.Id); unresolved++; diagnostics.Add($"{child.Name}：兄妹关系未全部加载。"); }
            var unions = await projection.ReadUnionsAsync(revision, child.Id, null, 100);
            var labels = new List<PartnerOption>();
            foreach (var union in unions.Page.Items)
            {
                var other = union.PartnerOneId == child.Id ? union.PartnerTwoId : union.PartnerOneId;
                var name = other is Guid otherId ? (await projection.ReadPersonAsync(revision, otherId)).Person.Name : "未详";
                labels.Add(new PartnerOption(other, name, union.Kind));
            }
            partners.Add(child.Id, labels.AsReadOnly());
            if (unions.Page.NextCursor is not null) { unresolved++; diagnostics.Add($"{child.Name}：婚配记录未全部加载。"); }
            var parents = await projection.ReadParentsAsync(revision, child.Id, null, 100);
            parentCounts[child.Id] = parents.Page.NextCursor is not null ? -1 : parents.Page.Items.Count;
            if (parents.Page.NextCursor is not null || parents.Page.Items.Count > 1)
            {
                unresolved++; diagnostics.Add($"{child.Name}：父母候选存在多项或未全部加载，暂不连线。"); continue;
            }
            if (parents.Page.Items.Count == 0) continue;
            var parent = parents.Page.Items[0];
            if (!rows.TryGetValue(parent.ParentId, out var parentRow) || ParseGeneration(child) <= ParseGeneration(parentRow))
            { unresolved++; diagnostics.Add($"{child.Name}：父母 {parent.ParentName} 未显示或世代列冲突，暂不连线。"); continue; }
            candidates.Add(new HorizontalLayoutRelationship(parent.RelationshipId, parent.ParentId, child.Id));
        }
        var groups = new List<OrderGroup>(); var blockedParents = new HashSet<Guid>();
        foreach (var group in candidates.GroupBy(edge => edge.ParentId))
        {
            var all = await projection.ReadChildrenAsync(revision, group.Key, null, 100);
            if (all.Page.NextCursor is not null || !group.Select(e => e.Id).ToHashSet().SetEquals(all.Page.Items.Select(e => e.RelationshipId)))
            {
                blockedParents.Add(group.Key); unresolved += group.Count();
                diagnostics.Add($"{rows[group.Key].Name}：子女关系未完整显示或存在歧义，暂不排序和连线。"); continue;
            }
            if (group.Count() > 1)
            {
                var members = group.Select(e => e.ChildId).ToArray();
                groups.Add(MakeOrderGroup(group.Key, members, group.Select(e => e.Id), rows));
            }
        }
        var visited = new HashSet<Guid>();
        foreach (var start in rows.Keys.Where(id => parentCounts[id] == 0 && siblings[id].Count > 0))
        {
            if (!visited.Add(start)) continue;
            var members = new List<Guid>(); var relationIds = new HashSet<Guid>(); var queue = new Queue<Guid>(); queue.Enqueue(start); var incomplete = false;
            while (queue.TryDequeue(out var id))
            {
                members.Add(id); if (incompleteSiblings.Contains(id)) incomplete = true;
                foreach (var link in siblings[id])
                {
                    relationIds.Add(link.RelationshipId);
                    if (!rows.ContainsKey(link.PersonId) || parentCounts[link.PersonId] != 0 || ParseGeneration(rows[link.PersonId]) != ParseGeneration(rows[id])) { incomplete = true; continue; }
                    if (visited.Add(link.PersonId)) queue.Enqueue(link.PersonId);
                }
            }
            if (incomplete) { unresolved++; diagnostics.Add($"{rows[start].Name}：兄妹关系未完整显示或父母情况不一致，暂不排序。"); }
            else if (members.Count > 1) groups.Add(MakeOrderGroup(null, members, relationIds, rows));
        }
        var layoutOrders = new Dictionary<Guid, int>(); var groupByPerson = new Dictionary<Guid, OrderGroup>();
        foreach (var group in groups)
        {
            foreach (var id in group.People) groupByPerson.Add(id, group);
            if (!_sessionOrders.TryGetValue(group.Key, out var order) || !IsComplete(group, order))
            {
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
        _canvasSiblings.Clear(); foreach (var pair in siblings) _canvasSiblings.Add(pair.Key, pair.Value);
        _orderGroups.Clear(); foreach (var pair in groupByPerson) _orderGroups.Add(pair.Key, pair.Value);
        _layoutOrders.Clear(); foreach (var pair in layoutOrders) _layoutOrders.Add(pair.Key, pair.Value);
        foreach (var key in _sessionOrders.Keys.Except(groups.Select(g => g.Key)).ToArray()) _sessionOrders.Remove(key);
        _canvasEdges.Clear(); _canvasEdges.AddRange(edges); _unresolvedCount = unresolved; _canvasTruncated = page.Page.NextCursor is not null;
        _diagnosticMessages.Clear(); _diagnosticMessages.AddRange(diagnostics); _diagnostics.ItemsSource = _diagnosticMessages.ToArray();
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
        if (!_orderGroups.TryGetValue(person, out var group) || !_sessionOrders.TryGetValue(group.Key, out var order) || !IsComplete(group, order)) return false;
        return order[person] + delta >= 1 && order[person] + delta <= group.People.Count;
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
        var order = new Dictionary<Guid, int>(_sessionOrders[group.Key]); var rank = order[person];
        var other = order.Single(pair => pair.Value == rank + delta).Key; order[person] = rank + delta; order[other] = rank;
        await ApplyOrderAsync(person, group, order);
    }
    private async Task ApplyOrderAsync(Guid person, OrderGroup group, Dictionary<Guid, int> order)
    {
        _busy = true; IsEnabled = false;
        try
        {
            await ValidateCanvasIdentityAsync(person);
            _sessionOrders[group.Key] = order;
            await LoadCanvasAsync(); ShowDraft();
            _status.Text = IsComplete(group, order) ? "已调整本次显示顺序，未修改谱载排行。" : $"已指定 {order.Count}/{group.People.Count} 人；请继续指定其余人物。";
        }
        catch (Exception ex) { ClearCanvas(); _canvasMonitor.Stop(); ShowDraft(); _status.Text = $"画布已失效，草稿保留：{ex.Message}"; }
        finally { _busy = false; IsEnabled = true; }
    }
    private async Task ReopenCanvasAsync()
    {
        if (BlockWhileInlineEditing()) return;
        if (_busy || _genealogy is not Guid genealogy) return;
        _busy = true; IsEnabled = false;
        try { await ResetProjectionAsync(genealogy); await LoadCanvasAsync(); _status.Text = CanvasStatus(); }
        catch (Exception ex) { _status.Text = $"画布无法重新打开：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task SelectCanvasPersonAsync(Guid id)
    {
        if (BlockWhileInlineEditing()) return;
        if (_busy || !CanvasContainsIdentity(id) || _projection is null) return;
        var projection = _projection; var revision = _projectionRevision;
        _busy = true; IsEnabled = false;
        try
        {
            await projection.ValidateAsync(revision);
            if (!ReferenceEquals(projection, _projection) || !projection.IsCurrent(revision) || !CanvasContainsIdentity(id)) return;
            var target = _people.Items.OfType<Option>().FirstOrDefault(item => item.Id == id);
            if (target is null) { ClearCanvas(); _status.Text = "人物列表已变化，请重新选择谱系。"; return; }
            _updating = true; _people.SelectedItem = target; _updating = false;
            _busy = false;
            await ChangePersonAsync(); RenderCanvas();
        }
        catch (Exception ex) { ClearCanvas(); _status.Text = $"画布已失效，草稿保留：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private bool CanvasContainsIdentity(Guid id) => _canvasPeople.ContainsKey(id) || _canvasPartners.Values.Any(items => items.Any(item => item.Id == id));
    private async Task PollCanvasAsync()
    {
        if (_busy || _canvasPolling || _canvasPeople.Count == 0 || _projection is null) return;
        _canvasPolling = true;
        var projection = _projection; var revision = _projectionRevision;
        try { await projection.ValidateAsync(revision); }
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
        try
        {
            if (_afterCommit is not null) await _afterCommit(genealogyId);
            if (_projection is null || _genealogy != genealogyId) return;
            var projection = _projection; var revision = await projection.ReopenAsync();
            _projectionRevision = revision;
            await LoadCanvasAsync(previous);
            _canvasMonitor.Start();
        }
        catch { ClearCanvas(); _canvasMonitor.Stop(); throw; }
    }
    private string CanvasStatus() => $"画布显示 {_canvasPeople.Count} 人；{_unresolvedCount} 项世代或关系待处理。" +
        (_canvasTruncated ? " 仅显示前 100 人，其他人物可在辅助列表查看。" : "");
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
    }
    private void ShowDraft()
    {
        _updating = true;
        try
        {
            var v = _session.Draft?.Values;
            _name.Text = v?.Name ?? ""; _sex.SelectedItem = v?.Sex; _birth.Text = v?.BirthDate ?? "";
            _death.Text = v?.DeathDate ?? ""; _biography.Text = v?.Biography ?? "";
            _generation.Text = RecordedGenerationParser.TryParse(v?.RecordedGeneration, out var generation) ? generation.ToString() : "";
            _generationEdited = _birthEdited = _deathEdited = _biographyEdited = false;
            _recordedGeneration.Text = $"谱载原文：{_session.Draft?.Original.Values.RecordedGeneration ?? "未记"}";
            _displayRank.Text = ""; _displayRank.IsEnabled = _applyRank.IsEnabled = false;
            if (_person is Guid person && _orderGroups.TryGetValue(person, out var group))
            {
                _displayRank.IsEnabled = _applyRank.IsEnabled = true;
                if (_sessionOrders.TryGetValue(group.Key, out var order) && order.TryGetValue(person, out var rank)) _displayRank.Text = rank.ToString();
            }
            _name.IsEnabled = _generation.IsEnabled = _sex.IsEnabled = _birth.IsEnabled = _death.IsEnabled = _biography.IsEnabled = v is not null;
        }
        finally { _updating = false; }
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
    private async Task ChangeGenealogyAsync()
    {
        if (!_updating && BlockWhileInlineEditing()) { RestoreSelection(); return; }
        if (_updating || _busy || _genealogies.SelectedItem is not Option next || next.Id == _genealogy) return;
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
            _genealogy = next.Id; _person = null; _updating = true; _people.ItemsSource = people; _people.SelectedItem = null; _updating = false;
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
            var partnerToSelect = _session.Draft.Original.IsNew ? _session.Draft.Original.PartnerId : null;
            var result = await _session.SaveAsync(); Report(result);
            if (result == DraftTransitionResult.Completed && dirty && _genealogy is Guid genealogy)
            {
                try
                {
                    using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
                    var people = await context.People.AsNoTracking().Where(p => p.GenealogyId == genealogy).OrderBy(p => p.Name)
                        .Select(p => new Option(p.Id, p.Name)).ToListAsync();
                    if (partnerToSelect is Guid partner)
                    {
                        var selected = await _session.SelectAsync(genealogy, partner, DraftLeaveChoice.Discard);
                        if (selected != DraftTransitionResult.Completed) throw new InvalidOperationException(_session.Error ?? "无法重新选择配偶所在人物。");
                        _person = partner;
                    }
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
            else if (result == DraftTransitionResult.Completed) _status.Text = "没有需要保存的修改。";
            ShowDraft();
        }
        catch (Exception ex) { _status.Text = $"保存失败：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    internal async Task<bool> PrepareForOwnerExitAsync()
    {
        if (BlockWhileInlineEditing()) return false;
        if (_approvedClose) return true;
        if (_busy) { _status.Text = "请等待当前操作结束后再关闭。"; return false; }
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
}
