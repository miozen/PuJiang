using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class AncestryPreviewDialog : Window
{
    private readonly GenealogySelectionViewModel _viewModel;
    private readonly AncestryPreviewContext _preview;
    private readonly AncestryPathSession _session;
    private bool _invalid;

    public AncestryPreviewDialog(GenealogySelectionViewModel viewModel, AncestryPreviewContext preview)
    {
        _viewModel = viewModel;
        _preview = preview;
        _session = new AncestryPathSession(preview.Snapshot, preview.Selection.PersonId);
        InitializeComponent();
        _viewModel.PropertyChanged += ViewModelChanged;
        _preview.Tree.CollectionChanged += TreeChanged;
        Closed += (_, _) => { _viewModel.PropertyChanged -= ViewModelChanged; _preview.Tree.CollectionChanged -= TreeChanged; };
        UpdateView();
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_viewModel.IsAncestryPreviewCurrent(_preview)) InvalidatePreview();
    }

    private void TreeChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidatePreview();

    private void InvalidatePreview()
    {
        _invalid = true;
        PathList.ItemsSource = null;
        ChoiceList.ItemsSource = null;
        ContinueButton.IsEnabled = false;
        StatusText.Text = "族谱或人物选择已变化，预览已失效；请关闭后重新打开。";
    }

    private void UpdateView()
    {
        if (_invalid || !_viewModel.IsAncestryPreviewCurrent(_preview)) { InvalidatePreview(); return; }
        PathList.ItemsSource = _session.Path;
        ChoiceList.ItemsSource = _session.Choices;
        StatusText.Text = _session.Status;
        ContinueButton.IsEnabled = false;
    }

    private void ChoiceList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ContinueButton.IsEnabled = !_invalid && ChoiceList.SelectedItem is AncestryChoice;

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_invalid || !_viewModel.IsAncestryPreviewCurrent(_preview)) { InvalidatePreview(); return; }
        if (ChoiceList.SelectedItem is not AncestryChoice choice) return;
        _session.Choose(choice.RelationshipId);
        UpdateView();
    }
}
