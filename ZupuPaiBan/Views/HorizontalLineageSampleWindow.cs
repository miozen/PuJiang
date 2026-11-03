using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.Views;

// An explicitly fictional, memory-only sample. It never reads a genealogy or writes data.
public sealed class HorizontalLineageSampleWindow : Window
{
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");
    private static readonly HorizontalLayoutPerson[] People = Enumerable.Range(1, 8).Select(n =>
        new HorizontalLayoutPerson(Id(n), n == 1 ? 0 : n <= 3 ? 1 : n <= 6 ? 2 : 3,
            n switch { 3 or 5 or 7 => 1, _ => 0 }, 150, 72)).ToArray();
    private static readonly HorizontalLayoutRelationship[] Relations = new[] {
        Edge(1, 1, 2), Edge(2, 1, 3), Edge(3, 2, 4), Edge(4, 2, 5),
        Edge(5, 3, 6), Edge(6, 4, 8), Edge(7, 4, 7) };
    private static HorizontalLayoutRelationship Edge(int id, int parent, int child) => new(Id(100 + id), Id(parent), Id(child));
    private static readonly string[] Names = ["", "示例·始祖", "示例·长房", "示例·次房", "示例·长孙", "示例·次孙", "示例·三孙", "示例·后代乙", "示例·后代甲"];
    private readonly HashSet<Guid> _expanded = [Id(1)];
    private readonly Canvas _canvas = new() { Background = Brushes.WhiteSmoke, Margin = new Thickness(0) };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TextBox _search = new() { Width = 150, Margin = new Thickness(8, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Margin = new Thickness(12, 6, 12, 6), Text = "虚构示例 · 只读 · 单击人物展开子女，使用 ± 折叠或展开" };
    private Guid? _selected;
    private Point? _dragStart;
    private Point _scrollStart;
    private bool _dragged;
    internal HorizontalLayoutResult CurrentLayout { get; private set; } = null!;
    internal Guid? SelectedPerson => _selected;
    internal bool IsExpanded(Guid id) => _expanded.Contains(id);
    internal double Zoom => _scale.ScaleX;

    public HorizontalLineageSampleWindow()
    {
        Title = "横向世系图 — 虚构示例（只读）";
        Width = 1060; Height = 680; MinWidth = 650; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var outer = new DockPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 8) };
        bar.Children.Add(new TextBlock { Text = "横向世系图", FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(new TextBlock { Text = "  虚构示例 · 只读", Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(_search);
        var find = new Button { Content = "查找示例人物", Padding = new Thickness(8, 3, 8, 3) };
        find.Click += (_, _) => FindPerson(_search.Text); bar.Children.Add(find);
        DockPanel.SetDock(bar, Dock.Top); outer.Children.Add(bar);
        DockPanel.SetDock(_status, Dock.Bottom); outer.Children.Add(_status);
        _canvas.LayoutTransform = _scale;
        _scroll.Content = _canvas;
        _scroll.PreviewMouseWheel += OnWheel;
        _canvas.MouseLeftButtonDown += CanvasDown;
        _canvas.MouseMove += CanvasMove;
        _canvas.MouseLeftButtonUp += CanvasUp;
        _canvas.LostMouseCapture += (_, _) => _dragStart = null;
        _canvas.ContextMenu = new ContextMenu();
        _canvas.ContextMenu.Items.Add(new MenuItem { Header = "虚构示例 · 只读", IsEnabled = false });
        outer.Children.Add(_scroll); Content = outer;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { _selected = null; Render(); e.Handled = true; } };
        Render();
    }

    internal void SelectPerson(Guid id, bool expandChildren)
    {
        if (!People.Any(p => p.Id == id)) return;
        _selected = id;
        if (expandChildren && Relations.Any(r => r.ParentId == id)) _expanded.Add(id);
        Render();
    }

    internal void ToggleBranch(Guid id)
    {
        if (!Relations.Any(r => r.ParentId == id)) return;
        if (!_expanded.Remove(id)) _expanded.Add(id);
        if (_selected.HasValue && _selected != id && !VisibleAfterExpansion(_selected.Value)) _selected = null;
        Render();
    }

    private bool VisibleAfterExpansion(Guid id) => HorizontalLineageLayout.Calculate(People, Relations, _expanded).Nodes.Any(n => n.PersonId == id);

    internal bool FindPerson(string query)
    {
        var index = Array.FindIndex(Names, 1, n => n.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(query) || index < 1) { _status.Text = "未找到示例人物。"; return false; }
        var id = Id(index); var child = id;
        while (Relations.FirstOrDefault(r => r.ChildId == child) is { } edge) { _expanded.Add(edge.ParentId); child = edge.ParentId; }
        SelectPerson(id, false);
        var node = CurrentLayout.Nodes.Single(n => n.PersonId == id);
        _scroll.UpdateLayout();
        _scroll.ScrollToHorizontalOffset(Math.Max(0, (node.X + 40) * Zoom - _scroll.ViewportWidth / 2));
        _scroll.ScrollToVerticalOffset(Math.Max(0, (node.Y + 40) * Zoom - _scroll.ViewportHeight / 2));
        return true;
    }

    private void Render()
    {
        CurrentLayout = HorizontalLineageLayout.Calculate(People, Relations, _expanded);
        _canvas.Children.Clear();
        _canvas.Width = CurrentLayout.Nodes.Max(n => n.X + n.Width) + 80;
        _canvas.Height = CurrentLayout.Nodes.Max(n => n.Y + n.Height) + 80;
        foreach (var edge in CurrentLayout.Edges)
        {
            var polyline = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(133, 149, 154)), StrokeThickness = 1.5, IsHitTestVisible = false };
            foreach (var p in edge.Points) polyline.Points.Add(new Point(p.X + 40, p.Y + 40));
            _canvas.Children.Add(polyline);
        }
        foreach (var node in CurrentLayout.Nodes)
        {
            var id = node.PersonId; var index = Array.FindIndex(People, p => p.Id == id) + 1;
            var card = new Border { Width = node.Width, Height = node.Height, CornerRadius = new CornerRadius(6),
                Background = Brushes.White, BorderThickness = new Thickness(id == _selected ? 2 : 1),
                BorderBrush = id == _selected ? Brushes.SteelBlue : Brushes.LightSlateGray, Padding = new Thickness(9, 5, 6, 5), Tag = id };
            var row = new DockPanel();
            if (Relations.Any(e => e.ParentId == id))
            {
                var toggle = new Button { Content = _expanded.Contains(id) ? "−" : "+", Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center, ToolTip = _expanded.Contains(id) ? "折叠后代" : "展开子女" };
                toggle.Click += (_, e) => { ToggleBranch(id); e.Handled = true; };
                DockPanel.SetDock(toggle, Dock.Right); row.Children.Add(toggle);
            }
            var labels = new StackPanel();
            labels.Children.Add(new TextBlock { Text = Names[index], FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            labels.Children.Add(new TextBlock { Text = $"第 {node.Generation + 1} 世", Foreground = Brushes.DimGray, FontSize = 11 });
            if (id == _selected) labels.Children.Add(new TextBlock { Text = "配偶：示例配偶", Foreground = Brushes.SteelBlue, FontSize = 11 });
            row.Children.Add(labels); card.Child = row;
            card.MouseLeftButtonDown += (_, e) => { SelectPerson(id, true); e.Handled = true; };
            card.MouseRightButtonDown += (_, e) => { SelectPerson(id, false); e.Handled = true; var menu = new ContextMenu(); menu.Items.Add(new MenuItem { Header = "示例人物 · 只读", IsEnabled = false }); menu.PlacementTarget = _canvas; menu.IsOpen = true; };
            Canvas.SetLeft(card, node.X + 40); Canvas.SetTop(card, node.Y + 40); _canvas.Children.Add(card);
        }
        _status.Text = _selected.HasValue ? $"已选择 {Names[Array.FindIndex(People, p => p.Id == _selected.Value) + 1]} · 虚构数据，只读" : "虚构示例 · 只读 · 单击人物展开子女，使用 ± 折叠或展开";
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var position = e.GetPosition(_scroll); var old = Zoom;
        var next = Math.Clamp(old * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.4, 2.5);
        if (next == old) return;
        var x = (_scroll.HorizontalOffset + position.X) / old; var y = (_scroll.VerticalOffset + position.Y) / old;
        _scale.ScaleX = _scale.ScaleY = next; _scroll.UpdateLayout();
        _scroll.ScrollToHorizontalOffset(x * next - position.X); _scroll.ScrollToVerticalOffset(y * next - position.Y);
        e.Handled = true;
    }

    private void CanvasDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(_scroll); _scrollStart = new Point(_scroll.HorizontalOffset, _scroll.VerticalOffset); _dragged = false;
        _canvas.CaptureMouse(); e.Handled = true;
    }
    private void CanvasMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var now = e.GetPosition(_scroll); var delta = now - start;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 4) _dragged = true;
        if (_dragged) { _scroll.ScrollToHorizontalOffset(_scrollStart.X - delta.X); _scroll.ScrollToVerticalOffset(_scrollStart.Y - delta.Y); }
    }
    private void CanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null) return;
        var clear = !_dragged; _dragStart = null; _canvas.ReleaseMouseCapture();
        if (clear) { _selected = null; Render(); }
        e.Handled = true;
    }
}
