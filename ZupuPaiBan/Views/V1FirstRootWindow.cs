using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PuJiang.V1.Application;

namespace ZupuPaiBan.Views;

// Isolated sample surface. The existing workbench and its legacy data path remain untouched.
public sealed class V1FirstRootWindow : Window
{
    private readonly FirstRootApplication _app;
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
    private TextBox? _input;
    private AddFirstRoot? _pending;
    private bool _saving, _uncertain, _hasRoot, _loadedOk;
    private Guid? _personId, _placementId;
    private Point? _panStart;
    private Point _panOffset;
    private bool _dragged;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal V1FirstRootWindow(FirstRootApplication app, Guid genealogy, Guid lineage, string surname)
    {
        _app = app; _genealogy = genealogy; _lineage = lineage; _surname = surname;
        Title = "世系编修工作台 — V1 虚构隔离样本"; Width = 1100; Height = 720; MinWidth = 760; MinHeight = 480;
        var outer = new DockPanel(); DockPanel.SetDock(_status, Dock.Bottom); outer.Children.Add(_status);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(210) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(300) });
        var left = new StackPanel { Margin = new Thickness(12) }; left.Children.Add(new TextBlock { Text = "谱系管理", FontSize = 17 }); left.Children.Add(new TextBlock { Text = "虚构修谱项目\n虚构空谱系", Margin = new Thickness(0, 12, 0, 0) }); grid.Children.Add(left);
        var center = new DockPanel { Margin = new Thickness(8) }; Grid.SetColumn(center, 1);
        var notice = new TextBlock { Text = "隔离虚构样本：仅支持首节点；关闭主工作台后样本不会自动恢复。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(notice, Dock.Top); center.Children.Add(notice);
        var tools = new StackPanel { Orientation = Orientation.Horizontal }; tools.Children.Add(_create); tools.Children.Add(_empty); tools.Children.Add(_reload); tools.Children.Add(_retry); DockPanel.SetDock(tools, Dock.Top); center.Children.Add(tools);
        _canvas.LayoutTransform = _scale; _scroll.Content = _canvas; center.Children.Add(_scroll); grid.Children.Add(center);
        _scroll.SizeChanged += (_, _) => FillCanvasViewport();
        _scroll.ScrollChanged += (_, _) => FillCanvasViewport();
        var right = new StackPanel { Margin = new Thickness(12) }; right.Children.Add(new TextBlock { Text = "人物资料", FontSize = 17 }); right.Children.Add(_details); Grid.SetColumn(right, 2); grid.Children.Add(right);
        outer.Children.Add(grid); Content = outer;
        _create.Click += (_, _) => BeginNamed();
        _empty.Click += async (_, _) => await BeginEmptyAsync();
        _reload.Click += async (_, _) => await ReloadAsync();
        _retry.Click += async (_, _) => await SubmitPendingAsync();
        _canvas.MouseRightButtonDown += (_, e) => { if (e.OriginalSource != _canvas || _hasRoot || _saving || _pending is not null) return; e.Handled = true; var menu = new ContextMenu(); var item = new MenuItem { Header = "添加首节点" }; item.Click += (_, _) => BeginNamed(); menu.Items.Add(item); var blank = new MenuItem { Header = "添加空节点" }; blank.Click += async (_, _) => await BeginEmptyAsync(); menu.Items.Add(blank); menu.PlacementTarget = _canvas; menu.IsOpen = true; };
        _scroll.PreviewMouseWheel += (_, e) => { var point = e.GetPosition(_scroll); var old = _scale.ScaleX; var next = Math.Clamp(old * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.4, 2.5); var x = (_scroll.HorizontalOffset + point.X) / old; var y = (_scroll.VerticalOffset + point.Y) / old; _scale.ScaleX = _scale.ScaleY = next; _scroll.UpdateLayout(); _scroll.ScrollToHorizontalOffset(x * next - point.X); _scroll.ScrollToVerticalOffset(y * next - point.Y); e.Handled = true; };
        _canvas.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource != _canvas || _saving) return; _panStart = e.GetPosition(_scroll); _panOffset = new(_scroll.HorizontalOffset, _scroll.VerticalOffset); _dragged = false; _canvas.CaptureMouse(); e.Handled = true; };
        _canvas.MouseMove += (_, e) => { if (_panStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return; var delta = e.GetPosition(_scroll) - start; if (Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance) _dragged = true; if (_dragged) { _scroll.ScrollToHorizontalOffset(_panOffset.X - delta.X); _scroll.ScrollToVerticalOffset(_panOffset.Y - delta.Y); } };
        _canvas.MouseLeftButtonUp += async (_, e) => { if (_panStart is null) return; _panStart = null; _canvas.ReleaseMouseCapture(); e.Handled = true; if (_dragged) return; if (_input is not null && !_uncertain) await ConfirmNamedAsync(); else if (_input is null && !_uncertain) ClearFocus(); };
        _canvas.LostMouseCapture += (_, _) => _panStart = null;
        Loaded += async (_, _) => { try { await ReloadAsync(); } finally { _ready.TrySetResult(); } };
        Closing += OnClosing;
    }

    private void BeginNamed()
    {
        if (!_loadedOk || _saving || _hasRoot || _pending is not null || _input is not null) return;
        _input = new TextBox { MinWidth = 120, Text = "", ToolTip = "输入名字（不含项目姓氏）" };
        Canvas.SetLeft(_input, 20); Canvas.SetTop(_input, 50); _canvas.Children.Add(_input);
        _input.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; if (!_uncertain) await ConfirmNamedAsync(); } else if (e.Key == Key.Escape && !_uncertain && !_saving) { e.Handled = true; CancelInput(); } };
        _input.Focus(); _status.Text = "输入名字，Enter 或单击画布空白提交；Esc 取消。";
    }
    private void FillCanvasViewport()
    {
        if (_scroll.ViewportWidth <= 0 || _scroll.ViewportHeight <= 0) return;
        _canvas.MinWidth = Math.Max(650, (_scroll.ViewportWidth - 1) / _scale.ScaleX);
        _canvas.MinHeight = Math.Max(360, (_scroll.ViewportHeight - 1) / _scale.ScaleY);
    }
    private async Task ConfirmNamedAsync()
    {
        if (_input is null || _saving || _uncertain) return;
        var name = _input.Text.Trim();
        if (name.Length == 0) { _status.Text = "请输入名字；空姓名请使用添加空节点。"; _input.Focus(); return; }
        _pending = new(Guid.NewGuid(), _genealogy, _lineage, RootNameMode.Named, _surname + name);
        await SubmitPendingAsync();
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
            if (_input is not null) { _canvas.Children.Remove(_input); _input = null; }
            try { await ReloadCoreAsync(); _status.Text = "首节点已保存并选中。"; }
            catch (Exception error) { _status.Text = $"已保存，但画布读取失败：{error.Message}。请重新读取。"; }
        }
        finally { _saving = false; UpdateButtons(); }
    }
    private async Task ReloadAsync()
    {
        if (_saving || _pending is not null || _input is not null) { _status.Text = "请先完成或取消当前输入。"; return; }
        _saving = true; UpdateButtons();
        try { await ReloadCoreAsync(); _status.Text = "已读取隔离样本。"; }
        catch (Exception error) { _status.Text = $"读取失败：{error.Message}"; }
        finally { _saving = false; UpdateButtons(); }
    }
    private async Task ReloadCoreAsync()
    {
        var result = await _app.ReadAsync(_genealogy, _lineage);
        var nodes = result.Nodes; _canvas.Children.Clear(); _hasRoot = nodes.Count > 0; _loadedOk = true;
        if (_personId is Guid person && _placementId is Guid placement && !nodes.Any(n => n.PersonId == person && n.PlacementId == placement)) ClearFocus();
        foreach (var node in nodes)
        {
            var visibleName = node.Name.Length == 0 ? "（未填写姓名）" : node.Name.StartsWith(_surname, StringComparison.Ordinal) ? node.Name[_surname.Length..] : node.Name;
            var card = new Border { Background = node.PersonId == _personId && node.PlacementId == _placementId ? Brushes.DarkOrange : Brushes.WhiteSmoke, Padding = new Thickness(8), Child = new TextBlock { Text = visibleName }, Tag = node };
            Canvas.SetLeft(card, 20); Canvas.SetTop(card, 50); _canvas.Children.Add(card);
            card.MouseLeftButtonDown += (_, e) => { e.Handled = true; if (_input is not null || _pending is not null || _saving) return; Select(node); };
        }
        var selected = nodes.FirstOrDefault(n => n.PersonId == _personId && n.PlacementId == _placementId);
        _details.Text = selected is null ? "未选中人物" : $"姓名：{(selected.Name.Length == 0 ? "（未填写姓名）" : selected.Name)}";
    }
    private void Select(RootNode node) { _personId = node.PersonId; _placementId = node.PlacementId; _details.Text = $"姓名：{(node.Name.Length == 0 ? "（未填写姓名）" : node.Name)}"; foreach (var card in _canvas.Children.OfType<Border>()) if (card.Tag is RootNode value) card.Background = value.PersonId == _personId && value.PlacementId == _placementId ? Brushes.DarkOrange : Brushes.WhiteSmoke; }
    private void ClearFocus() { _personId = null; _placementId = null; _details.Text = "未选中人物"; foreach (var card in _canvas.Children.OfType<Border>()) card.Background = Brushes.WhiteSmoke; }
    private void CancelInput() { if (_input is null) return; _canvas.Children.Remove(_input); _input = null; _status.Text = "已取消输入，未创建人物。"; }
    private void UpdateButtons() { _create.IsEnabled = _loadedOk && !_saving && !_hasRoot && _pending is null; _empty.IsEnabled = _loadedOk && !_saving && !_hasRoot && _pending is null; _reload.IsEnabled = !_saving && _pending is null; _retry.Visibility = _uncertain ? Visibility.Visible : Visibility.Collapsed; _retry.IsEnabled = !_saving; }
    internal bool CanCloseForOwner() { if (!_saving && _pending is null && _input is null) return true; _status.Text = _uncertain ? "保存结果尚未确认，请重试以保留输入。" : "请先完成或取消当前输入。"; return false; }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!CanCloseForOwner()) e.Cancel = true; }

    internal Task ReadyForTest => _ready.Task;
    internal void BeginForTest() => BeginNamed();
    internal void SetInputForTest(string value) { if (_input is not null) _input.Text = value; }
    internal Task ConfirmForTest() => ConfirmNamedAsync();
    internal Task EmptyForTest() => BeginEmptyAsync();
    internal Task RetryForTest() => SubmitPendingAsync();
    internal Task ReloadForTest() => ReloadAsync();
    internal void CancelForTest() => CancelInput();
    internal (Guid? Person, Guid? Placement) FocusForTest => (_personId, _placementId);
    internal string StatusForTest => _status.Text;
    internal string? InputForTest => _input?.Text;
    internal bool InputReadOnlyForTest => _input?.IsReadOnly ?? false;
    internal TextBox? InputControlForTest => _input;
    internal Canvas CanvasForTest => _canvas;
    internal ScrollViewer ScrollForTest => _scroll;
    internal int NodeCountForTest => _canvas.Children.OfType<Border>().Count(x => x.Tag is RootNode);
}
