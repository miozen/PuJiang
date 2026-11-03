using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class LineageTreeDialog : Window
{
    private readonly GenealogySelectionViewModel _viewModel;
    private readonly LineagePreviewContext _preview;
    private LineageTreeState _tree;
    private LineageProjectionSession _session;
    private Guid? _chartId;
    private long? _chartRevision;
    private readonly List<ChartChoiceReview> _invalidChoices = new();
    private bool _invalid;
    private bool _rendering;
    private LineageTreeNode? _selected;
    private readonly HashSet<Guid> _graphExpanded = new();
    private LineageGraphLayout? _graphLayout;

    public LineageTreeDialog(GenealogySelectionViewModel viewModel, LineagePreviewContext preview)
    {
        _viewModel = viewModel;
        _preview = preview;
        _session = new LineageProjectionSession(preview.Snapshot);
        _tree = new LineageTreeState(_session);
        InitializeComponent();
        _viewModel.PropertyChanged += ViewModelChanged;
        _preview.Tree.CollectionChanged += TreeChanged;
        Closed += (_, _) => { _viewModel.PropertyChanged -= ViewModelChanged; _preview.Tree.CollectionChanged -= TreeChanged; };
        Render(null);
        Loaded += async (_, _) => await RefreshCharts();
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_viewModel.IsLineagePreviewCurrent(_preview)) InvalidatePreview();
    }
    private void TreeChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidatePreview();
    private void InvalidatePreview()
    {
        _invalid = true;
        _selected = null;
        _tree.ClearSelection();
        LineageTree.ItemsSource = null;
        GraphCanvas.Children.Clear();
        _graphLayout = null;
        SearchResults.ItemsSource = null;
        CandidateList.ItemsSource = null;
        DetailsText.Text = string.Empty;
        LocateButton.IsEnabled = ChooseButton.IsEnabled = ClearButton.IsEnabled = ClearSelectionButton.IsEnabled = false;
        StatusText.Text = "族谱已切换或刷新，浏览已失效；请关闭后重新打开。";
    }
    private bool Current()
    {
        if (!_invalid && _viewModel.IsLineagePreviewCurrent(_preview)) return true;
        InvalidatePreview();
        return false;
    }
    private void Render(Guid? selectedId)
    {
        if (!Current()) return;
        _rendering = true;
        LineageTree.ItemsSource = _tree.Roots;
        _selected = selectedId is Guid id ? _tree.GetPerson(id) : null;
        UpdateSearch();
        _rendering = false;
        StatusText.Text = $"共 {_tree.People.Count} 人；{_tree.People.Count(n => n.Projection.NeedsChoice)} 人的父母关系待选择。";
        RenderDetails();
        RenderGraph();
    }
    private void RenderDetails()
    {
        if (_invalid) return;
        var row = _selected?.Projection;
        CandidateList.ItemsSource = row?.ParentCandidates;
        CandidateList.SelectedItem = row?.SelectedParent;
        var lost = row is null ? "" : string.Join("\n", row.LostAncestry.Select(r =>
            $"谱载上系失考（关系 ID: {r.Id}）；备注：{r.Note ?? "（未填写）"}；来源：{r.SourceCitation ?? "（未填写）"}"));
        DetailsText.Text = row is null ? "请选择人物。" :
            $"{row.Person.Name}（人物 ID: {row.Person.Id}）\n" + (string.IsNullOrEmpty(lost) ? "没有上系失考记录。" : lost);
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        if (ChooseButton is null || ClearButton is null) return;
        var row = _selected?.Projection;
        ChooseButton.IsEnabled = !_invalid && row?.ParentCandidates.Count > 0 && CandidateList.SelectedItem is LineageParentOption;
        ClearButton.IsEnabled = !_invalid && row?.ParentCandidates.Count > 1 && row.SelectedParent is not null;
    }
    private void LineageTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_invalid || _rendering) return;
        _selected = e.NewValue as LineageTreeNode;
        var boundary = _tree.Highlight(_selected?.PersonId);
        StatusText.Text = string.IsNullOrEmpty(boundary) ? "请选择人物。" : boundary;
        RenderDetails();
        RenderGraph();
    }
    private void CandidateList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (!Current() || _selected is null || CandidateList.SelectedItem is not LineageParentOption option) return;
        var id = _selected.PersonId;
        _tree.Choose(id, option.Relationship.Id);
        _invalidChoices.RemoveAll(c => c.Choice.PersonId == id);
        RefreshInvalid();
        Render(id);
        Locate(id);
    }

    private async Task RefreshCharts()
    {
        if (!Current()) return;
        try { Charts.ItemsSource = await _viewModel.ListLineageChartsAsync(_preview); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void RefreshInvalid()
    {
        InvalidChoices.ItemsSource = null;
        InvalidChoices.ItemsSource = _invalidChoices.ToList();
    }

    private void NewChart_Click(object sender, RoutedEventArgs e)
    {
        if (!Current()) return;
        _chartId = null; _chartRevision = null; ChartName.Text = string.Empty;
        _graphExpanded.Clear();
        _invalidChoices.Clear(); RefreshInvalid();
        _session = new LineageProjectionSession(_preview.Snapshot);
        _tree = new LineageTreeState(_session);
        Render(null);
    }

    private async void OpenChart_Click(object sender, RoutedEventArgs e)
    {
        if (!Current() || Charts.SelectedItem is not LineageChart chart) { StatusText.Text = "请选择图表。"; return; }
        try
        {
            var review = await _viewModel.ReviewLineageChartAsync(_preview, chart.Id);
            if (!Current()) return;
            if (review.GenerationChanged && !ConfirmReview(review)) return;
            _session = new LineageProjectionSession(_preview.Snapshot);
            _tree = new LineageTreeState(_session);
            _graphExpanded.Clear();
            _invalidChoices.Clear();
            foreach (var choice in review.Choices)
            {
                if (choice.Problem is not null) { _invalidChoices.Add(choice); _tree.Suppress(choice.Choice.PersonId); }
                else _tree.Choose(choice.Choice.PersonId, choice.Choice.RelationshipId);
            }
            _chartId = review.Chart.Id; _chartRevision = review.Chart.Revision; ChartName.Text = review.Chart.Name;
            RefreshInvalid(); Render(null);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private bool ConfirmReview(ChartReview review)
    {
        var dialog = new Window { Owner = this, Title = "复核图表", Width = 780, Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(12) };
        var message = new TextBlock { Text = "谱系数据已变化。请核对全部选线；确认后仅应用仍匹配的选线，失效项须单独处理。", TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(message, Dock.Top);
        panel.Children.Add(message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var confirm = new Button { Content = "确认复核", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(4), IsDefault = true };
        var cancel = new Button { Content = "取消", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(4), IsCancel = true };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(confirm); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        panel.Children.Add(new ListBox { ItemsSource = review.Choices, DisplayMemberPath = nameof(ChartChoiceReview.Label), Margin = new Thickness(0, 8, 0, 8) });
        dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }

    private void ClearInvalid_Click(object sender, RoutedEventArgs e)
    {
        if (!Current() || InvalidChoices.SelectedItem is not ChartChoiceReview choice) return;
        _invalidChoices.Remove(choice);
        if (_preview.Snapshot.People.Any(p => p.Id == choice.Choice.PersonId)) _session.Choose(choice.Choice.PersonId, null);
        _tree = new LineageTreeState(_session);
        RefreshInvalid(); Render(null);
    }

    private async void SaveChart_Click(object sender, RoutedEventArgs e)
    {
        if (!Current()) return;
        if (_invalidChoices.Count != 0) { StatusText.Text = "请先逐项重新选择或明确清除失效选线。"; return; }
        try
        {
            var chart = await _viewModel.SaveLineageChartAsync(_preview, _chartId, ChartName.Text, _chartRevision, _session.ExplicitSelections);
            _chartId = chart.Id; _chartRevision = chart.Revision;
            await RefreshCharts();
            StatusText.Text = "图表配置已保存。";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!Current() || _selected is null) return;
        var id = _selected.PersonId;
        _tree.Choose(id, null);
        Render(id);
        Locate(id);
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (SearchResults is not null && !_invalid) UpdateSearch(); }
    private void UpdateSearch() { SearchResults.ItemsSource = _tree.Search(SearchBox.Text); SearchResults.SelectedItem = null; }
    private void Locate_Click(object sender, RoutedEventArgs e)
    {
        if (!Current()) return;
        if (SearchResults.SelectedItem is not LineageTreeNode node) { StatusText.Text = "请先选择搜索结果。"; return; }
        Locate(node.PersonId);
    }
    private void Locate(Guid id)
    {
        if (LineageViews.SelectedIndex == 1)
        {
            var graphNode = _tree.Reveal(id);
            for (var parent = graphNode.Parent; parent is not null; parent = parent.Parent) _graphExpanded.Add(parent.PersonId);
            _selected = graphNode;
            StatusText.Text = _tree.Highlight(id);
            RenderDetails(); RenderGraph();
            var layout = _graphLayout!;
            var positioned = layout.Nodes.Single(n => n.Person.PersonId == id);
            GraphScroll.UpdateLayout();
            GraphScroll.ScrollToHorizontalOffset(positioned.X * GraphScale.ScaleX);
            GraphScroll.ScrollToVerticalOffset(positioned.Y * GraphScale.ScaleY);
            GraphScroll.UpdateLayout();
            RenderGraphViewport();
            return;
        }
        var node = _tree.Reveal(id);
        var path = new Stack<LineageTreeNode>();
        for (var item = node; item is not null; item = item.Parent) path.Push(item);
        ItemsControl owner = LineageTree;
        TreeViewItem? container = null;
        while (path.TryPop(out var item))
        {
            owner.UpdateLayout();
            container = owner.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (container is null) { StatusText.Text = "无法定位人物，请重新打开世系浏览。"; return; }
            if (path.Count > 0) container.IsExpanded = true;
            owner = container;
        }
        if (container is null) return;
        container.IsSelected = true;
        container.BringIntoView();
        container.Focus();
        _selected = node;
        StatusText.Text = _tree.Highlight(id);
        RenderDetails();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        if (!Current()) return;
        _tree.ClearSelection();
        _selected = null;
        StatusText.Text = "请选择人物。";
        RenderDetails();
        RenderGraph();
    }

    private void LineageViews_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, LineageViews)) RenderGraph();
    }

    private void RenderGraph()
    {
        if (_invalid || GraphCanvas is null || LineageViews?.SelectedIndex != 1) return;
        _graphLayout = LineageGraphLayoutService.Calculate(_tree, _graphExpanded);
        GraphCanvas.Width = _graphLayout.Width; GraphCanvas.Height = _graphLayout.Height;
        RenderGraphViewport();
    }

    private void GraphScroll_Changed(object sender, ScrollChangedEventArgs e) => RenderGraphViewport();
    private void GraphScroll_SizeChanged(object sender, SizeChangedEventArgs e) => RenderGraphViewport();

    private void RenderGraphViewport()
    {
        if (_invalid || _graphLayout is not { } layout || GraphScroll is null || GraphScale is null || LineageViews?.SelectedIndex != 1) return;
        var scale = GraphScale.ScaleX;
        var width = GraphScroll.ViewportWidth / scale;
        var height = GraphScroll.ViewportHeight / scale;
        if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height)) return;
        var viewport = new Rect(GraphScroll.HorizontalOffset / scale - width, GraphScroll.VerticalOffset / scale - height, width * 3, height * 3);
        GraphCanvas.Children.Clear();
        var positions = layout.Nodes.ToDictionary(n => n.Person.PersonId);
        var path = new HashSet<Guid>();
        for (var node = _selected; node?.Parent is not null; node = node.Parent) path.Add(node.PersonId);
        foreach (var edge in layout.Edges)
        {
            var parent = positions[edge.ParentId]; var child = positions[edge.ChildId];
            var start = new Point(parent.X + LineageGraphLayoutService.NodeWidth / 2, parent.Y + LineageGraphLayoutService.NodeHeight);
            var end = new Point(child.X + LineageGraphLayoutService.NodeWidth / 2, child.Y);
            if (!LineageGraphViewport.IntersectsLine(viewport, start, end)) continue;
            var highlighted = path.Contains(edge.ChildId);
            GraphCanvas.Children.Add(new Line { X1 = parent.X + LineageGraphLayoutService.NodeWidth / 2,
                Y1 = parent.Y + LineageGraphLayoutService.NodeHeight, X2 = child.X + LineageGraphLayoutService.NodeWidth / 2,
                Y2 = child.Y, Stroke = highlighted ? Brushes.DarkOrange : Brushes.Gray, StrokeThickness = highlighted ? 3 : 1.5,
                Tag = edge.RelationshipId, IsHitTestVisible = false });
        }
        foreach (var item in layout.Nodes)
        {
            if (!viewport.IntersectsWith(new Rect(item.X, item.Y, LineageGraphLayoutService.NodeWidth, LineageGraphLayoutService.NodeHeight))) continue;
            var node = item.Person;
            var panel = new Grid { Width = LineageGraphLayoutService.NodeWidth, Height = LineageGraphLayoutService.NodeHeight };
            panel.RowDefinitions.Add(new RowDefinition()); panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            var label = new Button { Tag = node.PersonId, Content = node.Projection.Person.Name +
                (node.Projection.NeedsChoice ? " [待选择]" : node.Projection.SelectedParent is null ? " [未记录上系]" : "") +
                (node.Projection.LostAncestry.Count > 0 ? " [失考]" : ""),
                ToolTip = node.Label, Background = node.PersonId == _selected?.PersonId ? Brushes.LightBlue :
                    node.IsPathAncestor ? Brushes.Moccasin : Brushes.White };
            label.Click += (_, _) => Locate(node.PersonId);
            panel.Children.Add(label);
            if (node.Children.Count > 0)
            {
                var toggle = new Button { Content = item.Expanded ? "折叠下系" : $"展开下系（{node.Children.Count}）", Tag = node.PersonId };
                Grid.SetRow(toggle, 1); panel.Children.Add(toggle);
                toggle.Click += (_, _) =>
                {
                    if (!_graphExpanded.Add(node.PersonId)) _graphExpanded.Remove(node.PersonId);
                    RenderGraph();
                };
            }
            Canvas.SetLeft(panel, item.X); Canvas.SetTop(panel, item.Y);
            GraphCanvas.Children.Add(panel);
        }
    }

    private void SetGraphZoom(double value) { GraphScale.ScaleX = GraphScale.ScaleY = Math.Clamp(value, 0.3, 2.0); GraphScroll.UpdateLayout(); RenderGraphViewport(); }
    private void GraphZoomIn_Click(object sender, RoutedEventArgs e) => SetGraphZoom(GraphScale.ScaleX + 0.1);
    private void GraphZoomOut_Click(object sender, RoutedEventArgs e) => SetGraphZoom(GraphScale.ScaleX - 0.1);
    private void GraphZoomReset_Click(object sender, RoutedEventArgs e) => SetGraphZoom(1.0);
}
