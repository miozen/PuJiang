using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ZupuPaiBan.Views;

// Self-contained fictional data: no context, database, or filesystem access.
public sealed class WorkbenchPrototypeWindow : Window
{
    private sealed record Person(int Id, string Name, int Generation, int? Parent, string Details, string Spouse = "未记录");
    private sealed record Item(Person Person, bool Reference = false);
    private static readonly Person[] People =
    [
        new(1, "示例·始祖", 1, null, "示例谱始祖"),
        new(2, "示例·长房", 2, 1, "长房"),
        new(3, "示例·次房", 2, 1, "次房"),
        new(4, "示例·胜邦", 3, 2, "振凤的嗣父"),
        new(5, "示例·裕庆", 4, 3, "振凤的生父"),
        new(6, "示例·振凤", 5, 4, "裕庆亲生长女，过继给胜邦", "示例·配偶"),
        new(7, "示例·长子", 6, 6, "振凤的长子"),
        new(8, "示例·次子", 6, 6, "振凤的次子"),
        new(9, "示例·未关联", 4, null, "尚未建立父子关系")
    ];
    private const double ColumnWidth = 190, NodeWidth = 138, NodeHeight = 52, RowHeight = 86, CanvasTop = 60;
    private readonly HashSet<int> _expanded = [1, 2, 3, 4, 5, 6];
    private readonly Dictionary<int, Point> _positions = [];
    private readonly Canvas _canvas = new() { Background = Brushes.White };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TextBox _search = new() { Width = 150, Margin = new Thickness(8, 0, 4, 0) };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 26 };
    private readonly TextBlock _status = new() { Text = "虚构内存样本 · 关闭后不保存", Margin = new Thickness(10, 5, 10, 5) };
    private int? _selected;
    private Point? _dragStart;
    private Point _scrollStart;
    private bool _dragged;
    private int _sample;

    public WorkbenchPrototypeWindow()
    {
        Title = "世系编修工作台 — 虚构内存原型";
        Width = 1380; Height = 800; MinWidth = 850; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var outer = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(12, 8, 12, 8) };
        header.Children.Add(new TextBlock { Text = "谱匠  /  世系编修工作台", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var searchPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        searchPanel.Children.Add(_search);
        var find = new Button { Content = "查找人物", Padding = new Thickness(10, 4, 10, 4) };
        find.Click += (_, _) => FindPerson(); searchPanel.Children.Add(find);
        header.Children.Add(searchPanel);
        DockPanel.SetDock(header, Dock.Top); outer.Children.Add(header);
        DockPanel.SetDock(_status, Dock.Bottom); outer.Children.Add(_status);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) });
        var left = new StackPanel { Margin = new Thickness(12) };
        left.Children.Add(new TextBlock { Text = "谱系管理", FontSize = 17, FontWeight = FontWeights.SemiBold });
        left.Children.Add(new TextBlock { Text = "虚构示例", Foreground = Brushes.Gray, Margin = new Thickness(0, 12, 0, 5) });
        var list = new ListBox { Height = 240, ItemsSource = new[] { "示例谱 · 多代过继", "示例谱 · 空白" }, SelectedIndex = 0 };
        list.SelectionChanged += (_, _) => { _sample = list.SelectedIndex; _selected = null; Render(); };
        left.Children.Add(list);
        left.Children.Add(new TextBlock { Text = "新增、重命名谱系将在后续接入。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 12, 0, 0) });
        Grid.SetColumn(left, 0); columns.Children.Add(left);
        _canvas.LayoutTransform = _scale; _scroll.Content = _canvas;
        _scroll.PreviewMouseWheel += OnWheel;
        _canvas.MouseLeftButtonDown += CanvasDown; _canvas.MouseMove += CanvasMove; _canvas.MouseLeftButtonUp += CanvasUp;
        _canvas.LostMouseCapture += (_, _) => _dragStart = null;
        Grid.SetColumn(_scroll, 1); columns.Children.Add(_scroll);
        var right = new StackPanel { Margin = new Thickness(14) };
        right.Children.Add(new TextBlock { Text = "人物资料与操作", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        right.Children.Add(_details);
        right.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12) });
        foreach (var label in new[] { "新增人物", "新增子女", "新增配偶", "建立 / 解除关系", "编辑人物" })
            right.Children.Add(new Button { Content = label + "（后续接入）", IsEnabled = false, Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(5) });
        Grid.SetColumn(right, 2); columns.Children.Add(right);
        outer.Children.Add(columns); Content = outer;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { _selected = null; Render(); e.Handled = true; } };
        Render();
    }

    private static IEnumerable<Item> Children(int parent)
    {
        foreach (var p in People.Where(p => p.Parent == parent)) yield return new Item(p);
        if (parent == 5) yield return new Item(People.Single(p => p.Id == 6), true);
    }

    private void Render()
    {
        _canvas.Children.Clear(); _positions.Clear();
        _canvas.Width = 6 * ColumnWidth + 50;
        double row = 0;
        var placements = new List<(Item Item, Point Position)>();
        var edges = new List<(Point Parent, Point Child)>();
        Point Place(Item item)
        {
            var children = !item.Reference && _expanded.Contains(item.Person.Id) ? Children(item.Person.Id).ToArray() : [];
            var childPoints = children.Select(Place).ToArray();
            var y = childPoints.Length == 0 ? CanvasTop + row++ * RowHeight : childPoints[0].Y;
            var pos = new Point(25 + (item.Person.Generation - 1) * ColumnWidth, y);
            placements.Add((item, pos));
            if (!item.Reference) _positions[item.Person.Id] = pos;
            foreach (var child in childPoints) edges.Add((pos, child));
            return pos;
        }
        if (_sample == 0)
            foreach (var root in People.Where(p => p.Parent is null)) Place(new Item(root));
        _canvas.Height = Math.Max(600, CanvasTop + (row + 1) * RowHeight);
        for (var i = 0; i < 6; i++)
        {
            var x = 25 + i * ColumnWidth;
            var line = new Line { X1 = x - 12, X2 = x - 12, Y1 = 0, Y2 = _canvas.Height, Stroke = Brushes.Gainsboro, IsHitTestVisible = false };
            _canvas.Children.Add(line);
            var label = new TextBlock { Text = $"第 {i + 1} 世", Foreground = Brushes.SlateGray, FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(label, x); Canvas.SetTop(label, 15); _canvas.Children.Add(label);
        }
        foreach (var edge in edges)
        {
            var start = new Point(edge.Parent.X + NodeWidth, edge.Parent.Y + NodeHeight / 2);
            var end = new Point(edge.Child.X, edge.Child.Y + NodeHeight / 2);
            var middle = start.X + 20;
            var path = new Polyline { Stroke = Brushes.SlateGray, StrokeThickness = 1.5, IsHitTestVisible = false,
                Points = new PointCollection { start, new(middle, start.Y), new(middle, end.Y), end } };
            _canvas.Children.Add(path);
        }
        foreach (var (item, pos) in placements)
        {
            var person = item.Person;
            var card = new Border { Width = NodeWidth, Height = NodeHeight, Background = item.Reference ? Brushes.Ivory : Brushes.White,
                BorderBrush = _selected == person.Id && !item.Reference ? Brushes.SteelBlue : Brushes.LightSlateGray,
                BorderThickness = new Thickness(_selected == person.Id && !item.Reference ? 2 : 1), CornerRadius = new CornerRadius(5), Padding = new Thickness(5) };
            var content = new DockPanel();
            if (!item.Reference && Children(person.Id).Any())
            {
                var toggle = new Button { Content = _expanded.Contains(person.Id) ? "−" : "+", Width = 22, Height = 22 };
                toggle.Click += (_, e) => { if (!_expanded.Remove(person.Id)) _expanded.Add(person.Id); Render(); e.Handled = true; };
                DockPanel.SetDock(toggle, Dock.Right); content.Children.Add(toggle);
            }
            content.Children.Add(new TextBlock { Text = item.Reference ? "振凤\n（出继胜邦）" : person.Name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            card.Child = content;
            card.MouseLeftButtonDown += (_, e) => { _selected = person.Id; if (!item.Reference && Children(person.Id).Any()) _expanded.Add(person.Id); Render(); e.Handled = true; };
            card.MouseRightButtonDown += (_, e) =>
            {
                _selected = person.Id; Render(); e.Handled = true;
                var menu = new ContextMenu { PlacementTarget = _canvas };
                menu.Items.Add(new MenuItem { Header = "新增或编辑（后续接入）", IsEnabled = false }); menu.IsOpen = true;
            };
            Canvas.SetLeft(card, pos.X); Canvas.SetTop(card, pos.Y); _canvas.Children.Add(card);
        }
        var selected = _selected is int id ? People.FirstOrDefault(p => p.Id == id) : null;
        _details.Text = selected is null ? "请选择画布上的人物" : $"姓名：{selected.Name}\n世代：第 {selected.Generation} 世\n说明：{selected.Details}\n婚配：{selected.Spouse}";
    }

    private void FindPerson()
    {
        var term = _search.Text.Trim();
        var person = _sample == 0 && term.Length > 0 ? People.FirstOrDefault(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) : null;
        if (person is null) { _status.Text = "未找到示例人物。"; return; }
        for (var parent = person.Parent; parent is int id; parent = People.Single(p => p.Id == id).Parent) _expanded.Add(id);
        _selected = person.Id; Render(); _scroll.UpdateLayout();
        var pos = _positions[person.Id];
        _scroll.ScrollToHorizontalOffset(Math.Max(0, pos.X * _scale.ScaleX - _scroll.ViewportWidth / 2));
        _scroll.ScrollToVerticalOffset(Math.Max(0, pos.Y * _scale.ScaleY - _scroll.ViewportHeight / 2));
        _status.Text = "已定位虚构人物 · 关闭后不保存";
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var point = e.GetPosition(_scroll); var old = _scale.ScaleX;
        var next = Math.Clamp(old * (e.Delta > 0 ? 1.15 : 1 / 1.15), .4, 2.5);
        var x = (_scroll.HorizontalOffset + point.X) / old; var y = (_scroll.VerticalOffset + point.Y) / old;
        _scale.ScaleX = _scale.ScaleY = next; _scroll.UpdateLayout();
        _scroll.ScrollToHorizontalOffset(x * next - point.X); _scroll.ScrollToVerticalOffset(y * next - point.Y); e.Handled = true;
    }
    private void CanvasDown(object sender, MouseButtonEventArgs e)
    {
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
        if (clear) { _selected = null; Render(); } e.Handled = true;
    }
}
