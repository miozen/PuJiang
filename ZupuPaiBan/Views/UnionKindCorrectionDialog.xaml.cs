using System.Windows;
using ZupuPaiBan.Models;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class UnionKindCorrectionDialog : Window
{
    public TargetLinkRemovalCandidate? SelectedCandidate => CandidateList.SelectedItem as TargetLinkRemovalCandidate;
    public FamilyUnionKind? NewKind => KindBox.SelectedValue as FamilyUnionKind?;
    public UnionKindCorrectionDialog(IReadOnlyList<TargetLinkRemovalCandidate> candidates)
    {
        InitializeComponent();
        var first = candidates.FirstOrDefault() ?? throw new ArgumentException("没有婚配记录。", nameof(candidates));
        if (candidates.Any(c => c.Kind != TargetLinkRemovalKind.FamilyUnion || c.UnionSnapshot is null ||
            c.GenealogyId != first.GenealogyId || c.ParticipantId != first.ParticipantId))
            throw new ArgumentException("婚配候选无效。", nameof(candidates));
        SubjectLabel.Text = $"当前人物：{first.SubjectName}（ID: {first.ParticipantId}）。请选择婚配。";
        CandidateList.ItemsSource = candidates.OrderBy(c => c.Description, StringComparer.Ordinal).ThenBy(c => c.Id).ToList();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCandidate?.UnionSnapshot is not { } union || NewKind is not { } kind || kind == union.Kind)
        { MessageBox.Show(this, "请选择一条婚配及不同的类型。", "提示"); return; }
        var target = ((System.Windows.Controls.ComboBoxItem)KindBox.SelectedItem).Content;
        if (MessageBox.Show(this, $"婚配：{SelectedCandidate.Description}\n记录 ID：{union.Id}\n更正为：{target}\n\n确定更正吗？", "确认更正婚配类型", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            DialogResult = true;
    }
}
