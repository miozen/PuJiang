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
        PreviewBox.Text = SelectedCandidate is { } candidate ? FormatPreview(candidate) : "请选择一条记录查看详情。";
        UpdateRemoveButton();
    }

    private static string FormatPreview(TargetLinkRemovalCandidate candidate)
    {
        static string Value(string? value) => string.IsNullOrEmpty(value) ? "（未填写）" : value;
        static string Person(string? name, Guid? id) => id is Guid personId ? $"{name}（ID: {personId}）" : "（未详）";
        if (candidate.RelationshipSnapshot is { } relationship)
            return $"{candidate.Description}\n记录 ID: {relationship.Id}\n主体: {Person(candidate.FirstName, relationship.PersonId)}\n对方: {Person(candidate.SecondName, relationship.RelatedPersonId)}\n谱载首选标记: {(relationship.IsPreferredForLineage ? "是" : "否")}\n备注: {Value(relationship.Note)}\n来源: {Value(relationship.SourceCitation)}";
        if (candidate.UnionSnapshot is { } union)
            return $"{candidate.Description}\n记录 ID: {union.Id}\n第一方: {Person(candidate.FirstName, union.PartnerOneId)}\n第二方: {Person(candidate.SecondName, union.PartnerTwoId)}\n起始时间: {Value(union.StartDateText)}\n结束时间: {Value(union.EndDateText)}\n备注: {Value(union.Note)}";
        return "记录详情不可用，请关闭窗口重新选择。";
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
