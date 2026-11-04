using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;
using PersonRow = ZupuPaiBan.Data.ProjectionReadSession.PersonRow;
using ChildCandidate = ZupuPaiBan.Data.ProjectionReadSession.ChildCandidate;
using ChartRow = ZupuPaiBan.Data.ProjectionReadSession.ChartRow;

namespace ZupuPaiBan.Views;

/// <summary>Read-only, explicitly assembled single-branch view. No genealogy data is written.</summary>
public sealed class RealHorizontalLineageWindow : Window
{
    private sealed record Node(PersonRow Person, int Column, Guid? Parent, Guid? Edge, int Order, bool Derived);
    private sealed record PersonOption(PersonRow Value) { public string Label => $"{Value.Name}｜{Value.Id}｜谱载世代 {Value.RecordedGeneration ?? "未记"}"; }
    private sealed record ChildOption(ChildCandidate Value) { public string Label => $"{Value.ChildName}｜{Value.ChildId}｜{RelationshipLabel(Value.Kind)}｜关系 {Value.RelationshipId}｜排行 {Value.RecordedBirthOrder ?? "未记"}"; }
    private sealed record ChartOption(ChartRow Value) { public string Label => Value.Id == Guid.Empty ? Value.Name : $"{Value.Name}｜{Value.Id}"; }
    private readonly TargetContextCoordinator _coordinator;
    private readonly Guid _genealogyId;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _monitor = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<Guid, Node> _nodes = new();
    private readonly Dictionary<string, int> _generationMap = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _expanded = new();
    private readonly ListBox _people = new() { Height = 115, DisplayMemberPath = nameof(PersonOption.Label) };
    private readonly ListBox _candidates = new() { Height = 115, DisplayMemberPath = nameof(ChildOption.Label) };
    private readonly ListBox _partners = new() { Height = 65, DisplayMemberPath = nameof(PersonOption.Label) };
    private readonly ComboBox _charts = new() { Width = 300, DisplayMemberPath = nameof(ChartOption.Label) };
    private readonly CheckBox _allowDerived = new() { Content = "谱载世代缺失时，允许按父子链临时推导", IsChecked = false, Margin = new Thickness(0, 5, 0, 0) };
    private readonly TextBox _column = new() { Width = 55, Text = "0" };
    private readonly TextBox _search = new() { Width = 185, MaxLength = 200 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly Canvas _canvas = new() { Background = Brushes.WhiteSmoke };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ScaleTransform _scale = new(1, 1);
    private ProjectionReadSession? _session;
    private Guid _token;
    private Guid? _peopleCursor;
    private Guid? _childCursor;
    private Guid? _chartCursor;
    private Guid? _detailPerson;
    private Guid? _parentDetailCursor;
    private Guid? _unionDetailCursor;
    private Guid? _otherDetailCursor;
    private Guid? _selected;
    private string? _searchQuery;
    private int _epoch;
    private int _rootChoiceVersion;
    private int _candidateChoiceVersion;
    private int _partnerChoiceVersion;
    private int _searchInputVersion;
    private int _mappingVersion;
    private bool _busy;
    private bool _invalid;
    private bool _closed;
    private Point? _dragStart;
    private Point _scrollStart;
    private bool _dragged;
    internal Func<ProjectionReadSession, Task>? BeforeApplyForTest { get; set; }
    internal ProjectionReadSession? SessionForTest => _session;
    internal int VisibleNodeCount => _nodes.Count;
    internal string StatusForTest => _status.Text;
    internal bool IsInvalidForTest => _invalid;
    internal int PeopleCountForTest => _people.Items.Count;
    internal int CanvasCountForTest => _canvas.Children.Count;
    internal string DetailsForTest => _details.Text;
    internal int PartnerCountForTest => _partners.Items.Count;
    internal Guid? SelectedForTest => _selected;
    internal bool IsPathHighlightedForTest(Guid id) => _canvas.Children.OfType<Border>().Single(item => item.Tag is Guid value && value == id).Background == Brushes.LemonChiffon;
    internal async Task SearchForTest(string value) { _search.Text = value; await SearchAsync(); }
    internal void ChangeSearchTextForTest(string value) => _search.Text = value;
    internal void ChangeColumnForTest(int value) => _column.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal async Task LocateForTest(Guid id)
    {
        _people.SelectedItem = _people.Items.OfType<PersonOption>().Single(item => item.Value.Id == id);
        await LocateSelectedAsync();
    }
    internal int VisibleCardsForTest => _canvas.Children.OfType<Border>().Count();
    internal double CenterYForTest(Guid id)
    {
        var card = _canvas.Children.OfType<Border>().Single(item => item.Tag is Guid value && value == id);
        return Canvas.GetTop(card) + card.Height / 2;
    }
    internal void StopMonitorForTest() => _monitor.Stop();
    internal void StartMonitorForTest() => _monitor.Start();
    internal void SelectChartForTest(Guid id) => _charts.SelectedItem = _charts.Items.OfType<ChartOption>().Single(item => item.Value.Id == id);
    internal void SelectPersonForTest(Guid id, int column)
    {
        _people.SelectedItem = _people.Items.OfType<PersonOption>().Single(item => item.Value.Id == id);
        _column.Text = column.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    internal void SelectChildForTest(Guid relationshipId, int column, bool allowDerived)
    {
        _candidates.SelectedItem = _candidates.Items.OfType<ChildOption>().Single(item => item.Value.RelationshipId == relationshipId);
        _column.Text = column.ToString(System.Globalization.CultureInfo.InvariantCulture); _allowDerived.IsChecked = allowDerived;
    }
    internal void CancelForTest() => Invalidate("已取消，请关闭后重新打开。");
    internal async Task MovePersonForTest(Guid id, int direction) { _selected = id; await MoveSelectedAsync(direction); }
    internal Task ToggleForTest(Guid id) => ToggleAsync(id);
    internal Task SelectNodeForTest(Guid id, bool expand) => SelectNodeAsync(id, expand);

    public RealHorizontalLineageWindow(TargetContextCoordinator coordinator, Guid genealogyId)
    {
        _coordinator = coordinator; _genealogyId = genealogyId;
        Title = "横向世系图 — 当前族谱（只读）"; Width = 1280; Height = 760; MinWidth = 900; MinHeight = 600;
        var outer = new DockPanel();
        var left = new StackPanel { Width = 340, Margin = new Thickness(8) };
        left.Children.Add(new TextBlock { Text = "人物（分页顺序不是谱载排行）", FontWeight = FontWeights.SemiBold });
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal };
        searchRow.Children.Add(_search); searchRow.Children.Add(Button("搜索", async () => await SearchAsync()));
        searchRow.Children.Add(Button("全部", async () => { if (!CanStart()) return; _searchQuery = null; await LoadPeopleAsync(true); })); left.Children.Add(searchRow);
        left.Children.Add(_people);
        left.Children.Add(Button("加载更多人物", async () => await LoadPeopleAsync(false)));
        left.Children.Add(Button("定位已加载人物", async () => await LocateSelectedAsync()));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "确认世代列：", VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(_column);
        row.Children.Add(Button("选为起点", async () => await SetRootAsync())); left.Children.Add(row);
        left.Children.Add(_allowDerived);
        left.Children.Add(new TextBlock { Text = "选择图表（可留空；仅供复核）", Margin = new Thickness(0, 8, 0, 2) }); left.Children.Add(_charts);
        left.Children.Add(Button("加载更多图表", async () => await LoadChartsAsync(false)));
        left.Children.Add(new TextBlock { Text = "当前人物的下系候选（未排序）", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        left.Children.Add(_candidates);
        left.Children.Add(Button("加载当前人物子女候选", async () => await LoadChildrenAsync(true)));
        left.Children.Add(Button("加载更多候选", async () => await LoadChildrenAsync(false)));
        left.Children.Add(Button("按所选顺序加入子女关系", async () => await AddSelectedChildAsync()));
        var reorder = new StackPanel { Orientation = Orientation.Horizontal };
        reorder.Children.Add(Button("上移所选人物", () => MoveSelectedAsync(-1)));
        reorder.Children.Add(Button("下移所选人物", () => MoveSelectedAsync(1)));
        left.Children.Add(reorder);
        left.Children.Add(new TextBlock { Text = "请按确认顺序逐项加入；列号须由您确认。仅显示手工加入的关系，其余候选保留在列表。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        left.Children.Add(Button("取消并清除", () => { Invalidate("已取消，请关闭后重新打开。"); return Task.CompletedTask; }));
        left.Children.Add(_details);
        left.Children.Add(Button("加载更多关系详情", async () => { if (_selected is Guid id) await LoadDetailsAsync(id, true); }));
        left.Children.Add(new TextBlock { Text = "已知配偶（选择后可定位）", Margin = new Thickness(0, 6, 0, 0) });
        left.Children.Add(_partners);
        left.Children.Add(Button("定位所选配偶", async () => await LocatePartnerAsync()));
        DockPanel.SetDock(left, Dock.Left); outer.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        DockPanel.SetDock(_status, Dock.Bottom); outer.Children.Add(_status);
        _canvas.LayoutTransform = _scale; _scroll.Content = _canvas;
        _scroll.PreviewMouseWheel += OnWheel;
        _canvas.MouseLeftButtonDown += CanvasDown; _canvas.MouseMove += CanvasMove; _canvas.MouseLeftButtonUp += CanvasUp;
        _canvas.LostMouseCapture += (_, _) => _dragStart = null;
        outer.Children.Add(_scroll); Content = outer;
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) => { _closed = true; _monitor.Stop(); Invalidate("窗口已关闭。"); };
        _monitor.Tick += async (_, _) => await PollAsync();
        _people.SelectionChanged += (_, _) => _rootChoiceVersion++;
        _candidates.SelectionChanged += (_, _) => _candidateChoiceVersion++;
        _partners.SelectionChanged += (_, _) => _partnerChoiceVersion++;
        _search.TextChanged += (_, _) => _searchInputVersion++;
        _column.TextChanged += (_, _) => _mappingVersion++;
        _allowDerived.Checked += (_, _) => _mappingVersion++;
        _allowDerived.Unchecked += (_, _) => _mappingVersion++;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && CanStart()) { ClearSelection(); e.Handled = true; } };
        _status.Text = "正在打开只读会话……";
    }
    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(5, 3, 5, 3) };
        button.Click += async (_, _) => await action(); return button;
    }
    private static string RelationshipLabel(PersonRelationshipKind kind) => kind switch
    {
        PersonRelationshipKind.BiologicalParent => "生物父母",
        PersonRelationshipKind.AdoptiveParent => "养父母",
        PersonRelationshipKind.HeirParent => "继嗣父母",
        PersonRelationshipKind.Spouse => "配偶关系",
        PersonRelationshipKind.LostAncestry => "上系失考",
        _ => "未知关系"
    };
    private static string UnionLabel(FamilyUnionKind kind) => kind switch
    {
        FamilyUnionKind.Marriage => "婚配",
        FamilyUnionKind.Partnership => "伴侣",
        _ => "类型未详"
    };
    internal async Task InitializeAsync()
    {
        if (_session is not null || _invalid) return;
        var epoch = _epoch;
        try
        {
            var session = await Task.Run(() => ProjectionReadSession.OpenAsync(_coordinator, _genealogyId, _lifetime.Token));
            if (!Current(epoch)) { await session.DisposeAsync(); return; }
            _session = session; _token = session.Token;
            await LoadChartsAsync(true);
            if (!Current(epoch)) return;
            _charts.SelectionChanged += (_, _) => ClearGraphForChartChange();
            await LoadPeopleAsync(true);
            if (Current(epoch)) _monitor.Start();
        }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"无法读取族谱：{ex.Message}"); }
    }
    private async Task LoadChartsAsync(bool first)
    {
        if (!CanStart() || (!first && _chartCursor is null)) return;
        var epoch = _epoch; _busy = true;
        try
        {
            var charts = await Task.Run(() => _session!.ReadChartsAsync(_token, first ? null : _chartCursor, 50, _lifetime.Token));
            if (!Current(epoch)) return;
            await Task.Run(() => _session!.ValidateAsync(_token, _lifetime.Token));
            if (!Current(epoch)) return;
            if (first) { _charts.Items.Clear(); _charts.Items.Add(new ChartOption(new ChartRow(Guid.Empty, "不使用图表", 0, 0))); }
            foreach (var chart in charts.Items) _charts.Items.Add(new ChartOption(chart));
            _chartCursor = charts.NextCursor;
            if (first) _charts.SelectedIndex = 0;
            if (_chartCursor is not null) _status.Text = "还有更多图表，可继续加载。";
        }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"读取图表失败：{ex.Message}"); }
        finally { _busy = false; }
    }
    private void ClearGraphForChartChange()
    {
        if (_invalid || _closed) return;
        _epoch++; _nodes.Clear(); _expanded.Clear(); _generationMap.Clear(); _selected = null; _childCursor = null;
        _canvas.Children.Clear(); _candidates.Items.Clear(); _details.Text = ""; _partners.Items.Clear();
        _status.Text = "图表已更换，请重新选择起点。";
    }
    internal async Task LoadPeopleAsync(bool first)
    {
        if (!CanStart()) return;
        if (!first && _peopleCursor is null) { _status.Text = "没有更多人物。"; return; }
        var epoch = _epoch; var search = _searchQuery; var searchVersion = _searchInputVersion; _busy = true;
        try
        {
            var page = search is null
                ? await Task.Run(() => _session!.ReadPeopleAsync(_token, first ? null : _peopleCursor, 50, _lifetime.Token))
                : await Task.Run(async () => { var found = await _session!.SearchPeopleAsync(_token, search, first ? null : _peopleCursor, 50, _lifetime.Token); return new ProjectionReadSession.PersonPage(found.Items, found.NextCursor); });
            if (BeforeApplyForTest is not null) await BeforeApplyForTest(_session!);
            if (!Current(epoch)) return;
            if (search is not null && searchVersion != _searchInputVersion) { _people.Items.Clear(); _peopleCursor = null; _status.Text = "搜索内容已变化，请重新搜索。"; return; }
            await Task.Run(() => _session!.ValidateAsync(_token, _lifetime.Token));
            if (!Current(epoch)) return;
            if (search is not null && searchVersion != _searchInputVersion) { _people.Items.Clear(); _peopleCursor = null; _status.Text = "搜索内容已变化，请重新搜索。"; return; }
            if (first) _people.Items.Clear();
            foreach (var person in page.Items) _people.Items.Add(new PersonOption(person));
            _peopleCursor = page.NextCursor;
            _status.Text = page.NextCursor is null ? "人物已加载完；请选择起点并明确列号。" : "请选择起点；可继续加载更多人物。";
        }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"数据已失效或读取失败：{ex.Message}"); }
        finally { _busy = false; }
    }
    private async Task SearchAsync()
    {
        if (!CanStart()) return;
        var query = _search.Text.Trim();
        if (query.Length == 0) { _status.Text = "请输入姓名或人物 ID。"; return; }
        _searchQuery = query; await LoadPeopleAsync(true);
    }
    private async Task LocateSelectedAsync()
    {
        if (!CanStart() || _people.SelectedItem is not PersonOption person) return;
        await LocatePersonAsync(person.Value, false);
    }
    private async Task LocatePartnerAsync()
    {
        if (!CanStart() || _partners.SelectedItem is not PersonOption person) return;
        await LocatePersonAsync(person.Value, true);
    }
    private async Task LocatePersonAsync(PersonRow person, bool partnerSelection)
    {
        var epoch = _epoch; var selectionVersion = partnerSelection ? _partnerChoiceVersion : _rootChoiceVersion; _busy = true;
        try
        {
            await Task.Run(() => _session!.ValidateAsync(_token, _lifetime.Token));
            if (!Current(epoch) || selectionVersion != (partnerSelection ? _partnerChoiceVersion : _rootChoiceVersion)) return;
            if (!_nodes.ContainsKey(person.Id))
            {
                var option = _people.Items.OfType<PersonOption>().FirstOrDefault(p => p.Value.Id == person.Id);
                if (option is null) { option = new PersonOption(person); _people.Items.Add(option); }
                _people.SelectedItem = option; _status.Text = "该人物尚未加载到当前图；可选为起点查看。"; return;
            }
            var node = _nodes[person.Id];
            for (var ancestor = node.Parent; ancestor is Guid parent; ancestor = _nodes[parent].Parent) _expanded.Add(parent);
            _selected = node.Person.Id; Render(); _canvas.UpdateLayout();
            var card = _canvas.Children.OfType<Border>().Single(item => item.Tag is Guid id && id == node.Person.Id);
            _scroll.ScrollToHorizontalOffset(Math.Max(0, Canvas.GetLeft(card) * _scale.ScaleX - _scroll.ViewportWidth / 2));
            _scroll.ScrollToVerticalOffset(Math.Max(0, Canvas.GetTop(card) * _scale.ScaleY - _scroll.ViewportHeight / 2));
        }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"定位失败：{ex.Message}"); }
        finally { _busy = false; }
        if (Current(epoch) && _selected == person.Id)
        {
            await LoadDetailsAsync(person.Id);
            if (Current(epoch) && _selected == person.Id) await LoadChildrenAsync(true);
        }
    }
    internal async Task SetRootAsync()
    {
        if (!CanStart() || _people.SelectedItem is not PersonOption selected) return;
        var person = selected.Value;
        if (!int.TryParse(_column.Text, out var column) || column < 0) { _status.Text = "请输入非负世代列号。"; return; }
        var epoch = _epoch; var selectionVersion = _rootChoiceVersion; var mappingVersion = _mappingVersion; _busy = true;
        try
        {
            await Task.Run(() => _session!.ValidateAsync(_token, _lifetime.Token));
            if (!Current(epoch) || selectionVersion != _rootChoiceVersion || mappingVersion != _mappingVersion) return;
            _nodes.Clear(); _expanded.Clear(); _generationMap.Clear();
            if (!string.IsNullOrWhiteSpace(person.RecordedGeneration)) _generationMap.Add(person.RecordedGeneration, column);
            _nodes.Add(person.Id, new Node(person, column, null, null, 0, false)); _expanded.Add(person.Id);
            _selected = person.Id; _candidates.Items.Clear(); _childCursor = null; Render();
            _status.Text = "已明确起点及列；不同断开分支的同代关系未确认。";
        }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"数据已失效：{ex.Message}"); }
        finally { _busy = false; }
        if (Current(epoch) && _selected == person.Id)
        {
            await LoadDetailsAsync(person.Id);
            if (Current(epoch) && _selected == person.Id) await LoadChildrenAsync(true);
        }
    }
    internal async Task LoadChildrenAsync(bool first)
    {
        if (!CanStart() || _selected is not Guid parent) return;
        if (!first && _childCursor is null) { _status.Text = "没有更多候选。"; return; }
        var epoch = _epoch; var selectionVersion = _candidateChoiceVersion; _busy = true;
        try
        {
            var page = await Task.Run(() => _session!.ReadChildCandidatesAsync(_token, parent, first ? null : _childCursor, 50, _lifetime.Token));
            if (!Current(epoch) || _selected != parent || selectionVersion != _candidateChoiceVersion) return;
            await Task.Run(() => _session!.ValidateAsync(_token, _lifetime.Token));
            if (!Current(epoch) || _selected != parent || selectionVersion != _candidateChoiceVersion) return;
            if (first) _candidates.Items.Clear();
            foreach (var child in page.Items) _candidates.Items.Add(new ChildOption(child));
            _childCursor = page.NextCursor;
            _status.Text = page.NextCursor is null ? "候选已加载完；请按确认的同胞顺序选择关系。" : "候选尚有更多；列表顺序不是排行。";
        }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"读取失败：{ex.Message}"); }
        finally { _busy = false; }
    }
    internal async Task AddSelectedChildAsync()
    {
        if (!CanStart() || _selected is not Guid parent || _candidates.SelectedItem is not ChildOption selected) return;
        var child = selected.Value;
        if (!int.TryParse(_column.Text, out var column) || column != _nodes[parent].Column + 1)
        { _status.Text = "请确认子女的世代列为父母列加一；跨代冲突保留为待处理，未绘制。"; return; }
        if (_nodes.ContainsKey(child.ChildId)) { _status.Text = "人物已在图中；复杂关系待处理，未创建重复人物。"; return; }
        if (string.IsNullOrWhiteSpace(child.RecordedGeneration) && _allowDerived.IsChecked != true)
        { _status.Text = "该人物缺少谱载世代；请先明确允许按父子链临时推导。"; return; }
        if (!string.IsNullOrWhiteSpace(child.RecordedGeneration) && _generationMap.TryGetValue(child.RecordedGeneration, out var mapped) && mapped != column)
        { _status.Text = "该谱载世代已映射到其他列；请复核世代冲突，未绘制。"; return; }
        var epoch = _epoch; var selectionVersion = _candidateChoiceVersion; var mappingVersion = _mappingVersion; _busy = true;
        try
        {
            // Show every parent option to the user through diagnostics; selecting this child is an explicit session choice.
            var parents = await Task.Run(() => _session!.ReadParentCandidatesAsync(_token, child.ChildId, null, 50, _lifetime.Token));
            if (!Current(epoch) || _selected != parent || selectionVersion != _candidateChoiceVersion || mappingVersion != _mappingVersion) return;
            _details.Text = "父母候选（保留全部原关系）：" + Environment.NewLine + string.Join(Environment.NewLine,
                parents.Items.Select(p => $"{p.ParentName}｜人物 {p.ParentId}｜{RelationshipLabel(p.Kind)}｜关系 {p.RelationshipId}｜来源 {p.Source ?? "未记"}｜备注 {p.Note ?? "未记"}"));
            if (parents.NextCursor is not null) { _status.Text = "父母候选超过 50 项，请先处理关系；未绘制。"; return; }
            if (!parents.Items.Any(p => p.RelationshipId == child.RelationshipId && p.ParentId == parent))
            { _status.Text = "所选关系已失效，未绘制。"; return; }
            if (_charts.SelectedItem is ChartOption selectedChart && selectedChart.Value is { Id: var chartId } && chartId != Guid.Empty)
            {
                var choice = await Task.Run(() => _session!.ReadChartChoiceAsync(_token, chartId, child.ChildId, _lifetime.Token));
                if (!Current(epoch) || _selected != parent || selectionVersion != _candidateChoiceVersion || mappingVersion != _mappingVersion) return;
                if (choice is { NeedsReview: true })
                {
                    _details.Text += $"{Environment.NewLine}图表历史选线：{choice.PersonName}（{choice.PersonId}）→ {choice.ParentName}（{choice.ParentId}）；关系 {choice.RelationshipId}；{choice.Problem}";
                    _status.Text = "图表选线需复核；请先在图表中处理，或明确选择不使用图表。"; return;
                }
                if (choice?.ApplicableRelationshipId is Guid chosen && chosen != child.RelationshipId)
                { _status.Text = "所选关系与图表选线不同；请选择图表所指定的关系，或明确选择不使用图表。"; return; }
            }
            var person = await Task.Run(() => _session!.ReadPersonAsync(_token, child.ChildId, _lifetime.Token));
            if (!Current(epoch) || _selected != parent || selectionVersion != _candidateChoiceVersion || mappingVersion != _mappingVersion) return;
            await Task.Run(() => _session!.ValidateAsync(_token, _lifetime.Token));
            if (!Current(epoch) || _selected != parent || selectionVersion != _candidateChoiceVersion || mappingVersion != _mappingVersion) return;
            var order = _nodes.Values.Count(n => n.Parent == parent);
            if (!string.IsNullOrWhiteSpace(person.RecordedGeneration)) _generationMap.TryAdd(person.RecordedGeneration, column);
            _nodes.Add(person.Id, new Node(person, column, parent, child.RelationshipId, order, string.IsNullOrWhiteSpace(person.RecordedGeneration))); _expanded.Add(person.Id);
            Render();
            _status.Text = parents.Items.Count > 1 ? "已按您的选择临时选线；还有其他父母候选，原关系未改变。" : "已按您的顺序加入；谱载资料未改变。";
        }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"读取失败：{ex.Message}"); }
        finally { _busy = false; }
    }
    private async Task LoadDetailsAsync(Guid id, bool append = false)
    {
        if (!CanStart()) return;
        if (append && (_detailPerson != id || (_parentDetailCursor is null && _unionDetailCursor is null && _otherDetailCursor is null)))
        { _status.Text = "没有更多关系详情。"; return; }
        if (!append)
        {
            _details.Text = ""; _partners.Items.Clear(); _detailPerson = id;
            _parentDetailCursor = _unionDetailCursor = _otherDetailCursor = null;
        }
        var epoch = _epoch; _busy = true;
        try
        {
            var person = _nodes[id].Person;
            var parents = !append || _parentDetailCursor is not null ? await Task.Run(() => _session!.ReadParentCandidatesAsync(_token, id, append ? _parentDetailCursor : null, 20, _lifetime.Token)) : null;
            var unions = !append || _unionDetailCursor is not null ? await Task.Run(() => _session!.ReadUnionsAsync(_token, id, append ? _unionDetailCursor : null, 20, _lifetime.Token)) : null;
            var others = !append || _otherDetailCursor is not null ? await Task.Run(() => _session!.ReadOtherRelationshipsAsync(_token, id, append ? _otherDetailCursor : null, 20, _lifetime.Token)) : null;
            var lines = append ? new List<string>() : new List<string> { $"{person.Name}  ID: {person.Id}", $"谱载世代：{person.RecordedGeneration ?? "未记"}；排行：{person.RecordedBirthOrder ?? "未记"}" };
            var partners = new List<PersonRow>();
            if (parents is not null) foreach (var parent in parents.Items)
                lines.Add($"父母候选 {parent.RelationshipId}：{parent.ParentName}（{RelationshipLabel(parent.Kind)}；来源 {parent.Source ?? "未记"}；备注 {parent.Note ?? "未记"}）");
            if (unions is not null) foreach (var union in unions.Items)
            {
                var partner = union.PartnerOneId == id ? union.PartnerTwoId : union.PartnerOneId;
                var partnerRow = partner is Guid p ? await Task.Run(() => _session!.ReadPersonAsync(_token, p, _lifetime.Token)) : null;
                if (partnerRow is not null) partners.Add(partnerRow);
                var name = partnerRow?.Name ?? "未详";
                lines.Add($"{UnionLabel(union.Kind)} {union.Id}：{name}（起 {union.Start ?? "未记"}；止 {union.End ?? "未记"}；备注 {union.Note ?? "未记"}）");
            }
            if (others is not null) foreach (var rel in others.Items) lines.Add($"原关系 {rel.Id}：{RelationshipLabel(rel.Kind)}，主体 {rel.PersonId}，对方 {rel.RelatedPersonId?.ToString() ?? "未详"}；来源 {rel.Source ?? "未记"}；备注 {rel.Note ?? "未记"}");
            await Task.Run(() => _session!.ValidateAsync(_token, _lifetime.Token));
            if (Current(epoch) && _selected == id)
            {
                if (!append) { _details.Text = ""; _partners.Items.Clear(); }
                _detailPerson = id;
                _parentDetailCursor = parents?.NextCursor; _unionDetailCursor = unions?.NextCursor; _otherDetailCursor = others?.NextCursor;
                _details.Text += (_details.Text.Length == 0 ? "" : Environment.NewLine) + string.Join(Environment.NewLine, lines);
                foreach (var partner in partners) _partners.Items.Add(new PersonOption(partner));
                if (_parentDetailCursor is not null || _unionDetailCursor is not null || _otherDetailCursor is not null) _status.Text = "还有更多关系，可继续加载。";
            }
        }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"读取详情失败：{ex.Message}"); }
        finally { _busy = false; }
    }
    private void Render()
    {
        _canvas.Children.Clear();
        if (_nodes.Count == 0) return;
        var people = _nodes.Values.Select(n => new HorizontalLayoutPerson(n.Person.Id, n.Column, n.Order, 180, 70)).ToArray();
        var edges = _nodes.Values.Where(n => n.Parent.HasValue).Select(n => new HorizontalLayoutRelationship(n.Edge!.Value, n.Parent!.Value, n.Person.Id)).ToArray();
        var layout = HorizontalLineageLayout.Calculate(people, edges, _expanded);
        var path = new HashSet<Guid>();
        for (var current = _selected; current is Guid id && _nodes.TryGetValue(id, out var item); current = item.Parent) path.Add(id);
        foreach (var edge in layout.Edges)
        {
            var line = new Polyline { Stroke = path.Contains(edge.ParentId) && path.Contains(edge.ChildId) ? Brushes.DarkOrange : Brushes.SlateGray, StrokeThickness = 1.5, IsHitTestVisible = false };
            foreach (var point in edge.Points) line.Points.Add(new Point(point.X + 30, point.Y + 30)); _canvas.Children.Add(line);
        }
        foreach (var node in layout.Nodes)
        {
            var item = _nodes[node.PersonId];
            var card = new Border { Width = node.Width, Height = node.Height, Background = path.Contains(node.PersonId) ? Brushes.LemonChiffon : Brushes.White, BorderBrush = node.PersonId == _selected ? Brushes.SteelBlue : Brushes.Gray,
                BorderThickness = new Thickness(node.PersonId == _selected ? 2 : 1), Padding = new Thickness(6), Tag = node.PersonId };
            var panel = new DockPanel();
            if (_nodes.Values.Any(n => n.Parent == node.PersonId))
            {
                var toggle = new Button { Content = _expanded.Contains(node.PersonId) ? "−" : "+", Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
                toggle.Click += async (_, e) => { e.Handled = true; await ToggleAsync(node.PersonId); };
                DockPanel.SetDock(toggle, Dock.Right); panel.Children.Add(toggle);
            }
            var label = item.Parent is null && string.IsNullOrWhiteSpace(item.Person.RecordedGeneration) ? "（相对起点）" : item.Derived ? "（推导）" : "（已确认）";
            card.ToolTip = $"{item.Person.Name}\n人物 ID：{item.Person.Id}\n列 {item.Column}{label}\n谱载世代：{item.Person.RecordedGeneration ?? "未记"}\n谱载排行：{item.Person.RecordedBirthOrder ?? "未记"}";
            panel.Children.Add(new TextBlock { Text = $"{item.Person.Name}\n列 {item.Column}{label}；谱载：{item.Person.RecordedGeneration ?? "未记"}\n排行：{item.Person.RecordedBirthOrder ?? "未记"}", TextTrimming = TextTrimming.CharacterEllipsis });
            card.Child = panel;
            card.MouseLeftButtonDown += async (_, e) => { e.Handled = true; await SelectNodeAsync(node.PersonId, true); };
            card.MouseRightButtonDown += async (_, e) =>
            {
                e.Handled = true; await SelectNodeAsync(node.PersonId, false);
                if (_selected != node.PersonId || _invalid || _closed) return;
                var menu = new ContextMenu(); menu.Items.Add(new MenuItem { Header = "当前视图只读", IsEnabled = false });
                menu.PlacementTarget = _canvas.Children.OfType<Border>().FirstOrDefault(item => item.Tag is Guid value && value == node.PersonId) ?? (UIElement)_canvas;
                menu.IsOpen = true;
            };
            Canvas.SetLeft(card, node.X + 30); Canvas.SetTop(card, node.Y + 30); _canvas.Children.Add(card);
        }
        _canvas.Width = layout.Nodes.Max(n => n.X + n.Width) + 60; _canvas.Height = layout.Nodes.Max(n => n.Y + n.Height) + 60;
    }
    private async Task SelectNodeAsync(Guid id, bool expand)
    {
        if (!CanStart() || !_nodes.ContainsKey(id)) return;
        var epoch = _epoch; _busy = true;
        try
        {
            await Task.Run(() => _session!.ValidateAsync(_token, _lifetime.Token));
            if (!Current(epoch)) return;
            _selected = id; if (expand) _expanded.Add(id); _candidates.Items.Clear(); _childCursor = null; _details.Text = ""; _partners.Items.Clear(); Render();
        }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"数据已失效：{ex.Message}"); }
        finally { _busy = false; }
        if (Current(epoch) && _selected == id)
        {
            await LoadDetailsAsync(id);
            if (expand && Current(epoch) && _selected == id) await LoadChildrenAsync(true);
        }
    }
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var position = e.GetPosition(_scroll); var old = _scale.ScaleX;
        var next = Math.Clamp(old * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.4, 2.5);
        if (next == old) return;
        var x = (_scroll.HorizontalOffset + position.X) / old; var y = (_scroll.VerticalOffset + position.Y) / old;
        _scale.ScaleX = _scale.ScaleY = next; _scroll.UpdateLayout();
        _scroll.ScrollToHorizontalOffset(x * next - position.X); _scroll.ScrollToVerticalOffset(y * next - position.Y);
        e.Handled = true;
    }
    private void CanvasDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanStart()) return;
        _dragStart = e.GetPosition(_scroll); _scrollStart = new Point(_scroll.HorizontalOffset, _scroll.VerticalOffset); _dragged = false;
        _canvas.CaptureMouse(); e.Handled = true;
    }
    private void CanvasMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(_scroll) - start;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 4) _dragged = true;
        if (_dragged) { _scroll.ScrollToHorizontalOffset(_scrollStart.X - delta.X); _scroll.ScrollToVerticalOffset(_scrollStart.Y - delta.Y); }
    }
    private void CanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null) return;
        var clear = !_dragged; _dragStart = null; _canvas.ReleaseMouseCapture();
        if (clear) ClearSelection(); e.Handled = true;
    }
    private void ClearSelection()
    {
        _selected = null; _candidates.Items.Clear(); _childCursor = null; _details.Text = ""; _partners.Items.Clear(); Render();
    }
    private async Task ToggleAsync(Guid id)
    {
        if (!CanStart() || !_nodes.ContainsKey(id)) return;
        var epoch = _epoch; _busy = true;
        try
        {
            await Task.Run(() => _session!.ValidateAsync(_token, _lifetime.Token));
            if (!Current(epoch)) return;
            if (!_expanded.Remove(id)) _expanded.Add(id);
            _selected = id; _candidates.Items.Clear(); _childCursor = null; _details.Text = ""; _partners.Items.Clear(); Render();
        }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"数据已失效：{ex.Message}"); }
        finally { _busy = false; }
    }
    private async Task MoveSelectedAsync(int direction)
    {
        if (!CanStart() || _selected is not Guid id || !_nodes.TryGetValue(id, out var selected) || selected.Parent is null) return;
        var siblings = _nodes.Values.Where(n => n.Parent == selected.Parent).OrderBy(n => n.Order).ToArray();
        var index = Array.FindIndex(siblings, n => n.Person.Id == id); var target = index + direction;
        if (target < 0 || target >= siblings.Length) return;
        var epoch = _epoch; _busy = true;
        try
        {
            await Task.Run(() => _session!.ValidateAsync(_token, _lifetime.Token));
            if (!Current(epoch) || _selected != id) return;
            var other = siblings[target];
            _nodes[id] = selected with { Order = other.Order }; _nodes[other.Person.Id] = other with { Order = selected.Order };
            Render(); _status.Text = "已调整本会话显示顺序，未修改谱载排行。";
        }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"数据已失效：{ex.Message}"); }
        finally { _busy = false; }
    }
    internal async Task PollAsync()
    {
        if (!CanStart()) return;
        var epoch = _epoch; _busy = true;
        try { await Task.Run(() => _session!.ValidateAsync(_token, _lifetime.Token)); }
        catch (OperationCanceledException) { if (Current(epoch)) Invalidate("操作已取消，请重新打开。"); }
        catch (Exception ex) { if (Current(epoch)) Invalidate($"数据已变化，请重新打开：{ex.Message}"); }
        finally { _busy = false; }
    }
    private bool Current(int epoch) => !_closed && !_invalid && epoch == _epoch && !_lifetime.IsCancellationRequested;
    private bool CanStart() => !_busy && !_closed && !_invalid && _session is not null;
    public void InvalidateForSelectionChange() => Invalidate("族谱已切换，请关闭后重新打开。");
    public void InvalidateForShutdown() => Invalidate("程序正在退出，内容已清除。");
    private void Invalidate(string message)
    {
        if (_invalid) return;
        _invalid = true; _epoch++; _monitor.Stop(); _lifetime.Cancel();
        _nodes.Clear(); _expanded.Clear(); _generationMap.Clear(); _selected = null; _people.Items.Clear(); _candidates.Items.Clear(); _charts.Items.Clear(); _partners.Items.Clear(); _details.Text = ""; _canvas.Children.Clear();
        _status.Text = message;
        if (_session is not null) _ = _session.DisposeAsync().AsTask();
    }
}
