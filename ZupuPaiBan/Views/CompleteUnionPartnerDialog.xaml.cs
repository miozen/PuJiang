using System.Windows;
using ZupuPaiBan.Models;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class CompleteUnionPartnerDialog : Window
{
    public TargetLinkRemovalCandidate? SelectedCandidate => CandidateList.SelectedItem as TargetLinkRemovalCandidate;
    public Person? SelectedPartner => PartnerBox.SelectedItem as Person;
    public CompleteUnionPartnerDialog(IReadOnlyList<TargetLinkRemovalCandidate> candidates, IReadOnlyList<Person> people)
    {
        InitializeComponent();
        var first = candidates.FirstOrDefault() ?? throw new ArgumentException("没有未详婚配。", nameof(candidates));
        if (candidates.Any(c => c.GenealogyId != first.GenealogyId || c.ParticipantId != first.ParticipantId ||
            c.Kind != TargetLinkRemovalKind.FamilyUnion || c.UnionSnapshot is not { PartnerTwoId: null }))
            throw new ArgumentException("婚配候选无效。", nameof(candidates));
        SubjectLabel.Text = $"当前人物：{first.SubjectName}（ID: {first.ParticipantId}）。请选择未详婚配和对方。";
        CandidateList.ItemsSource = candidates.OrderBy(c => c.Description, StringComparer.Ordinal).ThenBy(c => c.Id).ToList();
        PartnerBox.ItemsSource = people.Where(p => p.GenealogyId == first.GenealogyId && p.Id != first.ParticipantId)
            .OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Id).ToList();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCandidate is not { } candidate || SelectedPartner is not { } partner)
        { MessageBox.Show(this, "请选择婚配和对方人物。", "提示"); return; }
        if (MessageBox.Show(this, $"婚配：{candidate.Description}\n记录 ID：{candidate.Id}\n补全对方：{partner.Name}（ID: {partner.Id}）\n\n确定补全吗？", "确认补全", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            DialogResult = true;
    }
}
