using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.Views;

/// <summary>Limited workbench with an explicitly placed single-person canvas and profile draft.</summary>
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
    private readonly TextBox _column = new() { Width = 50, Text = "1", Margin = new Thickness(5, 0, 5, 0) };
    private readonly Canvas _canvas = new() { Background = Brushes.White, Width = 650, Height = 360 };
    private readonly DispatcherTimer _canvasMonitor = new() { Interval = TimeSpan.FromSeconds(1) };
    private WorkbenchProjectionSession? _projection;
    private long _projectionRevision;
    private ProjectionReadSession.PersonRow? _canvasPerson;
    private int _canvasColumn;
    private Func<Guid, Task>? _afterCommit;
    private Guid? _genealogy;
    private Guid? _person;
    private bool _updating;
    private bool _busy;
    private bool _approvedClose;
    private bool _canvasPolling;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Func<DraftLeaveChoice>? ChooseForTest { get; set; }
    internal WorkbenchDraftSession SessionForTest => _session;
    internal string StatusForTest => _status.Text;
    internal Func<Guid, Task>? AfterCommitAsync { set => _afterCommit = value; }
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
    internal async Task PlacePersonForTest(int column) { _column.Text = column.ToString(); await PlacePersonAsync(); }
    internal Guid? CanvasPersonForTest => _canvasPerson?.Id;
    internal string? CanvasNameForTest => _canvasPerson?.Name;
    internal int CanvasColumnForTest => _canvasColumn;
    internal Task PollCanvasForTest() => PollCanvasAsync();
    internal Task SelectCanvasForTest() => SelectCanvasPersonAsync();

    public WorkbenchProfileWindow(TargetContextCoordinator coordinator) : this(coordinator, WorkbenchDraftSession.Create(coordinator)) { }
    internal WorkbenchProfileWindow(TargetContextCoordinator coordinator, WorkbenchDraftSession session)
    {
        _coordinator = coordinator; _session = session;
        Title = "世系编修工作台 — 单人物画布试用"; Width = 1100; Height = 720; MinWidth = 760; MinHeight = 480;
        _session.AfterCommitAsync = RefreshAfterCommitAsync;
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
        var notice = new TextBlock { Text = "试用：选择人物并明确世代列后显示单节点；关系和其他人物暂未显示。列表顺序不是谱载排行。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(notice, Dock.Top); center.Children.Add(notice);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        tools.Children.Add(new TextBlock { Text = "确认世代列：", VerticalAlignment = VerticalAlignment.Center }); tools.Children.Add(_column);
        var place = new Button { Content = "将所选人物显示到画布", Padding = new Thickness(6) };
        place.Click += async (_, _) => await PlacePersonAsync(); tools.Children.Add(place);
        var reopen = new Button { Content = "重新打开画布", Padding = new Thickness(6), Margin = new Thickness(5, 0, 0, 0) };
        reopen.Click += async (_, _) => await ReopenCanvasAsync(); tools.Children.Add(reopen);
        DockPanel.SetDock(tools, Dock.Top); center.Children.Add(tools);
        _people.Height = 120; DockPanel.SetDock(_people, Dock.Top); center.Children.Add(_people);
        center.Children.Add(new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
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
        _canvasMonitor.Tick += async (_, _) => await PollCanvasAsync();
        Closing += OnClosing;
        ShowDraft();
    }
    private static void AddField(Panel panel, string label, Control field)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) }); panel.Children.Add(field);
    }
    private void ClearCanvas()
    {
        _canvasPerson = null; _canvasColumn = 0; _canvas.Children.Clear();
    }
    private void RenderCanvas()
    {
        _canvas.Children.Clear();
        if (_canvasPerson is not { } person) return;
        var x = 20 + (_canvasColumn - 1) * 190.0;
        _canvas.Width = Math.Max(650, x + 220);
        var heading = new TextBlock { Text = $"第 {_canvasColumn} 世（本次确认）", FontWeight = FontWeights.SemiBold };
        Canvas.SetLeft(heading, x); Canvas.SetTop(heading, 18); _canvas.Children.Add(heading);
        var card = new Border { Width = 170, Height = 74, BorderBrush = _person == person.Id ? Brushes.SteelBlue : Brushes.SlateGray,
            BorderThickness = new Thickness(_person == person.Id ? 2 : 1), CornerRadius = new CornerRadius(5), Background = Brushes.White, Padding = new Thickness(8),
            Child = new TextBlock { Text = $"{person.Name}\n谱载世代：{person.RecordedGeneration ?? "未记"}\n谱载排行：{person.RecordedBirthOrder ?? "未记"}", TextWrapping = TextWrapping.Wrap } };
        card.MouseLeftButtonDown += async (_, e) => { e.Handled = true; await SelectCanvasPersonAsync(); };
        Canvas.SetLeft(card, x); Canvas.SetTop(card, 60); _canvas.Children.Add(card);
    }
    private async Task ReleaseProjectionAsync()
    {
        _canvasMonitor.Stop(); ClearCanvas(); _projectionRevision = 0;
        var old = _projection; _projection = null;
        if (old is not null) await old.DisposeAsync();
    }
    private async Task ResetProjectionAsync(Guid genealogyId)
    {
        await ReleaseProjectionAsync();
        var next = new WorkbenchProjectionSession(_coordinator, genealogyId);
        try { _projectionRevision = await next.ReopenAsync(); _projection = next; _canvasMonitor.Start(); }
        catch { await next.DisposeAsync(); throw; }
    }
    private async Task PlacePersonAsync()
    {
        if (_busy || _person is not Guid id || _projection is null) return;
        if (!int.TryParse(_column.Text, out var column) || column is < 1 or > 1000)
        { _status.Text = "请输入 1 至 1000 的世代列，作为本次显示的明确选择。"; return; }
        _busy = true; IsEnabled = false;
        try
        {
            var projection = _projection; var revision = _projectionRevision;
            var result = await projection.ReadPersonAsync(revision, id);
            await projection.ValidateAsync(revision);
            if (!ReferenceEquals(projection, _projection) || !projection.IsCurrent(revision) || _person != id)
                throw new InvalidOperationException("人物或读取会话已变化。");
            _canvasPerson = result.Person; _canvasColumn = column; RenderCanvas();
            _status.Text = "已按本次明确选择显示单个人物；关系和其他人物尚未显示。";
        }
        catch (Exception ex) { ClearCanvas(); _status.Text = $"画布读取失败，请重新打开画布：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task ReopenCanvasAsync()
    {
        if (_busy || _genealogy is not Guid genealogy) return;
        _busy = true; IsEnabled = false;
        try { await ResetProjectionAsync(genealogy); _status.Text = "画布已重新打开；请确认世代列并重新显示人物，草稿保留。"; }
        catch (Exception ex) { _status.Text = $"画布无法重新打开：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task SelectCanvasPersonAsync()
    {
        if (_busy || _canvasPerson is not { } person || _projection is null) return;
        var projection = _projection; var revision = _projectionRevision;
        _busy = true; IsEnabled = false;
        try
        {
            await projection.ValidateAsync(revision);
            if (!ReferenceEquals(projection, _projection) || !projection.IsCurrent(revision) || _canvasPerson?.Id != person.Id) return;
            var target = _people.Items.OfType<Option>().FirstOrDefault(item => item.Id == person.Id);
            if (target is null) { ClearCanvas(); _status.Text = "人物列表已变化，请重新选择谱系。"; return; }
            _updating = true; _people.SelectedItem = target; _updating = false;
            _busy = false;
            await ChangePersonAsync(); RenderCanvas();
        }
        catch (Exception ex) { ClearCanvas(); _status.Text = $"画布已失效，草稿保留：{ex.Message}"; }
        finally { IsEnabled = true; _busy = false; }
    }
    private async Task PollCanvasAsync()
    {
        if (_busy || _canvasPolling || _canvasPerson is null || _projection is null) return;
        _canvasPolling = true;
        var projection = _projection; var revision = _projectionRevision;
        try { await projection.ValidateAsync(revision); }
        catch (Exception ex)
        {
            if (ReferenceEquals(projection, _projection) && revision == _projectionRevision)
            { ClearCanvas(); _canvasMonitor.Stop(); _status.Text = $"数据已变化，画布已清空；草稿保留。请重新打开画布：{ex.Message}"; }
        }
        finally { _canvasPolling = false; }
    }
    private async Task RefreshAfterCommitAsync(Guid genealogyId)
    {
        var oldPerson = _canvasPerson; var oldColumn = _canvasColumn;
        try
        {
            if (_afterCommit is not null) await _afterCommit(genealogyId);
            if (_projection is null || _genealogy != genealogyId) return;
            var projection = _projection; var revision = await projection.ReopenAsync();
            _projectionRevision = revision;
            if (oldPerson is not null)
            {
                var result = await projection.ReadPersonAsync(revision, oldPerson.Id);
                await projection.ValidateAsync(revision);
                if (!ReferenceEquals(projection, _projection) || !projection.IsCurrent(revision) ||
                    result.Person.RecordedGeneration != oldPerson.RecordedGeneration || result.Person.RecordedBirthOrder != oldPerson.RecordedBirthOrder)
                    throw new InvalidOperationException("谱载世代或排行已变化，须重新确认画布。");
                _canvasPerson = result.Person; _canvasColumn = oldColumn; RenderCanvas();
            }
            _canvasMonitor.Start();
        }
        catch { ClearCanvas(); _canvasMonitor.Stop(); throw; }
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
            await ResetProjectionAsync(next.Id);
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
            _person = next.Id; ShowDraft(); RenderCanvas(); _status.Text = "修改后请显式保存。";
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
            await ReleaseProjectionAsync();
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
