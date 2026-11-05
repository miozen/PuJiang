using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.Views;

/// <summary>Limited real-data profile editor. The separately available lineage canvas remains read-only.</summary>
public sealed class WorkbenchProfileWindow : Window
{
    private sealed record Option(Guid Id, string Name);
    private readonly TargetContextCoordinator _coordinator;
    private readonly WorkbenchDraftSession _session;
    private readonly ListBox _genealogies = new() { DisplayMemberPath = nameof(Option.Name) };
    private readonly ListBox _people = new() { DisplayMemberPath = nameof(Option.Name) };
    private readonly TextBox _name = new();
    private readonly TextBox _birth = new();
    private readonly TextBox _death = new();
    private readonly TextBox _biography = new() { AcceptsReturn = true, Height = 120, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _sex = new() { ItemsSource = Enum.GetValues<PersonSex>() };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private Guid? _genealogy;
    private Guid? _person;
    private bool _updating;
    private bool _busy;
    private bool _approvedClose;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Func<DraftLeaveChoice>? ChooseForTest { get; set; }
    internal WorkbenchDraftSession SessionForTest => _session;
    internal string StatusForTest => _status.Text;
    internal Func<Guid, Task>? AfterCommitAsync { set => _session.AfterCommitAsync = value; }
    internal Task InitializeForTest() => _loaded.Task;
    internal async Task SelectGenealogyForTest(Guid id)
    {
        _updating = true; _genealogies.SelectedItem = _genealogies.Items.OfType<Option>().Single(x => x.Id == id); _updating = false;
        await ChangeGenealogyAsync();
    }
    internal async Task SelectPersonForTest(Guid id)
    {
        _updating = true; _people.SelectedItem = _people.Items.OfType<Option>().Single(x => x.Id == id); _updating = false;
        await ChangePersonAsync();
    }
    internal void SetNameForTest(string name) => _name.Text = name;
    internal Task SaveForTest() => SaveAsync();

    public WorkbenchProfileWindow(TargetContextCoordinator coordinator) : this(coordinator, WorkbenchDraftSession.Create(coordinator)) { }
    internal WorkbenchProfileWindow(TargetContextCoordinator coordinator, WorkbenchDraftSession session)
    {
        _coordinator = coordinator; _session = session;
        Title = "世系编修工作台 — 人物档案试用"; Width = 1100; Height = 720; MinWidth = 760; MinHeight = 480;
        var outer = new DockPanel(); DockPanel.SetDock(_status, Dock.Bottom); outer.Children.Add(_status);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var left = new DockPanel { Margin = new Thickness(12) };
        var title = new TextBlock { Text = "谱系管理", FontSize = 17, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(title, Dock.Top); left.Children.Add(title); left.Children.Add(_genealogies);
        columns.Children.Add(left);
        var center = new DockPanel { Margin = new Thickness(8) };
        var notice = new TextBlock { Text = "选择人物编辑档案。此试用入口尚未接入世系画布；顺序不代表谱载排行。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(notice, Dock.Top); center.Children.Add(notice); center.Children.Add(_people);
        Grid.SetColumn(center, 1); columns.Children.Add(center);
        var right = new StackPanel { Margin = new Thickness(12) };
        right.Children.Add(new TextBlock { Text = "人物资料", FontSize = 17 });
        AddField(right, "姓名", _name); AddField(right, "性别", _sex); AddField(right, "出生", _birth);
        AddField(right, "逝世", _death); AddField(right, "生平", _biography);
        var save = new Button { Content = "保存档案", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8) };
        save.Click += async (_, _) => await SaveAsync(); right.Children.Add(save);
        Grid.SetColumn(right, 2); columns.Children.Add(right); outer.Children.Add(columns); Content = outer;
        _genealogies.SelectionChanged += async (_, _) => await ChangeGenealogyAsync();
        _people.SelectionChanged += async (_, _) => await ChangePersonAsync();
        _name.TextChanged += (_, _) => UpdateDraft(); _birth.TextChanged += (_, _) => UpdateDraft();
        _death.TextChanged += (_, _) => UpdateDraft(); _biography.TextChanged += (_, _) => UpdateDraft();
        _sex.SelectionChanged += (_, _) => UpdateDraft();
        Loaded += async (_, _) => { try { await LoadGenealogiesAsync(); } finally { _loaded.TrySetResult(); } };
        Closing += OnClosing;
        ShowDraft();
    }
    private static void AddField(Panel panel, string label, Control field)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) }); panel.Children.Add(field);
    }
    private DraftLeaveChoice Choose() => _session.Draft?.IsDirty != true ? DraftLeaveChoice.Discard :
        ChooseForTest?.Invoke() ?? MessageBox.Show(this, "人物档案有未保存修改。是否保存？\n选择“否”放弃，选择“取消”继续编辑。", "未保存的档案", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        { MessageBoxResult.Yes => DraftLeaveChoice.Save, MessageBoxResult.No => DraftLeaveChoice.Discard, _ => DraftLeaveChoice.Cancel };
    private void UpdateDraft()
    {
        if (_updating || _session.Draft is not { } draft || _sex.SelectedItem is not PersonSex sex) return;
        draft.Values = new(_name.Text, sex, _birth.Text, _death.Text, _biography.Text);
    }
    private void ShowDraft()
    {
        _updating = true;
        try
        {
            var v = _session.Draft?.Values;
            _name.Text = v?.Name ?? ""; _sex.SelectedItem = v?.Sex; _birth.Text = v?.BirthDate ?? "";
            _death.Text = v?.DeathDate ?? ""; _biography.Text = v?.Biography ?? "";
            _name.IsEnabled = _sex.IsEnabled = _birth.IsEnabled = _death.IsEnabled = _biography.IsEnabled = v is not null;
        }
        finally { _updating = false; }
    }
    private async Task LoadGenealogiesAsync()
    {
        try
        {
            using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
            var list = await context.Genealogies.AsNoTracking().OrderBy(g => g.Name).Select(g => new Option(g.Id, g.Name)).ToListAsync();
            _updating = true; _genealogies.ItemsSource = list; _updating = false;
            _status.Text = "请选择谱系和人物。";
        }
        catch (Exception ex) { _status.Text = $"读取谱系失败：{ex.Message}"; }
    }
    private async Task ChangeGenealogyAsync()
    {
        if (_updating || _busy || _genealogies.SelectedItem is not Option next || next.Id == _genealogy) return;
        _busy = true; IsEnabled = false;
        var dirty = _session.Draft?.IsDirty == true;
        var choice = Choose(); var saved = false;
        try
        {
            var result = await _session.LeaveAsync(choice);
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); return; }
            saved = dirty && choice == DraftLeaveChoice.Save;
            _person = null; ShowDraft();
            using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
            var people = await context.People.AsNoTracking().Where(p => p.GenealogyId == next.Id).OrderBy(p => p.Name)
                .Select(p => new Option(p.Id, p.Name)).ToListAsync();
            _genealogy = next.Id; _person = null; _updating = true; _people.ItemsSource = people; _people.SelectedItem = null; _updating = false;
            ShowDraft(); _status.Text = "请选择人物。";
        }
        catch (Exception ex) { _status.Text = saved ? $"档案已保存，但切换谱系失败：{ex.Message}" : $"切换谱系失败：{ex.Message}"; RestoreSelection(); }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task ChangePersonAsync()
    {
        if (_updating || _busy || _genealogy is not Guid genealogy || _people.SelectedItem is not Option next || next.Id == _person) return;
        _busy = true; IsEnabled = false;
        try
        {
            var prior = _session.Draft;
            var choice = Choose();
            var result = await _session.SelectAsync(genealogy, next.Id, choice);
            if (prior?.IsSaved == true && choice == DraftLeaveChoice.Save && result is DraftTransitionResult.Completed or DraftTransitionResult.SavedThenReadFailed)
            {
                var items = _people.Items.OfType<Option>().Select(item => item.Id == prior.Original.PersonId ? item with { Name = prior.Values.Name.Trim() } : item).ToList();
                _updating = true; _people.ItemsSource = items; _people.SelectedItem = items.FirstOrDefault(item => item.Id == next.Id); _updating = false;
            }
            if (result != DraftTransitionResult.Completed) { Report(result); RestoreSelection(); ShowDraft(); return; }
            _person = next.Id; ShowDraft(); _status.Text = "修改后请显式保存。";
        }
        catch (Exception ex) { _status.Text = $"选择人物失败：{ex.Message}"; RestoreSelection(); }
        finally { IsEnabled = true; _busy = false; }
    }
    private void RestoreSelection()
    {
        _updating = true;
        _genealogies.SelectedItem = _genealogies.Items.OfType<Option>().FirstOrDefault(o => o.Id == _genealogy);
        _people.SelectedItem = _people.Items.OfType<Option>().FirstOrDefault(o => o.Id == _person);
        _updating = false;
    }
    private void Report(DraftTransitionResult result)
    {
        _status.Text = result switch
        {
            DraftTransitionResult.Cancelled => "已取消，保留当前草稿。",
            DraftTransitionResult.SaveFailed => $"未保存，草稿保留：{_session.Error}",
            DraftTransitionResult.SavedRefreshFailed => $"已保存，但刷新失败，请重新选择人物：{_session.Error}",
            DraftTransitionResult.ReadFailed => $"读取人物失败：{_session.Error}",
            DraftTransitionResult.SavedThenReadFailed => $"档案已保存，但读取所选人物失败：{_session.Error}",
            _ => "操作完成。"
        };
        if (result == DraftTransitionResult.SavedRefreshFailed)
        {
            _genealogy = null; _updating = true; _people.ItemsSource = null; _updating = false;
            _status.Text += " 请重新选择谱系。";
        }
        if (result is DraftTransitionResult.SavedRefreshFailed or DraftTransitionResult.ReadFailed or DraftTransitionResult.SavedThenReadFailed) { _person = null; RestoreSelection(); }
    }
    private async Task SaveAsync()
    {
        if (_busy || _session.Draft is null) return;
        _busy = true; IsEnabled = false;
        try
        {
            var dirty = _session.Draft.IsDirty;
            var result = await _session.SaveAsync(); Report(result);
            if (result == DraftTransitionResult.Completed && dirty && _genealogy is Guid genealogy)
            {
                try
                {
                    using var operation = _coordinator.EnterOperation(); await using var context = _coordinator.CreateContext();
                    var people = await context.People.AsNoTracking().Where(p => p.GenealogyId == genealogy).OrderBy(p => p.Name)
                        .Select(p => new Option(p.Id, p.Name)).ToListAsync();
                    _updating = true; _people.ItemsSource = people; _people.SelectedItem = people.FirstOrDefault(p => p.Id == _person); _updating = false;
                    _status.Text = "档案已保存。";
                }
                catch (Exception ex)
                {
                    _updating = true; _people.ItemsSource = null; _updating = false;
                    _status.Text = $"档案已保存，但人物列表刷新失败：{ex.Message}";
                }
            }
            else if (result == DraftTransitionResult.Completed) _status.Text = "没有需要保存的修改。";
            ShowDraft();
        }
        catch (Exception ex) { _status.Text = $"保存失败：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    internal async Task<bool> PrepareForOwnerExitAsync()
    {
        if (_approvedClose) return true;
        if (_busy) { _status.Text = "请等待当前操作结束后再关闭。"; return false; }
        _busy = true; IsEnabled = false;
        try
        {
            var result = await _session.LeaveAsync(Choose());
            if (result != DraftTransitionResult.Completed) { Report(result); ShowDraft(); return false; }
            _approvedClose = true; return true;
        }
        catch (Exception ex) { _status.Text = $"无法关闭：{ex.Message}"; return false; }
        finally { if (!_approvedClose) IsEnabled = true; _busy = false; }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_approvedClose) return;
        e.Cancel = true;
        if (await PrepareForOwnerExitAsync()) _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
