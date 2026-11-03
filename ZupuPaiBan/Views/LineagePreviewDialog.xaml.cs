using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class LineagePreviewDialog : Window
{
    private readonly GenealogySelectionViewModel _viewModel;
    private readonly LineagePreviewContext _preview;
    private readonly LineageProjectionSession _session;
    private bool _invalid;
    private bool _rendering;

    public LineagePreviewDialog(GenealogySelectionViewModel viewModel, LineagePreviewContext preview)
    {
        _viewModel = viewModel;
        _preview = preview;
        _session = new LineageProjectionSession(preview.Snapshot);
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
        PersonList.ItemsSource = null;
        CandidateList.ItemsSource = null;
        DetailsText.Text = string.Empty;
        ChooseButton.IsEnabled = false;
        ClearButton.IsEnabled = false;
        StatusText.Text = "族谱已切换或刷新，预览已失效；请关闭后重新打开。";
    }

    private void Render(Guid? selectedPersonId)
    {
        if (_invalid || !_viewModel.IsLineagePreviewCurrent(_preview)) { InvalidatePreview(); return; }
        _rendering = true;
        var rows = _session.Project();
        PersonList.ItemsSource = rows;
        PersonList.SelectedItem = selectedPersonId is Guid id ? rows.SingleOrDefault(row => row.Person.Id == id) : null;
        _rendering = false;
        StatusText.Text = $"共 {rows.Count} 人；{rows.Count(row => row.NeedsChoice)} 人的父母关系待选择。选择仅在本次窗口有效。";
        RenderDetails();
    }

    private void RenderDetails()
    {
        if (_invalid) return;
        var row = PersonList.SelectedItem as LineagePersonProjection;
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
        var row = PersonList.SelectedItem as LineagePersonProjection;
        ChooseButton.IsEnabled = !_invalid && row?.ParentCandidates.Count > 1 && CandidateList.SelectedItem is LineageParentOption;
        ClearButton.IsEnabled = !_invalid && row?.ParentCandidates.Count > 1 && row.SelectedParent is not null;
    }
    private void PersonList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_rendering) RenderDetails(); }
    private void CandidateList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (_invalid || !_viewModel.IsLineagePreviewCurrent(_preview)) { InvalidatePreview(); return; }
        if (PersonList.SelectedItem is not LineagePersonProjection row || CandidateList.SelectedItem is not LineageParentOption option) return;
        _session.Choose(row.Person.Id, option.Relationship.Id);
        Render(row.Person.Id);
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_invalid || !_viewModel.IsLineagePreviewCurrent(_preview)) { InvalidatePreview(); return; }
        if (PersonList.SelectedItem is not LineagePersonProjection row) return;
        _session.Choose(row.Person.Id, null);
        Render(row.Person.Id);
    }
}
