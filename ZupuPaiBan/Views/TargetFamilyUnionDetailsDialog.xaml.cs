using System.Windows;
using System.Windows.Controls;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class TargetFamilyUnionDetailsDialog : Window
{
    public TargetLinkRemovalCandidate? SelectedCandidate => CandidateList.SelectedItem as TargetLinkRemovalCandidate;
    public string? StartDateText => StartDateBox.Text.Length == 0 && SelectedCandidate?.UnionSnapshot?.StartDateText != string.Empty ? null : StartDateBox.Text;
    public string? EndDateText => EndDateBox.Text.Length == 0 && SelectedCandidate?.UnionSnapshot?.EndDateText != string.Empty ? null : EndDateBox.Text;
    public string? Note => NoteBox.Text.Length == 0 && SelectedCandidate?.UnionSnapshot?.Note != string.Empty ? null : NoteBox.Text;

    public TargetFamilyUnionDetailsDialog(IReadOnlyCollection<TargetLinkRemovalCandidate> candidates)
    {
        var first = candidates.FirstOrDefault() ?? throw new ArgumentException("婚配候选不能为空。", nameof(candidates));
        if (candidates.Any(c => c.Kind != TargetLinkRemovalKind.FamilyUnion || c.UnionSnapshot is null ||
            c.GenealogyId != first.GenealogyId || c.ParticipantId != first.ParticipantId))
            throw new ArgumentException("婚配候选无效。", nameof(candidates));
        InitializeComponent();
        SubjectLabel.Text = $"当前人物：{first.SubjectName}（ID: {first.ParticipantId}）";
        CandidateList.ItemsSource = candidates.OrderBy(c => c.Description, StringComparer.Ordinal).ThenBy(c => c.Id).ToList();
    }

    private void CandidateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var union = SelectedCandidate?.UnionSnapshot;
        StartDateBox.Text = union?.StartDateText ?? string.Empty;
        EndDateBox.Text = union?.EndDateText ?? string.Empty;
        NoteBox.Text = union?.Note ?? string.Empty;
        SaveButton.IsEnabled = union is not null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCandidate is not null) DialogResult = true;
    }
}
