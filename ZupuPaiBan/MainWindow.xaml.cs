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

    private async void AddPerson_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.AddPersonAsync(); }
        catch (Exception ex) { MessageBox.Show($"添加人物失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
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
        var parent = _genealogyViewModel?.SelectedPerson;
        var child = _genealogyViewModel?.SelectedBiologicalChild;
        if (parent == null || child == null)
        {
            MessageBox.Show("请先选择父母人物和其生物子女。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"确定解除“{parent.Name}”与“{child.Name}”的生物父母关系吗？\n人物档案会保留，但该关系及其记录的备注和来源将被删除，无法撤销。",
                "确认解除关系", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.RemoveBiologicalParentRelationshipAsync(); }
        catch (Exception ex) { MessageBox.Show($"解除关系失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private void ClearSelectedPerson_Click(object sender, RoutedEventArgs e) =>
        _genealogyViewModel!.ClearSelectedPerson();

    private async void DeleteTargetPerson_Click(object sender, RoutedEventArgs e)
    {
        var person = _genealogyViewModel?.SelectedPerson;
        if (person == null)
        {
            MessageBox.Show("请先选择人物。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"确定删除人物“{person.Name}”吗？此操作无法撤销；有关联关系的人物不能删除。",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.DeleteUnrelatedPersonAsync(); }
        catch (Exception ex) { MessageBox.Show($"删除人物失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
    }

    private async void SavePersonProfile_Click(object sender, RoutedEventArgs e)
    {
        genealogyPanel.IsEnabled = false;
        try { await _genealogyViewModel!.SavePersonProfileAsync(); }
        catch (Exception ex) { MessageBox.Show($"保存档案失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { genealogyPanel.IsEnabled = true; }
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

    private async void TargetShortcut_AddRelationship_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var person = _genealogyViewModel!.GetSelectedPersonForEditing();
            var dialog = new TargetRelationshipDialog(person, _genealogyViewModel.GetCurrentPeopleForEditing()) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.AddTargetRelationshipAsync(dialog.Kind, dialog.RelatedPersonId); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"新增人物关系失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void TargetShortcut_AddFamilyUnion_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var person = _genealogyViewModel!.GetSelectedPersonForEditing();
            var dialog = new TargetFamilyUnionDialog(person, _genealogyViewModel.GetCurrentPeopleForEditing()) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.PartnerTwoId is not Guid partnerTwoId) return;
            genealogyPanel.IsEnabled = false;
            try { await _genealogyViewModel.AddTargetFamilyUnionAsync(dialog.Kind, partnerTwoId); }
            finally { genealogyPanel.IsEnabled = true; }
        }
        catch (Exception ex) { MessageBox.Show($"新增婚配失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
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
            var dialog = new TargetPersonEditDialog(person) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            genealogyPanel.IsEnabled = false;
            try
            {
                await _genealogyViewModel.SaveTreePersonProfileAsync(node, dialog.PersonName, dialog.Sex,
                    dialog.BirthDate, dialog.DeathDate, dialog.Biography);
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
