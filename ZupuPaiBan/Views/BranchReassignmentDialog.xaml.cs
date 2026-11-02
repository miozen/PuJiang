using System.Windows;
using ZupuPaiBan.ViewModels;

namespace ZupuPaiBan.Views;

public partial class BranchReassignmentDialog : Window
{
    public sealed record Destination(Guid? Id, string Label);
    private readonly BranchReassignmentCandidate _candidate;
    public Guid? ChildDestination => ((Destination)ChildTarget.SelectedItem).Id;
    public Guid? PersonDestination => ((Destination)PersonTarget.SelectedItem).Id;

    public BranchReassignmentDialog(BranchReassignmentCandidate candidate, IReadOnlyList<BranchChoice> choices)
    {
        _candidate = candidate;
        InitializeComponent();
        var children = candidate.Snapshot.Branches.Where(b => b.ParentBranchId == candidate.Snapshot.Source.Id).ToList();
        var labels = choices.ToDictionary(c => c.Id, c => c.Label);
        Preview.Text = $"删除房支：{candidate.Choice.Label}\n\n直属子房支（{children.Count}）：\n" +
            (children.Count == 0 ? "（无）" : string.Join("\n", children.Select(c => labels[c.Id]))) +
            $"\n\n直属人物（{candidate.Snapshot.People.Count}）：\n" +
            (candidate.Snapshot.People.Count == 0 ? "（无）" : string.Join("\n", candidate.Snapshot.People.Select(p => $"{p.Name}（ID: {p.Id}）"))) +
            "\n\n子房支的下级和人物归属会保留。";
        var available = choices.Where(c => c.Id != candidate.Snapshot.Source.Id).Select(c => new Destination(c.Id, c.Label)).ToArray();
        ChildTarget.ItemsSource = new[] { new Destination(null, "顶层") }.Concat(available).ToArray();
        PersonTarget.ItemsSource = new[] { new Destination(null, "未归属") }.Concat(available).ToArray();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (ChildTarget.SelectedItem is not Destination child || PersonTarget.SelectedItem is not Destination person)
        {
            MessageBox.Show(this, "请分别选择子房支和人物的安置位置。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var children = _candidate.Snapshot.Branches.Where(b => b.ParentBranchId == _candidate.Snapshot.Source.Id).ToList();
        var text = $"删除：{_candidate.Choice.Label}\n\n直属子房支移动到：{child.Label}\n" +
            (children.Count == 0 ? "（无）" : string.Join("\n", children.Select(b => $"{b.Name}（ID: {b.Id}）"))) +
            $"\n\n直属人物归属到：{person.Label}\n" +
            (_candidate.Snapshot.People.Count == 0 ? "（无）" : string.Join("\n", _candidate.Snapshot.People.Select(p => $"{p.Name}（ID: {p.Id}）"))) +
            "\n\n房支记录将被删除，无法撤销。确定继续吗？";
        if (MessageBox.Show(this, text, "确认删除房支", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            DialogResult = true;
    }
}
