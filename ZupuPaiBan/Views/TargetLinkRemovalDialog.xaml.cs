using System.Windows;
using System.Windows.Controls;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

/// <summary>Requires an explicit, per-record confirmation before a target-model unlink can be submitted.</summary>
public partial class TargetLinkRemovalDialog : Window
{
    public TargetLinkRemovalCandidate? SelectedCandidate => CandidateList.SelectedItem as TargetLinkRemovalCandidate;

    public TargetLinkRemovalDialog(IReadOnlyCollection<TargetLinkRemovalCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var subject = candidates.FirstOrDefault() ?? throw new ArgumentException("解除候选不能为空。", nameof(candidates));
        if (candidates.Any(candidate => candidate.GenealogyId != subject.GenealogyId ||
            candidate.ParticipantId != subject.ParticipantId || candidate.SubjectName != subject.SubjectName))
            throw new ArgumentException("解除候选的主体不一致。", nameof(candidates));
        InitializeComponent();
        SubjectLabel.Text = $"当前人物：{subject.SubjectName}（ID: {subject.ParticipantId}）";
        CandidateList.ItemsSource = candidates.OrderBy(candidate => candidate.Kind).ThenBy(candidate => candidate.Description, StringComparer.Ordinal).ToList();
    }

    private void CandidateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A changed selection requires a fresh acknowledgement for the exact record now shown.
        ConfirmationBox.IsChecked = false;
        UpdateRemoveButton();
    }

    private void ConfirmationChanged(object sender, RoutedEventArgs e) => UpdateRemoveButton();

    private void UpdateRemoveButton() =>
        RemoveButton.IsEnabled = SelectedCandidate is not null && ConfirmationBox.IsChecked == true;

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCandidate is null || ConfirmationBox.IsChecked != true) return;
        DialogResult = true;
    }
}
