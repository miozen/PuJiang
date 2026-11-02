using System.Windows;
using ZupuPaiBan.Models;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class ParentRelationshipCorrectionDialog : Window
{
    public TargetLinkRemovalCandidate? SelectedCandidate => CandidateList.SelectedItem as TargetLinkRemovalCandidate;
    public PersonRelationshipKind? NewKind => KindBox.SelectedValue as PersonRelationshipKind?;
    public ParentRelationshipCorrectionDialog(IReadOnlyList<TargetLinkRemovalCandidate> candidates)
    {
        InitializeComponent();
        var first = candidates.FirstOrDefault() ?? throw new ArgumentException("没有父母关系。", nameof(candidates));
        if (candidates.Any(c => c.GenealogyId != first.GenealogyId || c.ParticipantId != first.ParticipantId ||
            c.RelationshipSnapshot?.Kind is not (PersonRelationshipKind.BiologicalParent or PersonRelationshipKind.AdoptiveParent or PersonRelationshipKind.HeirParent)))
            throw new ArgumentException("父母关系候选无效。", nameof(candidates));
        SubjectLabel.Text = $"当前人物：{first.SubjectName}（ID: {first.ParticipantId}）。请选择含方向的关系。";
        CandidateList.ItemsSource = candidates.Select(c => c).OrderBy(c => c.Description, StringComparer.Ordinal).ToList();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCandidate?.RelationshipSnapshot is not { } relation || NewKind is not { } kind || kind == relation.Kind)
        { MessageBox.Show(this, "请选择一条关系及不同的父母类型。", "提示"); return; }
        var target = ((System.Windows.Controls.ComboBoxItem)KindBox.SelectedItem).Content;
        if (MessageBox.Show(this, $"关系：{SelectedCandidate.Description}\n记录 ID：{relation.Id}\n更正为：{target}\n\n确定更正吗？", "确认更正", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            DialogResult = true;
    }
}
