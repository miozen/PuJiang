using System.Windows;
using System.Windows.Controls;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class TargetRelationshipDetailsDialog : Window
{
    public TargetLinkRemovalCandidate? SelectedCandidate => CandidateList.SelectedItem as TargetLinkRemovalCandidate;
    public string? Note => NoteBox.Text.Length == 0 && SelectedCandidate?.RelationshipSnapshot?.Note != string.Empty ? null : NoteBox.Text;
    public string? SourceCitation => SourceCitationBox.Text.Length == 0 && SelectedCandidate?.RelationshipSnapshot?.SourceCitation != string.Empty ? null : SourceCitationBox.Text;

    public TargetRelationshipDetailsDialog(IReadOnlyCollection<TargetLinkRemovalCandidate> candidates)
    {
        var first = candidates.FirstOrDefault() ?? throw new ArgumentException("关系候选不能为空。", nameof(candidates));
        if (candidates.Any(c => c.Kind != TargetLinkRemovalKind.Relationship || c.RelationshipSnapshot is null ||
            c.GenealogyId != first.GenealogyId || c.ParticipantId != first.ParticipantId))
            throw new ArgumentException("关系候选无效。", nameof(candidates));
        InitializeComponent();
        SubjectLabel.Text = $"当前人物：{first.SubjectName}（ID: {first.ParticipantId}）";
        CandidateList.ItemsSource = candidates.OrderBy(c => c.Description, StringComparer.Ordinal).ThenBy(c => c.Id).ToList();
    }

    private void CandidateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var relation = SelectedCandidate?.RelationshipSnapshot;
        NoteBox.Text = relation?.Note ?? string.Empty;
        SourceCitationBox.Text = relation?.SourceCitation ?? string.Empty;
        SaveButton.IsEnabled = relation is not null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCandidate is not null) DialogResult = true;
    }
}
