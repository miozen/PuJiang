using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PuJiang.V1.Application;

namespace ZupuPaiBan.Views;

// A repair never derives a proposed order from the damaged positions or from query order.
internal sealed class V1SpouseOrderRepairWindow : Window
{
    private readonly SpouseApplication _app;
    private readonly Guid _genealogy, _lineage, _father, _placement;
    private readonly RepairSnapshot _snapshot;
    private readonly List<Guid> _chosen = new();
    private readonly StackPanel _candidates = new();
    private readonly TextBlock _selection = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _save = new() { Content = "确认并保存修复", Padding = new Thickness(8), Margin = new Thickness(0, 8, 8, 0) };
    private readonly Button _undo = new() { Content = "撤销最后选择", Padding = new Thickness(8), Margin = new Thickness(0, 8, 8, 0) };
    private readonly Button _cancel = new() { Content = "取消", Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0) };
    private RepairSpouseOrder? _pending;
    private bool _saving, _uncertain;

    internal V1SpouseOrderRepairWindow(SpouseApplication app, Guid g, Guid l, Guid father, Guid placement, RepairSnapshot snapshot)
    {
        _app = app; _genealogy = g; _lineage = l; _father = father; _placement = placement; _snapshot = snapshot;
        Title = "修复配偶顺序 — 隔离样本"; Width = 560; Height = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "请按实际顺序逐一点击全部配偶。系统不会依据损坏位置或列表顺序推断；确认后保留修复前原始记录。", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_candidates); panel.Children.Add(_selection);
        var actions = new WrapPanel(); actions.Children.Add(_save); actions.Children.Add(_undo); actions.Children.Add(_cancel); panel.Children.Add(actions); panel.Children.Add(_status);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _save.Click += async (_, _) => await SaveAsync();
        _undo.Click += (_, _) => { if (_saving || _uncertain || _chosen.Count == 0) return; _chosen.RemoveAt(_chosen.Count - 1); _pending = null; Render(); };
        _cancel.Click += (_, _) => Close();
        Closing += OnClosing; Render();
    }
    private void Render()
    {
        _candidates.Children.Clear();
        foreach (var candidate in _snapshot.Candidates)
        {
            var button = new Button { Content = $"{(candidate.Name.Length == 0 ? "（空姓名）" : candidate.Name)} · {candidate.UnionId:N}", HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), IsEnabled = !_saving && !_uncertain && !_chosen.Contains(candidate.UnionId) };
            button.Click += (_, _) => { _chosen.Add(candidate.UnionId); _pending = null; Render(); };
            _candidates.Children.Add(button);
        }
        _selection.Text = "已指定：" + (_chosen.Count == 0 ? "无" : string.Join(" → ", _chosen.Select(id => { var x = _snapshot.Candidates.Single(c => c.UnionId == id); return (x.Name.Length == 0 ? "（空姓名）" : x.Name) + " · " + x.UnionId.ToString("N")[..8]; })));
        _save.IsEnabled = !_saving && _chosen.Count == _snapshot.Candidates.Count;
        _save.Content = _uncertain ? "重试同一修复" : "确认并保存修复";
        _undo.IsEnabled = !_saving && !_uncertain && _chosen.Count > 0; _cancel.IsEnabled = !_saving && !_uncertain;
    }
    private async Task SaveAsync()
    {
        if (_saving || _chosen.Count != _snapshot.Candidates.Count) return;
        _pending ??= new(Guid.NewGuid(), _genealogy, _lineage, _father, _placement, _snapshot.Evidence, _chosen.ToArray());
        _saving = true; Render();
        try
        {
            try { await _app.RepairAsync(_pending); }
            catch (Exception error)
            {
                try
                {
                    if (!await _app.HasRepairReceiptAsync(_pending.OperationId)) { _pending = null; _uncertain = false; _status.Text = $"未保存：{error.Message}。选择已保留；如原数据已变化，请取消并重新打开。"; return; }
                }
                catch { _uncertain = true; _status.Text = $"保存结果未确认：{error.Message}。请重试同一操作。"; return; }
            }
            _uncertain = false; _saving = false; DialogResult = true;
        }
        finally { _saving = false; Render(); }
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (_saving || _uncertain) { e.Cancel = true; _status.Text = "保存尚未确认，请等待或重试同一操作。"; } }
    internal IReadOnlyList<Button> CandidateButtonsForTest => _candidates.Children.OfType<Button>().ToArray();
    internal Button SaveButtonForTest => _save;
}
