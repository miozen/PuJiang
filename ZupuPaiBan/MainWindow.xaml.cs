using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.ViewModels;
using ZupuPaiBan.Views;
using ZupuPaiBan.Services;

namespace ZupuPaiBan;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TargetContextCoordinator _targetContexts;
    private readonly string _backupDirectory;
    private readonly TimeSpan _exitTimeout;
    private int _quiescenceStarted;
    private bool _exitStarted;
    private bool _exitReady;
    private GenealogySelectionViewModel? _genealogyViewModel;

    public MainWindow() : this(CreateApplicationCoordinator(), AppDataPaths.BackupsDirectory, true)
    {
    }

    private static TargetContextCoordinator CreateApplicationCoordinator()
    {
        var targetOptions = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = AppDataPaths.DatabasePath,
                Pooling = false
            }.ToString()).Options;
        return new TargetContextCoordinator(targetOptions);
    }

    internal MainWindow(TargetContextCoordinator coordinator, string backupDirectory, bool coordinateExit = false, TimeSpan? exitTimeout = null)
    {
        InitializeComponent();

        _targetContexts = coordinator;
        _backupDirectory = backupDirectory;
        _exitTimeout = exitTimeout ?? TimeSpan.FromSeconds(30);
        var dataService = new DataService(_targetContexts);
        _viewModel = new MainViewModel(dataService);
        DataContext = _viewModel;
        _genealogyViewModel = new GenealogySelectionViewModel(_targetContexts.CreateContext, _targetContexts);
        genealogyPanel.DataContext = _genealogyViewModel;
        _genealogyViewModel.PropertyChanged += GenealogyViewModel_PropertyChanged;
        genealogyPanel.IsEnabled = false;

        _viewModel.OnEditMemberRequested += OnEditMemberRequested;
        _viewModel.OnOpenPreviewRequested += OnOpenPreviewRequested;
        _viewModel.OnExportCollaborationRequested += OnExportCollaborationRequested;
        _viewModel.OnImportCollaborationRequested += OnImportCollaborationRequested;
        if (coordinateExit) Closing += MainWindow_Closing;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        IsEnabled = false;
        try
        {
            await InitializeDatabaseAsync();
            if (Volatile.Read(ref _quiescenceStarted) == 0)
            {
                genealogyPanel.IsEnabled = true;
                IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            FileAppLogger.Write(AppLogLevel.Error, "Database initialization failed.", ex);
            MessageBox.Show($"数据库初始化失败，应用将关闭。请保留数据及备份并检查日志。\n\n{ex.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    internal async Task InitializeDatabaseAsync()
    {
        using var operation = _targetContexts.EnterOperation();
        await using var context = _targetContexts.CreateContext();
        await new DatabaseMigrationService(context, _backupDirectory).MigrateAsync();
        await _viewModel.LoadDataAsync();
        await _genealogyViewModel!.LoadAsync();
    }

    // Preparation only: no database replacement or restore is performed here.
    internal async Task<bool> QuiesceForMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _quiescenceStarted, 1) != 0)
            throw new InvalidOperationException("数据库停用已经开始，不能重复执行。");
        IsEnabled = false;
        _viewModel.StatusText = "正在等待数据库操作结束……";
        try
        {
            await _targetContexts.DrainAsync(cancellationToken);
            _viewModel.StatusText = "数据库操作已停止；恢复功能尚未开放。";
            return true;
        }
        catch (Exception ex)
        {
            FileAppLogger.Write(AppLogLevel.Error, "Database quiescence failed; restart required.", ex);
            _viewModel.StatusText = "数据库停用未完成，请关闭并重新启动应用；不会执行恢复。";
            return false;
        }
    }

    internal async Task<bool> PrepareForExitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await QuiesceForMaintenanceAsync(cancellationToken)) return false;
            foreach (Window child in OwnedWindows.Cast<Window>().ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var closed = false;
                EventHandler onClosed = (_, _) => closed = true;
                child.Closed += onClosed;
                try { child.Close(); }
                finally { child.Closed -= onClosed; }
                if (!closed) throw new InvalidOperationException("子窗口拒绝关闭。");
            }
            cancellationToken.ThrowIfCancellationRequested();
            _viewModel.StatusText = "数据库操作和子窗口已关闭。";
            return true;
        }
        catch (Exception ex)
        {
            FileAppLogger.Write(AppLogLevel.Error, "Window shutdown coordination failed; restart required.", ex);
            _viewModel.StatusText = "窗口关闭未完成，数据目录仍被占用；不会执行恢复。";
            return false;
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_exitReady) return;
        e.Cancel = true;
        if (_exitStarted) return;
        _exitStarted = true;
        using var timeout = new CancellationTokenSource(_exitTimeout);
        if (await PrepareForExitAsync(timeout.Token))
        {
            _exitReady = true;
            Close();
        }
    }

    private void DatabaseBackup_Click(object sender, RoutedEventArgs e)
    {
        new DatabaseBackupDialog(AppDataPaths.DatabasePath, _backupDirectory, _targetContexts) { Owner = this }.ShowDialog();
    }

    private async void InternalJsonImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GenealogyExchangeImportDialog(_targetContexts) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ImportedGenealogyId is not Guid genealogyId) return;

        genealogyPanel.IsEnabled = false;
        try
        {
            await _genealogyViewModel!.LoadAsync();
            var imported = _genealogyViewModel.Genealogies.SingleOrDefault(item => item.Id == genealogyId)
                ?? throw new InvalidOperationException("导入成功，但未能找到新族谱。");
            await _genealogyViewModel.SelectAsync(imported);
            _genealogyViewModel.Status = $"已导入族谱：{imported.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入已完成，但刷新族谱选择失败：{ex.Message}", "内部 JSON 导入", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void ExternalExchangeImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExternalGenealogyExchangeImportDialog(_targetContexts) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ImportedGenealogyId is not Guid genealogyId) return;
        genealogyPanel.IsEnabled = false;
        try
        {
            await _genealogyViewModel!.LoadAsync();
            var imported = _genealogyViewModel.Genealogies.SingleOrDefault(item => item.Id == genealogyId)
                ?? throw new InvalidOperationException("导入成功，但未能找到新族谱。");
            await _genealogyViewModel.SelectAsync(imported);
            _genealogyViewModel.Status = $"已导入外部族谱：{imported.Name}";
        }
        catch (Exception ex) { MessageBox.Show($"导入已完成，但刷新族谱选择失败：{ex.Message}", "外部格式导入", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void CreateGenealogy_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.CreateAsync(); }
        catch (Exception ex) { MessageBox.Show($"创建族谱失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void GenealogySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_genealogyViewModel == null) return;
        try { await _genealogyViewModel.SelectAsync(genealogySelector.SelectedItem as Genealogy); }
        catch (Exception ex) { _genealogyViewModel.Status = $"加载人物失败：{ex.Message}"; }
    }

    private async void RenameGenealogy_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.RenameGenealogyAsync(); }
        catch (Exception ex) { MessageBox.Show($"重命名族谱失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void ViewImportHistory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var history = await _genealogyViewModel!.GetGenealogyImportHistoryAsync();
            new GenealogyImportHistoryDialog(history) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show($"读取导入来源失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void EditGenealogyProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var candidate = await _genealogyViewModel!.GetGenealogyProfileCandidateAsync();
            var dialog = new GenealogyProfileDialog(candidate.Snapshot) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.SaveGenealogyProfileAsync(candidate, dialog.Surname, dialog.Description); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"保存族谱资料失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void AddPerson_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.AddPersonAsync(); }
        catch (Exception ex) { MessageBox.Show($"添加人物失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void CreateBranch_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.CreateRootBranchAsync(); }
        catch (Exception ex) { MessageBox.Show($"创建房支失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void CreateChildBranch_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.CreateChildBranchAsync(); }
        catch (Exception ex) { MessageBox.Show($"创建子房支失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void RenameBranch_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.RenameBranchAsync(); }
        catch (Exception ex) { MessageBox.Show($"重命名房支失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void DeleteBranch_Click(object sender, RoutedEventArgs e)
    {
        BranchDeleteCandidate candidate;
        try { candidate = _genealogyViewModel!.GetBranchDeleteCandidate(); }
        catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show($"确定删除空房支“{candidate.Choice.Label}”吗？有子房支或人物的房支不能删除。",
                "确认删除房支", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel.DeleteEmptyBranchAsync(candidate); }
        catch (Exception ex) { MessageBox.Show($"删除房支失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void ReassignDeleteBranch_Click(object sender, RoutedEventArgs e)
    {
        BranchReassignmentCandidate candidate;
        try { candidate = _genealogyViewModel!.GetBranchReassignmentCandidate(); }
        catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dialog = new BranchReassignmentDialog(candidate, _genealogyViewModel.ParentBranchChoices.ToList()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel.DeleteBranchAndReassignAsync(candidate, dialog.ChildDestination, dialog.PersonDestination); }
        catch (Exception ex) { MessageBox.Show($"删除房支失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void MoveBranch_Click(object sender, RoutedEventArgs e)
    {
        BranchMoveCandidate candidate;
        try { candidate = _genealogyViewModel!.GetBranchMoveCandidate(); }
        catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show($"确定将“{candidate.SourceChoice.Label}”移动到“{candidate.TargetChoice?.Label ?? "顶层"}”吗？子房支和人物归属将保留。", "确认移动房支", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel.MoveBranchAsync(candidate); }
        catch (Exception ex) { MessageBox.Show($"移动房支失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void RenamePerson_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.RenamePersonAsync(); }
        catch (Exception ex) { MessageBox.Show($"保存姓名失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void AddChild_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.AddBiologicalChildAsync(); }
        catch (Exception ex) { MessageBox.Show($"添加子女失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void AddExistingChild_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.AddExistingBiologicalChildAsync(); }
        catch (Exception ex) { MessageBox.Show($"关联子女失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void RemoveBiologicalParentRelationship_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            genealogyPanel.IsEnabled = false;
            BiologicalUnlinkCandidate candidate;
            try { candidate = await _genealogyViewModel!.GetBiologicalUnlinkCandidateAsync(); }
            finally { genealogyPanel.IsEnabled = true; }
            var dialog = new TargetLinkRemovalDialog([candidate.Link], selectSingle: true) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.RemoveBiologicalParentRelationshipAsync(candidate); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"解除关系失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ClearSelectedPerson_Click(object sender, RoutedEventArgs e) =>
        _genealogyViewModel!.ClearSelectedPerson();

    private async void DeleteTargetPerson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var preview = await _genealogyViewModel!.GetPersonDeletePreviewAsync();
            var dialog = new PersonDeletePreviewDialog(preview) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.DeletePersonFromPreviewAsync(preview); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"删除人物失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void SavePersonProfile_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.SavePersonProfileAsync(); }
        catch (Exception ex) { MessageBox.Show($"保存档案失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private void LocatePerson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _genealogyViewModel?.SelectedSearchResult
                ?? throw new InvalidOperationException("请先选择搜索结果。");
            var node = _genealogyViewModel!.GetSearchResultNode(result);
            LocateTreeNode(node);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "人物定位", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void LocateRelationshipTarget_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var detail = targetTree.SelectedItem as TargetMemberTreeDetail
                ?? throw new InvalidOperationException("请先选择一条关系或婚配记录。");
            LocateTreeNode(_genealogyViewModel!.GetRelationshipTargetNode(detail));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "定位关系对方", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void LocateTreeNode(TargetMemberTreeNode node)
    {
        targetTree.UpdateLayout();
        if (targetTree.ItemContainerGenerator.ContainerFromItem(node) is not TreeViewItem item)
            throw new InvalidOperationException("无法定位人物，请重新加载族谱。");
        item.IsSelected = true;
        item.BringIntoView();
        item.Focus();
    }

    private async void TargetPerson_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_genealogyViewModel == null) return;
        _genealogyViewModel.SelectedPerson = (sender as ListBox)?.SelectedItem as Person;
        await _genealogyViewModel.LoadSelectedBiologicalChildrenAsync();
    }

    private async void TargetTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TargetMemberTreeNode node && _genealogyViewModel != null)
            await _genealogyViewModel.SelectTreePersonAsync(node);
    }

    private async void TargetTree_AddChild_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: TargetMemberTreeNode node } || _genealogyViewModel is null) return;
        await AddTreeChildAsync(node);
    }

    private async void TargetShortcut_AddChild_Click(object sender, RoutedEventArgs e)
    {
        try { await AddTreeChildAsync(_genealogyViewModel!.GetSelectedTreePersonNode()); }
        catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async void LineageTree_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            genealogyPanel.IsEnabled = false;
            LineagePreviewContext preview;
            try { preview = await _genealogyViewModel!.GetLineagePreviewAsync(); }
            finally { genealogyPanel.IsEnabled = true; }
            new LineageTreeDialog(_genealogyViewModel!, preview) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show($"无法打开树形世系：{ex.Message}", "树形世系浏览", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async void LineagePreview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            genealogyPanel.IsEnabled = false;
            LineagePreviewContext preview;
            try { preview = await _genealogyViewModel!.GetLineagePreviewAsync(); }
            finally { genealogyPanel.IsEnabled = true; }
            new LineagePreviewDialog(_genealogyViewModel!, preview) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show($"无法预览全谱主线：{ex.Message}", "全谱主线预览", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async void AncestryPreview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            genealogyPanel.IsEnabled = false;
            AncestryPreviewContext preview;
            try { preview = await _genealogyViewModel!.GetAncestryPreviewAsync(); }
            finally { genealogyPanel.IsEnabled = true; }
            new AncestryPreviewDialog(_genealogyViewModel!, preview) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show($"无法预览上系：{ex.Message}", "上系路径预览", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async void TargetShortcut_AddRelationship_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var person = _genealogyViewModel!.GetSelectedPersonForEditing();
            var selection = _genealogyViewModel.CapturePersonSelection();
            var dialog = new TargetRelationshipDialog(person, _genealogyViewModel.GetCurrentPeopleForEditing()) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            _genealogyViewModel.ValidatePersonSelection(selection);
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.AddTargetRelationshipAsync(dialog.Kind, dialog.RelatedPersonId, dialog.Note, dialog.SourceCitation); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"新增人物关系失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void TargetShortcut_AddFamilyUnion_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var person = _genealogyViewModel!.GetSelectedPersonForEditing();
            var selection = _genealogyViewModel.CapturePersonSelection();
            var dialog = new TargetFamilyUnionDialog(person, _genealogyViewModel.GetCurrentPeopleForEditing()) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            _genealogyViewModel.ValidatePersonSelection(selection);
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.AddTargetFamilyUnionAsync(dialog.Kind, dialog.PartnerTwoId, dialog.StartDateText, dialog.EndDateText, dialog.Note, dialog.UnknownPartner); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"新增婚配失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void EditRelationshipDetails_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var candidates = (await _genealogyViewModel!.GetTargetLinkRemovalCandidatesAsync())
                .Where(candidate => candidate.Kind == TargetLinkRemovalKind.Relationship).ToList();
            if (candidates.Count == 0) { MessageBox.Show("当前人物没有可编辑的关系。", "提示"); return; }
            var dialog = new TargetRelationshipDetailsDialog(candidates) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedCandidate is not { } candidate) return;
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.UpdateTargetRelationshipDetailsAsync(candidate, dialog.Note, dialog.SourceCitation); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"保存关系资料失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void CorrectParentKind_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var candidates = (await _genealogyViewModel!.GetTargetLinkRemovalCandidatesAsync())
                .Where(c => c.RelationshipSnapshot?.Kind is PersonRelationshipKind.BiologicalParent or PersonRelationshipKind.AdoptiveParent or PersonRelationshipKind.HeirParent).ToList();
            if (candidates.Count == 0) { MessageBox.Show("当前人物没有可更正类型的父母关系。", "提示"); return; }
            var dialog = new ParentRelationshipCorrectionDialog(candidates) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedCandidate is not { } candidate || dialog.NewKind is not { } kind) return;
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.CorrectParentRelationshipKindAsync(candidate, kind); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"更正关系类型失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void EditFamilyUnionDetails_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var candidates = (await _genealogyViewModel!.GetTargetLinkRemovalCandidatesAsync())
                .Where(candidate => candidate.Kind == TargetLinkRemovalKind.FamilyUnion).ToList();
            if (candidates.Count == 0) { MessageBox.Show("当前人物没有可编辑的婚配。", "提示"); return; }
            var dialog = new TargetFamilyUnionDetailsDialog(candidates) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedCandidate is not { } candidate) return;
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.UpdateTargetFamilyUnionDetailsAsync(candidate, dialog.StartDateText, dialog.EndDateText, dialog.Note); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"保存婚配资料失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void CorrectUnionKind_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var candidates = (await _genealogyViewModel!.GetTargetLinkRemovalCandidatesAsync())
                .Where(c => c.Kind == TargetLinkRemovalKind.FamilyUnion).ToList();
            if (candidates.Count == 0) { MessageBox.Show("当前人物没有可更正的婚配。", "提示"); return; }
            var dialog = new UnionKindCorrectionDialog(candidates) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedCandidate is not { } candidate || dialog.NewKind is not { } kind) return;
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.CorrectFamilyUnionKindAsync(candidate, kind); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"更正婚配类型失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void CompleteUnionPartner_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var candidates = (await _genealogyViewModel!.GetTargetLinkRemovalCandidatesAsync())
                .Where(c => c.Kind == TargetLinkRemovalKind.FamilyUnion && c.UnionSnapshot is { PartnerTwoId: null }).ToList();
            if (candidates.Count == 0) { MessageBox.Show("当前人物没有另一方未详的婚配。", "提示"); return; }
            var dialog = new CompleteUnionPartnerDialog(candidates, _genealogyViewModel.GetCurrentPeopleForEditing()) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedCandidate is not { } candidate || dialog.SelectedPartner is not { } partner) return;
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.CompleteUnknownUnionPartnerAsync(candidate, partner); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"补全婚配对方失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void TargetShortcut_RemoveLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            genealogyPanel.IsEnabled = false;
            IReadOnlyList<TargetLinkRemovalCandidate> candidates;
            try { candidates = await _genealogyViewModel!.GetTargetLinkRemovalCandidatesAsync(); }
            finally { genealogyPanel.IsEnabled = true; }
            if (candidates.Count == 0)
            {
                MessageBox.Show("当前人物没有可解除的关系或婚配记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new TargetLinkRemovalDialog(candidates) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedCandidate is not { } candidate) return;
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.RemoveTargetLinkAsync(candidate); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"解除关系或婚配失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task AddTreeChildAsync(TargetMemberTreeNode node)
    {
        var dialog = new TargetChildDialog(node.Label) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.AddBiologicalChildForTreeNodeAsync(node, dialog.ChildName); }
        catch (Exception ex) { MessageBox.Show($"添加子女失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void TargetTreePerson_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not TextBlock { DataContext: TargetMemberTreeNode node } || _genealogyViewModel is null) return;
        e.Handled = true;
        await EditTreePersonAsync(node);
    }

    private async void TargetShortcut_Edit_Click(object sender, RoutedEventArgs e)
    {
        try { await EditTreePersonAsync(_genealogyViewModel!.GetSelectedTreePersonNode()); }
        catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async Task EditTreePersonAsync(TargetMemberTreeNode node)
    {
        try
        {
            var person = _genealogyViewModel!.GetTreePerson(node);
            var dialog = new TargetPersonEditDialog(person, _genealogyViewModel.Branches.ToList()) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            genealogyPanel.IsEnabled = false;
            try
            {
                await _genealogyViewModel.SaveTreePersonProfileWithRecordedValuesAsync(node, dialog.PersonName, dialog.Sex,
                    dialog.BirthDate, dialog.DeathDate, dialog.Biography, dialog.RecordedGeneration, dialog.RecordedBirthOrder,
                    updateBranch: true, branchId: dialog.BranchId);
            }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"保存档案失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void GenealogyViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GenealogySelectionViewModel.SelectedPerson)) return;
        if (targetTree.SelectedItem is TargetMemberTreeNode node &&
            node.PersonId == _genealogyViewModel?.SelectedPerson?.Id) return;

        ClearSelectedTreeItem(targetTree);
    }

    private static bool ClearSelectedTreeItem(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TreeViewItem { IsSelected: true } item)
            {
                item.IsSelected = false;
                return true;
            }
            if (ClearSelectedTreeItem(child)) return true;
        }
        return false;
    }

    private void TreeMembers_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (treeMembers.SelectedItem is MemberTreeNode node)
        {
            _viewModel.SelectedNode = node;
        }
    }

    private async void TreeMembers_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedNode != null)
        {
            await ShowEditDialog(_viewModel.SelectedNode.Member);
        }
    }

    private async void OnEditMemberRequested(object? sender, FamilyMember member)
    {
        await ShowEditDialog(member);
    }

    private async Task ShowEditDialog(FamilyMember member)
    {
        var editVm = new MemberEditViewModel(member)
        {
            ModalOperationFactory = _targetContexts.EnterOperation
        };
        var dialog = new MemberEditDialog(editVm)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            editVm.ApplyToMember(member);
            using var operation = _targetContexts.EnterOperation();
            var dataService = new DataService(_targetContexts);
            await dataService.UpdateMemberAsync(member);
            await _viewModel.LoadDataAsync();
            _viewModel.StatusText = $"已更新成员: {member.Name}";
        }
    }

    private async void OnOpenPreviewRequested(object? sender, EventArgs e)
    {
        var dataService = new DataService(_targetContexts);
        var printService = new Services.PrintService();
        var previewWindow = new PrintPreviewWindow(dataService, printService);
        previewWindow.Owner = this;
        previewWindow.Show();
    }

    private void BtnAbout_Click(object sender, RoutedEventArgs e)
    {
        var aboutDialog = new AboutDialog
        {
            Owner = this
        };
        aboutDialog.ShowDialog();
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnExportCollaborationRequested(object? sender, EventArgs e)
    {
        var allMembers = await _viewModel.GetAllMembersAsync();
        if (allMembers.Count == 0)
        {
            MessageBox.Show("没有成员数据", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new CollaborationExportDialog(allMembers, new DataService(_targetContexts)) { Owner = this };
        dialog.ShowDialog();
    }

    private async void OnImportCollaborationRequested(object? sender, EventArgs e)
    {
        var dataService = new DataService(_targetContexts);
        var allMembers = await dataService.GetAllMembersAsync();
        if (allMembers.Count == 0)
        {
            MessageBox.Show("请先添加至少一位成员作为导入目标", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new CollaborationImportDialog(allMembers, dataService) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.LoadDataAsync();
            _viewModel.StatusText = "协作导入完成";
        }
    }
}
