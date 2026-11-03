using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class LineageTreeDialog : Window
{
    private readonly GenealogySelectionViewModel _viewModel;
    private readonly LineagePreviewContext _preview;
    private readonly LineageTreeState _tree;
    private bool _invalid;
    private bool _rendering;
    private LineageTreeNode? _selected;

    public LineageTreeDialog(GenealogySelectionViewModel viewModel, LineagePreviewContext preview)
    {
        _viewModel = viewModel;
        _preview = preview;
        _tree = new LineageTreeState(new LineageProjectionSession(preview.Snapshot));
        InitializeComponent();
        _viewModel.PropertyChanged += ViewModelChanged;
        _preview.Tree.CollectionChanged += TreeChanged;
        Closed += (_, _) => { _viewModel.PropertyChanged -= ViewModelChanged; _preview.Tree.CollectionChanged -= TreeChanged; };
        Render(null);
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
    }
    private void RenderDetails()
    {
        if (_invalid) return;
        var row = _selected?.Projection;
        CandidateList.ItemsSource = row?.ParentCandidates;
        CandidateList.SelectedItem = row?.SelectedParent;
        var lost = row is null ? "" : string.Join("\n", row.LostAncestry.Select(r =>
            $"谱载上系失考（关系 ID: {r.Id}）；备注：{r.Note ?? "（未填写）"}；来源：{r.SourceCitation ?? "（未填写）"}"));
        DetailsText.Text = row is null ? "请选择人物。" : string.IsNullOrEmpty(lost) ? "没有上系失考记录。" : lost;
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        if (ChooseButton is null || ClearButton is null) return;
        var row = _selected?.Projection;
        ChooseButton.IsEnabled = !_invalid && row?.ParentCandidates.Count > 1 && CandidateList.SelectedItem is LineageParentOption;
        ClearButton.IsEnabled = !_invalid && row?.ParentCandidates.Count > 1 && row.SelectedParent is not null;
    }
    private void LineageTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_invalid || _rendering) return;
        _selected = e.NewValue as LineageTreeNode;
        var boundary = _tree.Highlight(_selected?.PersonId);
        StatusText.Text = string.IsNullOrEmpty(boundary) ? "请选择人物。" : boundary;
        RenderDetails();
    }
    private void CandidateList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (!Current() || _selected is null || CandidateList.SelectedItem is not LineageParentOption option) return;
        var id = _selected.PersonId;
        _tree.Choose(id, option.Relationship.Id);
        Render(id);
        Locate(id);
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
    }
}
