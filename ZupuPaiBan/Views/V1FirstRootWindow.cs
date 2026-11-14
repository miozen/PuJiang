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
    private readonly Guid _genealogy, _lineage;
    private readonly string _surname;
    private readonly Canvas _canvas = new() { Background = Brushes.White, MinWidth = 650, MinHeight = 360 };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TextBlock _status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _details = new() { Text = "未选中人物", TextWrapping = TextWrapping.Wrap };
    private readonly Button _create = new() { Content = "添加首节点", Padding = new Thickness(6) };
    private readonly Button _empty = new() { Content = "添加空节点", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
    private readonly Button _reload = new() { Content = "重新读取", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
    private readonly Button _retry = new() { Content = "重试保存", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _addChild = new() { Content = "添加子女", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
    private readonly Button _addChildEmpty = new() { Content = "添加空子女", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
    private readonly Button _addSpouse = new() { Content = "添加配偶", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
    private readonly StackPanel _familyDetails = new();
    private TextBox? _input;
    private StackPanel? _inputHost;
    private AddFirstRoot? _pending;
    private AddChild? _pendingChild;
    private AddSpouse? _pendingSpouse;
    private SetChildMother? _pendingMother;
    private UpdatePersonProfile? _pendingProfile;
    private bool _profileDirty;
    private PersonSex? _selectedSex;
    private int _detailsVersion;
    private enum InputKind { Root, Child, Spouse }
    private InputKind _inputKind;
    private IReadOnlyList<RootNode> _rootNodes = Array.Empty<RootNode>();
    private IReadOnlyList<ChildNode> _childNodes = Array.Empty<ChildNode>();
    private IReadOnlyList<SpouseNode> _visibleSpouses = Array.Empty<SpouseNode>();
    private Point? _draftLocation;
    private readonly Guid _draftId = Guid.NewGuid();
    private bool HasPending => _pending is not null || _pendingChild is not null || _pendingSpouse is not null || _pendingMother is not null || _pendingProfile is not null;
    private bool _saving, _uncertain, _hasRoot, _loadedOk;
    private Guid? _personId, _placementId;
    private Point? _panStart;
    private Point _panOffset;
    private bool _dragged;
    private double _contentWidth = 650, _contentHeight = 360;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal V1FirstRootWindow(FirstRootApplication app, Guid genealogy, Guid lineage, string surname, ChildApplication? children = null, SpouseApplication? spouses = null, PersonProfileApplication? profiles = null)
    {
        _app = app; _genealogy = genealogy; _lineage = lineage; _surname = surname;
        _children = children; _spouses = spouses; _profiles = profiles;
        Title = "世系编修工作台 — V1 虚构隔离样本"; Width = 1100; Height = 720; MinWidth = 760; MinHeight = 480;
        var outer = new DockPanel(); DockPanel.SetDock(_status, Dock.Bottom); outer.Children.Add(_status);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(210) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(300) });
        var left = new StackPanel { Margin = new Thickness(12) }; left.Children.Add(new TextBlock { Text = "谱系管理", FontSize = 17 }); left.Children.Add(new TextBlock { Text = "虚构修谱项目\n虚构空谱系", Margin = new Thickness(0, 12, 0, 0) }); grid.Children.Add(left);
        var center = new DockPanel { Margin = new Thickness(8) }; Grid.SetColumn(center, 1);
        var notice = new TextBlock { Text = "隔离虚构样本；关闭主工作台后样本不会自动恢复。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(notice, Dock.Top); center.Children.Add(notice);
        var tools = new WrapPanel(); tools.Children.Add(_create); tools.Children.Add(_empty); tools.Children.Add(_addChild); tools.Children.Add(_addChildEmpty); tools.Children.Add(_addSpouse); tools.Children.Add(_reload); tools.Children.Add(_retry); DockPanel.SetDock(tools, Dock.Top); center.Children.Add(tools);
        _canvas.LayoutTransform = _scale; _scroll.Content = _canvas; center.Children.Add(_scroll); grid.Children.Add(center);
        _scroll.SizeChanged += (_, _) => FillCanvasViewport();
        _scroll.ScrollChanged += (_, _) => FillCanvasViewport();
        var right = new StackPanel { Margin = new Thickness(12) }; right.Children.Add(new TextBlock { Text = "人物资料", FontSize = 17 }); right.Children.Add(_details); right.Children.Add(_familyDetails); Grid.SetColumn(right, 2); grid.Children.Add(right);
        outer.Children.Add(grid); Content = outer;
        _create.Click += (_, _) => BeginNamed();
        _empty.Click += async (_, _) => await BeginEmptyAsync();
        _reload.Click += async (_, _) => await ReloadAsync();
        _addChild.Click += (_, _) => BeginFamilyInput(InputKind.Child);
        _addChildEmpty.Click += async (_, _) => await BeginEmptyChildAsync();
        _addSpouse.Click += (_, _) => BeginFamilyInput(InputKind.Spouse);
        _retry.Click += async (_, _) => { if (_pending is not null) await SubmitPendingAsync(); else await SubmitFamilyAsync(); };
        _canvas.MouseRightButtonDown += (_, e) => { if (e.OriginalSource != _canvas || _hasRoot || _saving || _pending is not null) return; e.Handled = true; var menu = new ContextMenu(); var item = new MenuItem { Header = "添加首节点" }; item.Click += (_, _) => BeginNamed(); menu.Items.Add(item); var blank = new MenuItem { Header = "添加空节点" }; blank.Click += async (_, _) => await BeginEmptyAsync(); menu.Items.Add(blank); menu.PlacementTarget = _canvas; menu.IsOpen = true; };
        _scroll.PreviewMouseWheel += (_, e) => { var point = e.GetPosition(_scroll); var old = _scale.ScaleX; var next = Math.Clamp(old * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.4, 2.5); var x = (_scroll.HorizontalOffset + point.X) / old; var y = (_scroll.VerticalOffset + point.Y) / old; _scale.ScaleX = _scale.ScaleY = next; _scroll.UpdateLayout(); _scroll.ScrollToHorizontalOffset(x * next - point.X); _scroll.ScrollToVerticalOffset(y * next - point.Y); e.Handled = true; };
        _canvas.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource != _canvas || _saving) return; _panStart = e.GetPosition(_scroll); _panOffset = new(_scroll.HorizontalOffset, _scroll.VerticalOffset); _dragged = false; _canvas.CaptureMouse(); e.Handled = true; };
        _canvas.MouseMove += (_, e) => { if (_panStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return; var delta = e.GetPosition(_scroll) - start; if (Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance) _dragged = true; if (_dragged) { _scroll.ScrollToHorizontalOffset(_panOffset.X - delta.X); _scroll.ScrollToVerticalOffset(_panOffset.Y - delta.Y); } };
        _canvas.MouseLeftButtonUp += async (_, e) => { if (_panStart is null) return; _panStart = null; _canvas.ReleaseMouseCapture(); e.Handled = true; if (_dragged) return; if (_input is not null && !_uncertain) await ConfirmNamedAsync(); else if (_input is null && !_uncertain && !_profileDirty) ClearFocus(); };
        _canvas.LostMouseCapture += (_, _) => _panStart = null;
        Loaded += async (_, _) => { try { await ReloadAsync(); } finally { _ready.TrySetResult(); } };
        Closing += OnClosing;
    }

    private void BeginNamed()
    {
        if (!_loadedOk || _saving || _hasRoot || _pending is not null || _input is not null) return;
        _inputKind = InputKind.Root;
        ShowInput(true);
    }
    private void ShowInput(bool showSurname)
    {
        if (_inputKind == InputKind.Child) RenderCanvas(true);
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
        var left = _inputKind == InputKind.Child && _draftLocation is Point d ? d.X : selectedCard is null || _inputKind == InputKind.Root ? 20 : Canvas.GetLeft(selectedCard);
        var top = _inputKind == InputKind.Child && _draftLocation is Point p ? p.Y : selectedCard is null || _inputKind == InputKind.Root ? 50 : Canvas.GetTop(selectedCard) + 42 + _visibleSpouses.Count * 38;
        Canvas.SetLeft(_inputHost, left); Canvas.SetTop(_inputHost, top); _canvas.Children.Add(_inputHost);
        _input.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; if (!_uncertain) await ConfirmNamedAsync(); } else if (e.Key == Key.Escape && !_uncertain && !_saving) { e.Handled = true; CancelInput(); } };
        _input.Focus(); _status.Text = "输入名字，Enter 或单击画布空白提交；Esc 取消。";
    }
    private void FillCanvasViewport()
    {
        if (_scroll.ViewportWidth <= 0 || _scroll.ViewportHeight <= 0) return;
        _canvas.MinWidth = Math.Max(_contentWidth, (_scroll.ViewportWidth - 1) / _scale.ScaleX);
        _canvas.MinHeight = Math.Max(_contentHeight, (_scroll.ViewportHeight - 1) / _scale.ScaleY);
    }
    private async Task ConfirmNamedAsync()
    {
        if (_input is null || _saving || _uncertain) return;
        var name = _input.Text.Trim();
        if (name.Length == 0) { _status.Text = "请输入名字；空姓名请使用添加空节点。"; _input.Focus(); return; }
        if (_inputKind == InputKind.Root) { _pending = new(Guid.NewGuid(), _genealogy, _lineage, RootNameMode.Named, _surname + name); await SubmitPendingAsync(); }
        else if (_personId is Guid person && _placementId is Guid placement)
        {
            if (_inputKind == InputKind.Child) _pendingChild = new(Guid.NewGuid(), _genealogy, _lineage, person, placement, RootNameMode.Named, _surname + name);
            else _pendingSpouse = new(Guid.NewGuid(), _genealogy, _lineage, person, placement, RootNameMode.Named, name);
            await SubmitFamilyAsync();
        }
    }
    private void BeginFamilyInput(InputKind kind)
    {
        if (!_loadedOk || _saving || HasPending || _profileDirty || _input is not null || _personId is null || _placementId is null || _profiles is not null && _selectedSex != PersonSex.Male) return;
        if (kind == InputKind.Child && _children is null || kind == InputKind.Spouse && _spouses is null) return;
        _inputKind = kind; ShowInput(kind == InputKind.Child);
    }
    private async Task BeginEmptyChildAsync()
    {
        if (!_loadedOk || _saving || HasPending || _profileDirty || _input is not null || _children is null || _personId is not Guid person || _placementId is not Guid placement || _profiles is not null && _selectedSex != PersonSex.Male) return;
        _pendingChild = new(Guid.NewGuid(), _genealogy, _lineage, person, placement, RootNameMode.Empty, "");
        await SubmitFamilyAsync();
    }
    private async Task SubmitFamilyAsync()
    {
        if (_saving || !HasPending) return;
        _saving = true; UpdateButtons(); if (_input is not null) _input.IsReadOnly = true;
        try
        {
            try
            {
                if (_pendingChild is { } child && _children is not null)
                {
                    var saved = await _children.ExecuteAsync(child);
                    _personId = saved.PersonId; _placementId = saved.PlacementId; _pendingChild = null;
                }
                else if (_pendingSpouse is { } spouse && _spouses is not null) { await _spouses.AddAsync(spouse); _pendingSpouse = null; }
                else if (_pendingMother is { } mother && _spouses is not null) { await _spouses.SetMotherAsync(mother); _pendingMother = null; }
                else if (_pendingProfile is { } profile && _profiles is not null) { await _profiles.UpdateAsync(profile); _pendingProfile = null; _profileDirty = false; }
                else return;
            }
            catch (Exception error)
            {
                try
                {
                    var committed = _pendingChild is { } c && _children is not null ? await _children.HasReceiptAsync(c.OperationId)
                        : _pendingSpouse is { } s && _spouses is not null ? await _spouses.HasReceiptAsync(s.OperationId, false)
                        : _pendingMother is { } m && _spouses is not null ? await _spouses.HasReceiptAsync(m.OperationId, true)
                        : _pendingProfile is { } p && _profiles is not null ? await _profiles.HasReceiptAsync(p.OperationId)
                        : true;
                    if (!committed)
                    {
                        _pendingChild = null; _pendingSpouse = null; _pendingMother = null; _pendingProfile = null; _uncertain = false;
                        if (_input is not null) _input.IsReadOnly = false;
                        _status.Text = $"未保存：{error.Message}。草稿已保留，可修改后重试。";
                        return;
                    }
                }
                catch { /* Receipt lookup failed: retain the same operation for a safe retry. */ }
                _uncertain = true; _status.Text = $"保存未确认：{error.Message}。草稿已保留，请重试同一操作。"; return;
            }
            _uncertain = false;
            if (_inputHost is not null) _canvas.Children.Remove(_inputHost);
            _inputHost = null; _input = null;
            try { await ReloadCoreAsync(); _status.Text = "已保存并重新读取。"; }
            catch (Exception error) { _status.Text = $"已保存，但画布读取失败：{error.Message}。请重新读取。"; }
        }
        finally { _saving = false; UpdateButtons(); }
    }
    private async Task BeginEmptyAsync()
    {
        if (!_loadedOk || _saving || _hasRoot || _pending is not null || _input is not null) return;
        _pending = new(Guid.NewGuid(), _genealogy, _lineage, RootNameMode.Empty, "");
        await SubmitPendingAsync();
    }
    private async Task SubmitPendingAsync()
    {
        if (_saving || _pending is not { } command) return;
        _saving = true; UpdateButtons(); if (_input is not null) _input.IsReadOnly = true;
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
                        if (_input is not null) _input.IsReadOnly = false;
                        _status.Text = $"未保存：{error.Message}。输入已保留，可修改后重试。";
                        return;
                    }
                }
                catch { /* The write outcome is still unknown; do not generate a new operation. */ }
                _uncertain = true; _status.Text = $"保存未确认：{error.Message}。输入已保留，请重试同一操作。"; return;
            }
            _pending = null; _uncertain = false; _personId = saved.PersonId; _placementId = saved.PlacementId; _hasRoot = true;
            if (_inputHost is not null) _canvas.Children.Remove(_inputHost);
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
        _hasRoot = _rootNodes.Count > 0; _loadedOk = true;
        if (_personId is Guid person && _placementId is Guid placement &&
            !_rootNodes.Any(n => n.PersonId == person && n.PlacementId == placement) &&
            !_childNodes.Any(n => n.PersonId == person && n.PlacementId == placement)) ClearFocus();
        UpdateSelection(); await RefreshFamilyDetailsAsync(); RenderCanvas();
    }
    private void RenderCanvas(bool previewChild = false)
    {
        _canvas.Children.Clear(); _draftLocation = null;
        var draftOrder = previewChild && _placementId is Guid parent
            ? _childNodes.Count(n => n.ParentPlacementId == parent && n.Sex == PersonSex.Male) + 1 : 0;
        var entries = _rootNodes.Select(n => new DisplayNode(n.PersonId, n.PlacementId, null, n.Name, 0, null, n))
            .Concat(_childNodes.Select(n => new DisplayNode(n.PersonId, n.PlacementId, n.ParentPlacementId, n.Name,
                previewChild && n.ParentPlacementId == _placementId && n.Position >= draftOrder ? n.Position + 1 : n.Position, n.FatherRelationshipId, n))).ToList();
        if (previewChild && _placementId is Guid parentPlace)
            entries.Add(new DisplayNode(_draftId, _draftId, parentPlace, "", draftOrder, _draftId, null));
        var byPlace = entries.ToDictionary(x => x.Placement);
        var generation = new Dictionary<Guid, int>();
        int GetGeneration(DisplayNode item, HashSet<Guid> seen)
        {
            if (generation.TryGetValue(item.Placement, out var existing)) return existing;
            if (!seen.Add(item.Placement)) throw new InvalidOperationException("位置存在循环，无法显示。");
            var value = item.Parent is Guid parent ? byPlace.TryGetValue(parent, out var p) ? checked(GetGeneration(p, seen) + 1) : throw new InvalidOperationException("父位置缺失，无法显示。") : 0;
            seen.Remove(item.Placement); return generation[item.Placement] = value;
        }
        var people = entries.Select(x => new HorizontalLayoutPerson(x.Placement, GetGeneration(x, new()), x.Order, 110, 34,
            x.Placement == _placementId ? _visibleSpouses.Count * 38 : 0)).ToArray();
        var edges = entries.Where(x => x.Parent is not null).Select(x => new HorizontalLayoutRelationship(x.Relationship!.Value, x.Parent!.Value, x.Placement)).ToArray();
        var layout = HorizontalLineageLayout.Calculate(people, edges, entries.Select(x => x.Placement).ToHashSet());
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
            card.MouseLeftButtonDown += async (_, e) => { e.Handled = true; if (_input is not null || HasPending || _saving || _profileDirty) return; _personId = node.Person; _placementId = node.Placement; UpdateSelection(); await RefreshFamilyDetailsAsync(); RenderCanvas(); };
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
                var addBlank = new MenuItem { Header = "添加空子女" }; addBlank.Click += async (_, _) => await BeginEmptyChildAsync(); menu.Items.Add(addBlank);
                var addSpouse = new MenuItem { Header = "添加配偶" }; addSpouse.Click += (_, _) => BeginFamilyInput(InputKind.Spouse); menu.Items.Add(addSpouse);
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
                    Canvas.SetLeft(partner, item.X + 20); Canvas.SetTop(partner, top); _canvas.Children.Add(partner);
                }
            }
        }
        _contentWidth = Math.Max(650, layout.Nodes.Select(n => n.X + n.Width + 100).DefaultIfEmpty(0).Max());
        _contentHeight = Math.Max(360, layout.Nodes.Select(n => n.Y + n.Height + (n.PersonId == _placementId ? _visibleSpouses.Count * 38 : 0) + 100).DefaultIfEmpty(0).Max());
        FillCanvasViewport();
    }
    private sealed record DisplayNode(Guid Person, Guid Placement, Guid? Parent, string Name, int Order, Guid? Relationship, object? Original);
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
    private async Task RefreshFamilyDetailsAsync()
    {
        var version = ++_detailsVersion;
        _familyDetails.Children.Clear();
        _visibleSpouses = Array.Empty<SpouseNode>();
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
            }
            var ownSpouses = _selectedSex is PersonSex.Female or PersonSex.Unknown
                ? Array.Empty<SpouseNode>()
                : await _spouses.ReadAsync(_genealogy, _lineage, person, placement);
            if (version != _detailsVersion || _personId != person || _placementId != placement) return;
            _visibleSpouses = ownSpouses;
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
        catch (Exception error) { if (version == _detailsVersion && _personId == person && _placementId == placement) _familyDetails.Children.Add(new TextBlock { Text = $"关系资料读取失败：{error.Message}", TextWrapping = TextWrapping.Wrap }); }
    }
    private void Select(RootNode node) { _personId = node.PersonId; _placementId = node.PlacementId; _details.Text = $"姓名：{(node.Name.Length == 0 ? "（未填写姓名）" : node.Name)}"; foreach (var card in _canvas.Children.OfType<Border>()) if (card.Tag is RootNode value) card.Background = value.PersonId == _personId && value.PlacementId == _placementId ? Brushes.DarkOrange : Brushes.WhiteSmoke; }
    private void ClearFocus() { _detailsVersion++; _personId = null; _placementId = null; _selectedSex = null; _visibleSpouses = Array.Empty<SpouseNode>(); _details.Text = "未选中人物"; _familyDetails.Children.Clear(); RenderCanvas(); UpdateButtons(); }
    private void CancelInput() { if (_input is null) return; if (_inputHost is not null) _canvas.Children.Remove(_inputHost); _inputHost = null; _input = null; RenderCanvas(); _status.Text = "已取消输入，未创建人物。"; }
    private void UpdateButtons() { _create.IsEnabled = _loadedOk && !_saving && !_hasRoot && !HasPending; _empty.IsEnabled = _loadedOk && !_saving && !_hasRoot && !HasPending; _reload.IsEnabled = !_saving && !HasPending && !_profileDirty; var canAdd = _loadedOk && !_saving && !HasPending && !_profileDirty && _input is null && _personId is not null && _placementId is not null && (_profiles is null || _selectedSex == PersonSex.Male); _addChild.IsEnabled = _addChildEmpty.IsEnabled = canAdd && _children is not null; _addSpouse.IsEnabled = canAdd && _spouses is not null; _retry.Visibility = _uncertain ? Visibility.Visible : Visibility.Collapsed; _retry.IsEnabled = !_saving; }
    internal bool CanCloseForOwner() { if (!_saving && !HasPending && _input is null && !_profileDirty) return true; _status.Text = _uncertain ? "保存结果尚未确认，请重试以保留输入。" : "请先完成或取消当前输入。"; return false; }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!CanCloseForOwner()) e.Cancel = true; }

    internal Task ReadyForTest => _ready.Task;
    internal void BeginForTest() => BeginNamed();
    internal void SetInputForTest(string value) { if (_input is not null) _input.Text = value; }
    internal Task ConfirmForTest() => ConfirmNamedAsync();
    internal Task EmptyForTest() => BeginEmptyAsync();
    internal Task RetryForTest() => SubmitPendingAsync();
    internal Task RetryFamilyForTest() => SubmitFamilyAsync();
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
    internal void ClearFocusForTest() => ClearFocus();
    internal void BeginSpouseForTest() => BeginFamilyInput(InputKind.Spouse);
    internal Task EmptyChildForTest() => BeginEmptyChildAsync();
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
