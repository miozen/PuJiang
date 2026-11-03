using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.ViewModels;

public sealed record BranchChoice(Guid Id, string Label);
public sealed record GenealogyProfileCandidate(GenealogyGraphInfo Snapshot, int SelectionVersion);
public sealed record GenealogyImportHistory(GenealogyGraphInfo Genealogy, IReadOnlyList<GenealogyImportRecord> Records);
public sealed record BranchDeleteCandidate(Guid GenealogyId, GenealogyBranchData Branch, BranchChoice Choice, int SelectionVersion);
public sealed record BranchMoveCandidate(Guid GenealogyId, GenealogyBranchData Source, GenealogyBranchData? Target, BranchChoice SourceChoice, BranchChoice? TargetChoice, int SelectionVersion);
public sealed record BranchReassignmentCandidate(NonEmptyBranchSnapshot Snapshot, BranchChoice Choice, int SelectionVersion);
public sealed record PersonDeletePreview(Guid GenealogyId, Guid PersonId, string PersonName, int SelectionVersion, int PersonSelectionVersion, long Generation, IReadOnlyList<TargetLinkRemovalCandidate> Links);
public sealed record PersonSelectionToken(Guid GenealogyId, Guid PersonId, int SelectionVersion, int PersonSelectionVersion);
public sealed record BiologicalUnlinkCandidate(TargetLinkRemovalCandidate Link, int ChildSelectionVersion);
public sealed record AncestryPreviewContext(GenealogyGraphSnapshot Snapshot, PersonSelectionToken Selection, ObservableCollection<TargetMemberTreeNode> Tree);
public sealed record LineagePreviewContext(GenealogyGraphSnapshot Snapshot, Guid GenealogyId, int SelectionVersion, ObservableCollection<TargetMemberTreeNode> Tree);

/// <summary>创建和选择目标模型族谱；选择仅在当前会话有效。</summary>
public partial class GenealogySelectionViewModel : ObservableObject
{
    private readonly Func<ZupuDbContext> _createContext;
    private readonly TargetContextCoordinator? _operationCoordinator;
    private int _selectionVersion;
    private int _personSelectionVersion;
    private int _candidatePersonSelectionVersion;
    private int _biologicalChildSelectionVersion;

    [ObservableProperty] private ObservableCollection<Genealogy> _genealogies = new();
    [ObservableProperty] private Genealogy? _selectedGenealogy;
    [ObservableProperty] private string _renamedGenealogyName = string.Empty;
    partial void OnSelectedGenealogyChanged(Genealogy? value) => RenamedGenealogyName = value?.Name ?? string.Empty;
    [ObservableProperty] private ObservableCollection<Person> _people = new();
    [ObservableProperty] private ObservableCollection<GenealogyBranchData> _branches = new();
    [ObservableProperty] private string _newBranchName = string.Empty;
    [ObservableProperty] private ObservableCollection<BranchChoice> _parentBranchChoices = new();
    [ObservableProperty] private BranchChoice? _selectedParentBranch;
    [ObservableProperty] private string _newChildBranchName = string.Empty;
    [ObservableProperty] private BranchChoice? _branchToRename;
    [ObservableProperty] private BranchChoice? _moveTargetBranch;
    [ObservableProperty] private bool _moveToRoot;
    [ObservableProperty] private string _renamedBranchName = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ObservableCollection<Person> _searchResults = new();
    [ObservableProperty] private Person? _selectedSearchResult;

    partial void OnSearchTextChanged(string value) => UpdateSearchResults();
    partial void OnPeopleChanged(ObservableCollection<Person> value) => UpdateSearchResults();

    private void UpdateSearchResults()
    {
        SelectedSearchResult = null;
        var query = SearchText.Trim();
        SearchResults = new ObservableCollection<Person>(query.Length == 0 || SelectedGenealogy is null
            ? Array.Empty<Person>()
            : People.Where(p => p.GenealogyId == SelectedGenealogy.Id && p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    public TargetMemberTreeNode GetSearchResultNode(Person result)
    {
        if (SelectedGenealogy is null || !SearchResults.Any(p => ReferenceEquals(p, result)) ||
            result.GenealogyId != SelectedGenealogy.Id)
            throw new InvalidOperationException("搜索结果已失效，请重新搜索。");
        return TargetTree.SingleOrDefault(node => node.PersonId == result.Id)
            ?? throw new InvalidOperationException("人物已变化，请重新加载族谱。");
    }

    public TargetMemberTreeNode GetRelationshipTargetNode(TargetMemberTreeDetail detail)
    {
        if (SelectedGenealogy is null || !TargetTree.Any(node => node.Entries.Any(entry => ReferenceEquals(entry, detail))))
            throw new InvalidOperationException("关系选择已失效，请重新选择。");
        if (detail.OtherPersonId is not Guid id)
            throw new InvalidOperationException("此记录没有可定位的对方人物。");
        if (!People.Any(person => person.Id == id && person.GenealogyId == SelectedGenealogy.Id))
            throw new InvalidOperationException("对方人物已失效，请重新加载族谱。");
        return TargetTree.SingleOrDefault(node => node.PersonId == id)
            ?? throw new InvalidOperationException("对方人物已失效，请重新加载族谱。");
    }
    [ObservableProperty] private ObservableCollection<TargetMemberTreeNode> _targetTree = new();
    [ObservableProperty] private ObservableCollection<Person> _biologicalChildren = new();
    [ObservableProperty] private Person? _selectedBiologicalChild;
    partial void OnSelectedBiologicalChildChanged(Person? value) => ++_biologicalChildSelectionVersion;
    [ObservableProperty] private Person? _selectedExistingChild;
    [ObservableProperty] private string _childrenStatus = "请选择人物查看生物子女";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedParentStatus))]
    private Person? _selectedPerson;
    [ObservableProperty] private string _editedPersonName = string.Empty;
    [ObservableProperty] private PersonSex _editedSex;
    [ObservableProperty] private string? _editedBirthDate;
    [ObservableProperty] private string? _editedDeathDate;
    [ObservableProperty] private string? _editedBiography;
    [ObservableProperty] private string _newName = string.Empty;
    [ObservableProperty] private string _newPersonName = string.Empty;
    [ObservableProperty] private string _newChildName = string.Empty;
    [ObservableProperty] private string _status = "请选择族谱";

    public string SelectedParentStatus => SelectedPerson is null
        ? "添加子女的父母：未选择"
        : $"添加子女的父母：{SelectedPerson.Name}（ID: {SelectedPerson.Id}）";

    public void ClearSelectedPerson() => SelectedPerson = null;

    public async Task SelectTreePersonAsync(TargetMemberTreeNode node)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        // 忽略切谱或刷新后仍在 UI 事件队列中的旧节点。
        if (SelectedGenealogy is null || !TargetTree.Any(item => ReferenceEquals(item, node))) return;
        var person = People.SingleOrDefault(p => p.Id == node.PersonId && p.GenealogyId == SelectedGenealogy.Id);
        if (person is null) return;
        SelectedPerson = person;
        await LoadSelectedBiologicalChildrenAsync();
    }

    public Person GetTreePerson(TargetMemberTreeNode node)
    {
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        if (!TargetTree.Any(item => ReferenceEquals(item, node)))
            throw new InvalidOperationException("人物已变化，请重新选择当前族谱的人物。");
        return People.SingleOrDefault(p => p.Id == node.PersonId && p.GenealogyId == genealogy.Id)
            ?? throw new InvalidOperationException("当前族谱中不存在该人物。");
    }

    public TargetMemberTreeNode GetSelectedTreePersonNode()
    {
        var (_, person) = GetCurrentSelectedPersonForEditing();
        return TargetTree.SingleOrDefault(node => node.PersonId == person.Id)
            ?? throw new InvalidOperationException("所选人物已变化，请重新选择。");
    }

    /// <summary>Returns the selected person only after proving it still belongs to the current target genealogy.</summary>
    public Person GetSelectedPersonForEditing() => GetCurrentSelectedPersonForEditing().Person;

    public PersonSelectionToken CapturePersonSelection()
    {
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        return new PersonSelectionToken(genealogy.Id, person.Id, _selectionVersion, _candidatePersonSelectionVersion);
    }

    public void ValidatePersonSelection(PersonSelectionToken token)
    {
        if (token.GenealogyId != SelectedGenealogy?.Id || token.PersonId != SelectedPerson?.Id ||
            token.SelectionVersion != _selectionVersion || token.PersonSelectionVersion != _candidatePersonSelectionVersion)
            throw new InvalidOperationException("当前选择已变化，请重新选择。");
        _ = GetCurrentSelectedPersonForEditing();
    }

    public async Task<AncestryPreviewContext> GetAncestryPreviewAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var selection = CapturePersonSelection();
        var tree = TargetTree;
        var changed = false;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changedHandler = (_, _) => changed = true;
        tree.CollectionChanged += changedHandler;
        try
        {
            var snapshot = await GetGraphSnapshotForSelectionAsync(selection.GenealogyId);
            ValidatePersonSelection(selection);
            if (changed || !ReferenceEquals(tree, TargetTree)) throw new InvalidOperationException("族谱已刷新，请重新打开上系预览。");
            return new AncestryPreviewContext(snapshot, selection, tree);
        }
        finally { tree.CollectionChanged -= changedHandler; }
    }

    public bool IsAncestryPreviewCurrent(AncestryPreviewContext preview)
    {
        try { ValidatePersonSelection(preview.Selection); }
        catch (InvalidOperationException) { return false; }
        return ReferenceEquals(preview.Tree, TargetTree);
    }

    public async Task<LineagePreviewContext> GetLineagePreviewAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var version = _selectionVersion;
        var tree = TargetTree;
        var changed = false;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler handler = (_, _) => changed = true;
        tree.CollectionChanged += handler;
        try
        {
            var snapshot = await GetGraphSnapshotForSelectionAsync(genealogy.Id);
            if (changed || version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || !ReferenceEquals(tree, TargetTree))
                throw new InvalidOperationException("族谱已切换或刷新，请重新打开主线预览。");
            return new LineagePreviewContext(snapshot, genealogy.Id, version, tree);
        }
        finally { tree.CollectionChanged -= handler; }
    }

    public bool IsLineagePreviewCurrent(LineagePreviewContext preview) =>
        preview.GenealogyId == SelectedGenealogy?.Id && preview.SelectionVersion == _selectionVersion && ReferenceEquals(preview.Tree, TargetTree);

    /// <summary>Supplies edit dialogs with a snapshot bounded to the current target genealogy.</summary>
    public IReadOnlyList<Person> GetCurrentPeopleForEditing()
    {
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        return People.Where(person => person.GenealogyId == genealogy.Id)
            .OrderBy(person => person.Name, StringComparer.Ordinal).ThenBy(person => person.Id).ToList();
    }

    public async Task SaveTreePersonProfileAsync(TargetMemberTreeNode node, string? name, PersonSex sex,
        string? birthDate, string? deathDate, string? biography)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var person = GetTreePerson(node);
        var genealogy = SelectedGenealogy!;
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).UpdatePersonBasicProfileAsync(genealogy.Id, person.Id,
            name, sex, birthDate, deathDate, biography);
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id) return;
        try { await SelectAsync(genealogy); }
        catch (Exception ex)
        {
            if (SelectedGenealogy?.Id == genealogy.Id) Status = $"档案已保存，但目标谱系刷新失败：{ex.Message}";
        }
    }

    public async Task SaveTreePersonProfileWithRecordedValuesAsync(TargetMemberTreeNode node, string? name, PersonSex sex,
        string? birthDate, string? deathDate, string? biography, string? recordedGeneration, string? recordedBirthOrder,
        bool updateBranch = false, Guid? branchId = null)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var person = GetTreePerson(node);
        var genealogy = SelectedGenealogy!;
        if (updateBranch && branchId is Guid id && !Branches.Any(b => b.Id == id))
            throw new InvalidOperationException("所选房支已失效，请重新选择。");
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).UpdatePersonBasicProfileWithRecordedValuesAsync(genealogy.Id, person.Id,
            name, sex, birthDate, deathDate, biography, recordedGeneration, recordedBirthOrder,
            updateBranch: updateBranch, branchId: branchId);
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id) return;
        try { await SelectAsync(genealogy); }
        catch (Exception ex)
        {
            if (SelectedGenealogy?.Id == genealogy.Id) Status = $"档案已保存，但目标谱系刷新失败：{ex.Message}";
        }
    }

    public async Task CreateRootBranchAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).CreateRootBranchAsync(genealogy.Id, NewBranchName);
        NewBranchName = string.Empty;
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id) return;
        try { await SelectAsync(genealogy); }
        catch (Exception ex) { if (SelectedGenealogy?.Id == genealogy.Id) Status = $"房支已创建，但刷新失败：{ex.Message}"; }
    }

    public async Task CreateChildBranchAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var parent = SelectedParentBranch;
        if (parent is null || !ParentBranchChoices.Any(choice => ReferenceEquals(choice, parent)))
            throw new InvalidOperationException("请选择当前族谱中的上级房支。");
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).CreateChildBranchAsync(genealogy.Id, parent.Id, NewChildBranchName);
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id) return;
        NewChildBranchName = string.Empty;
        try { await SelectAsync(genealogy); }
        catch (Exception ex) { if (SelectedGenealogy?.Id == genealogy.Id) Status = $"子房支已创建，但刷新失败：{ex.Message}"; }
    }

    public async Task RenameBranchAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var branch = BranchToRename;
        if (branch is null || !ParentBranchChoices.Any(choice => ReferenceEquals(choice, branch)))
            throw new InvalidOperationException("请选择当前族谱中的房支。");
        var snapshot = Branches.SingleOrDefault(item => item.Id == branch.Id)
            ?? throw new InvalidOperationException("房支已失效，请重新选择。");
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).RenameBranchAsync(genealogy.Id, branch.Id, snapshot.Name, snapshot.ParentBranchId, RenamedBranchName);
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id) return;
        RenamedBranchName = string.Empty;
        try { await SelectAsync(genealogy); }
        catch (Exception ex) { if (SelectedGenealogy?.Id == genealogy.Id) Status = $"房支已重命名，但刷新失败：{ex.Message}"; }
    }

    public BranchDeleteCandidate GetBranchDeleteCandidate()
    {
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var choice = BranchToRename;
        if (choice is null || !ParentBranchChoices.Any(item => ReferenceEquals(item, choice)))
            throw new InvalidOperationException("请选择当前族谱中的房支。");
        var branch = Branches.SingleOrDefault(item => item.Id == choice.Id)
            ?? throw new InvalidOperationException("房支已失效，请重新选择。");
        return new BranchDeleteCandidate(genealogy.Id, branch, choice, _selectionVersion);
    }

    public async Task DeleteEmptyBranchAsync(BranchDeleteCandidate candidate)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        if (candidate.SelectionVersion != _selectionVersion || SelectedGenealogy?.Id != candidate.GenealogyId ||
            !ReferenceEquals(BranchToRename, candidate.Choice) || !ParentBranchChoices.Any(item => ReferenceEquals(item, candidate.Choice)))
            throw new InvalidOperationException("选择已变化，请重新确认删除。");
        await using var context = _createContext();
        await new GenealogyDataService(context).DeleteEmptyBranchAsync(candidate.GenealogyId, candidate.Branch.Id,
            candidate.Branch.Name, candidate.Branch.ParentBranchId, candidate.Branch.SortOrder);
        if (candidate.SelectionVersion != _selectionVersion || SelectedGenealogy?.Id != candidate.GenealogyId) return;
        try { await SelectAsync(SelectedGenealogy); }
        catch (Exception ex) { if (SelectedGenealogy?.Id == candidate.GenealogyId) Status = $"房支已删除，但刷新失败：{ex.Message}"; }
    }

    public BranchReassignmentCandidate GetBranchReassignmentCandidate()
    {
        var source = GetBranchDeleteCandidate();
        return new BranchReassignmentCandidate(new NonEmptyBranchSnapshot(source.GenealogyId, source.Branch,
            Branches.ToArray(), People.Where(p => p.BranchId == source.Branch.Id)
                .Select(p => new BranchPersonSnapshot(p.Id, p.Name)).ToArray()), source.Choice, source.SelectionVersion);
    }

    public async Task DeleteBranchAndReassignAsync(BranchReassignmentCandidate candidate, Guid? childDestination, Guid? personDestination)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        if (candidate.SelectionVersion != _selectionVersion || SelectedGenealogy?.Id != candidate.Snapshot.GenealogyId ||
            !ReferenceEquals(BranchToRename, candidate.Choice))
            throw new InvalidOperationException("选择已变化，请重新预览。");
        await using var context = _createContext();
        await new GenealogyDataService(context).DeleteBranchAndReassignAsync(candidate.Snapshot, childDestination, personDestination);
        if (candidate.SelectionVersion != _selectionVersion || SelectedGenealogy?.Id != candidate.Snapshot.GenealogyId) return;
        try { await SelectAsync(SelectedGenealogy); }
        catch (Exception ex) { if (SelectedGenealogy?.Id == candidate.Snapshot.GenealogyId) Status = $"房支已删除，但刷新失败：{ex.Message}"; }
    }

    public BranchMoveCandidate GetBranchMoveCandidate()
    {
        var source = GetBranchDeleteCandidate();
        var choice = MoveToRoot ? null : MoveTargetBranch;
        if (!MoveToRoot && (choice is null || !ParentBranchChoices.Any(item => ReferenceEquals(item, choice))))
            throw new InvalidOperationException("请选择新上级或勾选移至顶层。");
        var target = choice is null ? null : Branches.SingleOrDefault(item => item.Id == choice.Id)
            ?? throw new InvalidOperationException("目标房支已失效。");
        return new BranchMoveCandidate(source.GenealogyId, source.Branch, target, source.Choice, choice, source.SelectionVersion);
    }

    public async Task MoveBranchAsync(BranchMoveCandidate candidate)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        if (candidate.SelectionVersion != _selectionVersion || SelectedGenealogy?.Id != candidate.GenealogyId ||
            !ReferenceEquals(BranchToRename, candidate.SourceChoice) ||
            (candidate.TargetChoice is null ? !MoveToRoot : MoveToRoot || !ReferenceEquals(MoveTargetBranch, candidate.TargetChoice)))
            throw new InvalidOperationException("选择已变化，请重新确认移动。");
        await using var context = _createContext();
        await new GenealogyDataService(context).MoveBranchAsync(candidate.GenealogyId, candidate.Source, candidate.Target);
        if (candidate.SelectionVersion != _selectionVersion || SelectedGenealogy?.Id != candidate.GenealogyId) return;
        try { await SelectAsync(SelectedGenealogy); }
        catch (Exception ex) { if (SelectedGenealogy?.Id == candidate.GenealogyId) Status = $"房支已移动，但刷新失败：{ex.Message}"; }
    }

    private static List<BranchChoice> CreateBranchChoices(IReadOnlyList<GenealogyBranchData> branches)
    {
        var byId = branches.ToDictionary(b => b.Id);
        return branches.Select(branch =>
        {
            var names = new List<string>();
            var seen = new HashSet<Guid>();
            var current = branch;
            while (true)
            {
                if (!seen.Add(current.Id)) throw new InvalidOperationException("房支层级存在循环。");
                names.Add(current.Name);
                if (current.ParentBranchId is not Guid parent) break;
                if (!byId.TryGetValue(parent, out current!)) throw new InvalidOperationException("房支上级不存在。");
            }
            names.Reverse();
            return new BranchChoice(branch.Id, $"{string.Join(" / ", names)}（ID: {branch.Id}）");
        }).OrderBy(choice => choice.Label, StringComparer.Ordinal).ToList();
    }

    public GenealogySelectionViewModel(Func<ZupuDbContext> createContext, TargetContextCoordinator? operationCoordinator = null)
    {
        _createContext = createContext;
        _operationCoordinator = operationCoordinator;
    }

    public async Task LoadAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        await using var context = _createContext();
        var items = await new GenealogyDataService(context).GetGenealogiesAsync();
        Genealogies = new ObservableCollection<Genealogy>(items);
    }

    public async Task CreateAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var name = NewName.Trim();
        if (name.Length == 0 || name.Length > 200)
            throw new InvalidOperationException("族谱名称须为 1 至 200 个字符。");

        await using var context = _createContext();
        var service = new GenealogyDataService(context);
        if ((await service.GetGenealogiesAsync()).Any(g => g.Name == name))
            throw new InvalidOperationException("该族谱名称已存在。");

        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = name });
        await LoadAsync();
        NewName = string.Empty;
        SelectedGenealogy = Genealogies.Single(g => g.Id == genealogy.Id);
        await SelectAsync(SelectedGenealogy);
    }

    public async Task RenameGenealogyAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var selected = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var version = _selectionVersion;
        var originalName = selected.Name;
        await using var context = _createContext();
        var name = await new GenealogyDataService(context).RenameGenealogyAsync(selected.Id, originalName, RenamedGenealogyName);
        foreach (var item in Genealogies.Where(item => item.Id == selected.Id)) item.Name = name;
        if (version != _selectionVersion || SelectedGenealogy?.Id != selected.Id) return;
        SelectedGenealogy.Name = name;
        RenamedGenealogyName = name;
        Status = $"当前族谱：{name}，{People.Count} 位人物";
    }

    public async Task<GenealogyImportHistory> GetGenealogyImportHistoryAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var selected = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var version = _selectionVersion;
        await using var context = _createContext();
        var service = new GenealogyDataService(context);
        var snapshot = await service.GetGenealogyProfileAsync(selected.Id);
        var records = await service.GetImportRecordsAsync(selected.Id);
        if (version != _selectionVersion || SelectedGenealogy?.Id != selected.Id)
            throw new InvalidOperationException("当前族谱已变化，请重新选择。");
        return new GenealogyImportHistory(snapshot, records.AsReadOnly());
    }

    public async Task<GenealogyProfileCandidate> GetGenealogyProfileCandidateAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var selected = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var version = _selectionVersion;
        await using var context = _createContext();
        var snapshot = await new GenealogyDataService(context).GetGenealogyProfileAsync(selected.Id);
        if (version != _selectionVersion || SelectedGenealogy?.Id != selected.Id)
            throw new InvalidOperationException("当前族谱已变化，请重新选择。");
        return new GenealogyProfileCandidate(snapshot, version);
    }

    public async Task SaveGenealogyProfileAsync(GenealogyProfileCandidate candidate, string? surname, string? description)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        using var operation = _operationCoordinator?.EnterOperation();
        if (candidate.SelectionVersion != _selectionVersion || SelectedGenealogy?.Id != candidate.Snapshot.Id)
            throw new InvalidOperationException("当前族谱已变化，请重新选择。");
        await using var context = _createContext();
        await new GenealogyDataService(context).UpdateGenealogyProfileAsync(candidate.Snapshot, surname, description);
        if (candidate.SelectionVersion != _selectionVersion || SelectedGenealogy?.Id != candidate.Snapshot.Id) return;
        foreach (var item in Genealogies.Where(g => g.Id == candidate.Snapshot.Id))
        {
            item.Surname = surname;
            item.Description = description;
        }
        SelectedGenealogy.Surname = surname;
        SelectedGenealogy.Description = description;
        Status = "族谱资料已保存。";
    }

    public async Task SelectAsync(Genealogy? genealogy)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var version = ++_selectionVersion;
        ++_personSelectionVersion;
        SelectedGenealogy = genealogy;
        SelectedPerson = null;
        SelectedBiologicalChild = null;
        SelectedExistingChild = null;
        BiologicalChildren.Clear();
        ChildrenStatus = "请选择人物查看生物子女";
        People.Clear();
        Branches.Clear();
        SelectedParentBranch = null;
        BranchToRename = null;
        ParentBranchChoices.Clear();
        UpdateSearchResults();
        TargetTree.Clear();
        if (genealogy == null)
        {
            Status = "请选择族谱";
            return;
        }

        Status = "正在加载人物…";
        GenealogyGraphSnapshot snapshot;
        try
        {
            snapshot = await GetGraphSnapshotForSelectionAsync(genealogy.Id);
        }
        catch (Exception exception)
        {
            // The target selection is already cleared, so never expose a prior genealogy after a failed snapshot.
            if (version == _selectionVersion)
                Status = $"加载目标谱系失败：{exception.Message}";
            throw;
        }
        var tree = TargetMemberTreeProjection.Create(snapshot);
        var people = CreatePeople(snapshot, genealogy.Id);
        var branchChoices = CreateBranchChoices(snapshot.Branches);
        if (version != _selectionVersion) return;
        People = new ObservableCollection<Person>(people);
        Branches = new ObservableCollection<GenealogyBranchData>(snapshot.Branches);
        ParentBranchChoices = new ObservableCollection<BranchChoice>(branchChoices);
        TargetTree = new ObservableCollection<TargetMemberTreeNode>(tree);
        Status = $"当前族谱：{genealogy.Name}，{people.Count} 位人物";
    }

    private static List<Person> CreatePeople(GenealogyGraphSnapshot snapshot, Guid genealogyId) =>
        snapshot.People.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Id).Select(p => new Person
        {
            Id = p.Id, GenealogyId = genealogyId, Name = p.Name, Sex = p.Sex, BirthDateText = p.BirthDateText,
            DeathDateText = p.DeathDateText, Biography = p.Biography, PhotoPath = p.PhotoPath,
            IsHistoricalRecord = p.IsHistoricalRecord, RecordedGeneration = p.RecordedGeneration,
            DerivedGeneration = p.DerivedGeneration, RecordedBirthOrder = p.RecordedBirthOrder,
            DerivedBirthOrder = p.DerivedBirthOrder, BranchId = p.BranchId
        }).ToList();

    public async Task AddPersonAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var selected = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).AddPersonAsync(new Person
        {
            GenealogyId = selected.Id,
            Name = NewPersonName
        });
        NewPersonName = string.Empty;
        if (version == _selectionVersion && SelectedGenealogy?.Id == selected.Id)
            await SelectAsync(selected);
    }

    partial void OnSelectedPersonChanged(Person? value)
    {
        ++_personSelectionVersion;
        ++_candidatePersonSelectionVersion;
        SelectedBiologicalChild = null;
        SelectedExistingChild = null;
        BiologicalChildren.Clear();
        ChildrenStatus = "请选择人物查看生物子女";
        EditedPersonName = value?.Name ?? string.Empty;
        EditedSex = value?.Sex ?? PersonSex.Unknown;
        EditedBirthDate = value?.BirthDateText;
        EditedDeathDate = value?.DeathDateText;
        EditedBiography = value?.Biography;
    }

    public async Task LoadSelectedBiologicalChildrenAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var version = ++_personSelectionVersion;
        var genealogy = SelectedGenealogy;
        var parent = SelectedPerson;
        SelectedBiologicalChild = null;
        BiologicalChildren.Clear();
        if (genealogy == null || parent == null)
        {
            ChildrenStatus = "请选择人物查看生物子女";
            return;
        }

        ChildrenStatus = "正在加载生物子女…";
        try
        {
            await using var context = _createContext();
            var children = await new GenealogyDataService(context).GetBiologicalChildrenAsync(genealogy.Id, parent.Id);
            if (version != _personSelectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != parent.Id) return;
            BiologicalChildren = new ObservableCollection<Person>(children);
            ChildrenStatus = $"所选人物的生物子女：{children.Count} 人";
        }
        catch (Exception ex)
        {
            if (version == _personSelectionVersion && SelectedGenealogy?.Id == genealogy.Id && SelectedPerson?.Id == parent.Id)
                ChildrenStatus = $"加载生物子女失败：{ex.Message}";
        }
    }

    public async Task RenamePersonAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var person = SelectedPerson ?? throw new InvalidOperationException("请先选择人物。");
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).RenamePersonAsync(genealogy.Id, person.Id, EditedPersonName);
        if (version == _selectionVersion && SelectedGenealogy?.Id == genealogy.Id)
            await SelectAsync(genealogy);
    }

    public async Task SavePersonProfileAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var person = SelectedPerson ?? throw new InvalidOperationException("请先选择人物。");
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).UpdatePersonProfileAsync(genealogy.Id, person.Id,
            EditedSex, EditedBirthDate, EditedDeathDate, EditedBiography);
        if (version == _selectionVersion && SelectedGenealogy?.Id == genealogy.Id)
            await SelectAsync(genealogy);
    }

    public async Task<PersonDeletePreview> GetPersonDeletePreviewAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        var version = _selectionVersion;
        var personVersion = _candidatePersonSelectionVersion;
        var snapshot = await GetGraphSnapshotForSelectionAsync(genealogy.Id);
        var links = await GetTargetLinkRemovalCandidatesAsync();
        var current = snapshot.People.SingleOrDefault(p => p.Id == person.Id) ?? throw new InvalidOperationException("人物已不存在。");
        if (version != _selectionVersion || personVersion != _candidatePersonSelectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id ||
            links.Any(link => link.UnlinkGeneration != snapshot.UnlinkGeneration))
            throw new InvalidOperationException("选择或数据已变化，请重新预览。");
        return new PersonDeletePreview(genealogy.Id, person.Id, current.Name, version, personVersion, snapshot.UnlinkGeneration, links);
    }

    public async Task DeletePersonFromPreviewAsync(PersonDeletePreview preview)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        if (preview.Links.Count != 0 || preview.SelectionVersion != _selectionVersion || preview.PersonSelectionVersion != _candidatePersonSelectionVersion ||
            SelectedGenealogy?.Id != preview.GenealogyId || SelectedPerson?.Id != preview.PersonId)
            throw new InvalidOperationException("选择或关联已变化，请重新预览。");
        await using var context = _createContext();
        await new GenealogyDataService(context).DeleteUnrelatedPersonConfirmedAsync(preview.GenealogyId, preview.PersonId, preview.PersonName, preview.Generation);
        if (preview.SelectionVersion != _selectionVersion || SelectedGenealogy?.Id != preview.GenealogyId) return;
        try { await SelectAsync(SelectedGenealogy); }
        catch (Exception ex) { if (SelectedGenealogy?.Id == preview.GenealogyId) Status = $"人物已删除，但刷新失败：{ex.Message}"; }
    }

    public async Task DeleteUnrelatedPersonAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var person = SelectedPerson ?? throw new InvalidOperationException("请先选择人物。");
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).DeleteUnrelatedPersonAsync(genealogy.Id, person.Id);
        if (version == _selectionVersion && SelectedGenealogy?.Id == genealogy.Id)
            await SelectAsync(genealogy);
    }

    public async Task AddBiologicalChildAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var parent = SelectedPerson ?? throw new InvalidOperationException("请先选择父母人物。");
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).AddBiologicalChildAsync(genealogy.Id, parent.Id, NewChildName);
        NewChildName = string.Empty;
        if (version == _selectionVersion && SelectedGenealogy?.Id == genealogy.Id)
            await SelectAsync(genealogy);
    }

    public async Task AddBiologicalChildForTreeNodeAsync(TargetMemberTreeNode node, string? name)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        if (!TargetTree.Any(item => ReferenceEquals(item, node)) ||
            !People.Any(p => p.Id == node.PersonId && p.GenealogyId == genealogy.Id))
            throw new InvalidOperationException("人物已变化，请重新选择当前族谱的人物。");

        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).AddBiologicalChildAsync(genealogy.Id, node.PersonId, name);
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id) return;
        try { await SelectAsync(genealogy); }
        catch (Exception ex)
        {
            if (SelectedGenealogy?.Id == genealogy.Id)
                Status = $"子女已保存，但目标谱系刷新失败：{ex.Message}";
        }
    }

    public async Task<BiologicalUnlinkCandidate> GetBiologicalUnlinkCandidateAsync()
    {
        var (genealogy, parent) = GetCurrentSelectedPersonForEditing();
        var child = SelectedBiologicalChild ?? throw new InvalidOperationException("请先选择生物子女。");
        if (child.GenealogyId != genealogy.Id || !People.Any(p => p.Id == child.Id && p.GenealogyId == genealogy.Id))
            throw new InvalidOperationException("所选子女不属于当前族谱，请重新选择。");
        var childVersion = _biologicalChildSelectionVersion;
        var candidates = await GetTargetLinkRemovalCandidatesAsync();
        if (childVersion != _biologicalChildSelectionVersion || SelectedBiologicalChild?.Id != child.Id)
            throw new InvalidOperationException("子女选择已变化，请重新选择。");
        var matches = candidates.Where(c => c.RelationshipSnapshot is { Kind: PersonRelationshipKind.BiologicalParent } r &&
            r.PersonId == child.Id && r.RelatedPersonId == parent.Id).ToList();
        if (matches.Count != 1) throw new InvalidOperationException("所选生物父母关系不存在或不唯一，请重新加载。");
        return new BiologicalUnlinkCandidate(matches[0], childVersion);
    }

    public async Task RemoveBiologicalParentRelationshipAsync(BiologicalUnlinkCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.ChildSelectionVersion != _biologicalChildSelectionVersion ||
            candidate.Link.RelationshipSnapshot is not { Kind: PersonRelationshipKind.BiologicalParent } relationship ||
            relationship.PersonId != SelectedBiologicalChild?.Id || relationship.RelatedPersonId != SelectedPerson?.Id)
            throw new InvalidOperationException("子女或父母选择已变化，请重新选择。");
        await RemoveTargetLinkCoreAsync(candidate.Link, "生物父子关系已解除", "已解除生物父子关系。");
    }

    public async Task RemoveBiologicalParentRelationshipAsync()
    {
        var candidate = await GetBiologicalUnlinkCandidateAsync();
        await RemoveBiologicalParentRelationshipAsync(candidate);
    }

    public async Task AddExistingBiologicalChildAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var parent = SelectedPerson ?? throw new InvalidOperationException("请先选择父母人物。");
        var child = SelectedExistingChild ?? throw new InvalidOperationException("请先选择已有子女人物。");
        var version = _personSelectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).AddExistingBiologicalChildAsync(genealogy.Id, parent.Id, child.Id);
        SelectedExistingChild = null;
        if (version == _personSelectionVersion && SelectedGenealogy?.Id == genealogy.Id && SelectedPerson?.Id == parent.Id)
        {
            await LoadSelectedBiologicalChildrenAsync();
            await RefreshTargetTreeAsync(genealogy.Id, _selectionVersion, "生物父子关系已建立");
        }
    }

    /// <summary>
    /// Creates one explicit relationship for the selected target-model person. Both UI candidates and the
    /// persistence validator are genealogy-scoped; this method never infers, merges, or replaces a relation.
    /// </summary>
    public async Task AddTargetRelationshipAsync(PersonRelationshipKind kind, Guid? relatedPersonId,
        string? note = null, string? sourceCitation = null)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        if (kind == PersonRelationshipKind.LostAncestry)
            relatedPersonId = null;
        else if (relatedPersonId is not Guid relatedId || !GetCurrentPeopleForEditing().Any(candidate => candidate.Id == relatedId))
            throw new InvalidOperationException("关系对方必须是当前族谱中的人物。");

        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).AddRelationshipAsync(new PersonRelationship
        {
            GenealogyId = genealogy.Id,
            PersonId = person.Id,
            RelatedPersonId = relatedPersonId,
            Kind = kind,
            Note = note,
            SourceCitation = sourceCitation
        });
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id) return;
        if (kind == PersonRelationshipKind.BiologicalParent)
            await LoadSelectedBiologicalChildrenAsync();
        if (await RefreshTargetTreeAsync(genealogy.Id, version, "人物关系已新增"))
            Status = "已新增人物关系。";
    }

    /// <summary>
    /// Creates one explicit family union; an unknown second partner requires explicit caller intent.
    /// </summary>
    public async Task AddTargetFamilyUnionAsync(FamilyUnionKind kind, Guid? partnerTwoId,
        string? startDateText = null, string? endDateText = null, string? note = null, bool unknownPartner = false)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        if (unknownPartner != (partnerTwoId is null))
            throw new InvalidOperationException("请明确选择婚配对方或选择另一方未详。");
        if (partnerTwoId is Guid partnerId && !GetCurrentPeopleForEditing().Any(candidate => candidate.Id == partnerId))
            throw new InvalidOperationException("婚配对方必须是当前族谱中的人物。");

        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).AddFamilyUnionAsync(new FamilyUnion
        {
            GenealogyId = genealogy.Id,
            PartnerOneId = person.Id,
            PartnerTwoId = partnerTwoId,
            Kind = kind,
            StartDateText = startDateText,
            EndDateText = endDateText,
            Note = note
        });
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id) return;
        if (await RefreshTargetTreeAsync(genealogy.Id, version, "婚配关系已新增"))
            Status = "已新增婚配关系。";
    }

    /// <summary>Reads only the current selected person's removable links from the current genealogy snapshot.</summary>
    public async Task<IReadOnlyList<TargetLinkRemovalCandidate>> GetTargetLinkRemovalCandidatesAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        var version = _selectionVersion;
        var personVersion = _candidatePersonSelectionVersion;
        var snapshot = await GetGraphSnapshotForSelectionAsync(genealogy.Id);
        if (version != _selectionVersion || personVersion != _candidatePersonSelectionVersion ||
            SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id)
            throw new InvalidOperationException("当前选择已变化，请重新选择解除记录。");
        var names = snapshot.People.ToDictionary(item => item.Id, item => item.Name);
        if (!names.TryGetValue(person.Id, out var subjectName))
            throw new InvalidOperationException("当前人物已不存在，请重新选择。");
        var candidates = new List<TargetLinkRemovalCandidate>();
        foreach (var relationship in snapshot.Relationships.Where(item => item.PersonId == person.Id || item.RelatedPersonId == person.Id))
        {
            if (!names.TryGetValue(relationship.PersonId, out var subject) ||
                (relationship.RelatedPersonId is Guid relatedPersonId && !names.ContainsKey(relatedPersonId)))
                throw new InvalidOperationException("关系中的人物已不存在，请重新选择。");
            var related = relationship.RelatedPersonId is Guid relatedId
                ? names[relatedId]
                : "（无对方）";
            candidates.Add(new TargetLinkRemovalCandidate(relationship.Id, TargetLinkRemovalKind.Relationship,
                $"人物关系：{subject} → {related}（{GetRelationshipKindName(relationship.Kind)}）",
                genealogy.Id, person.Id, version, personVersion, snapshot.UnlinkGeneration, subjectName, subject,
                relationship.RelatedPersonId is null ? null : related, relationship, null));
        }
        foreach (var union in snapshot.Unions.Where(item => item.PartnerOneId == person.Id || item.PartnerTwoId == person.Id))
        {
            if (!names.TryGetValue(union.PartnerOneId, out var first) ||
                (union.PartnerTwoId is Guid partnerId && !names.ContainsKey(partnerId)))
                throw new InvalidOperationException("婚配中的人物已不存在，请重新选择。");
            var second = union.PartnerTwoId is Guid secondId
                ? names[secondId]
                : "（未详）";
            candidates.Add(new TargetLinkRemovalCandidate(union.Id, TargetLinkRemovalKind.FamilyUnion,
                $"婚配：{first} — {second}（{GetFamilyUnionKindName(union.Kind)}）",
                genealogy.Id, person.Id, version, personVersion, snapshot.UnlinkGeneration, subjectName, first,
                union.PartnerTwoId is null ? null : second, null, union));
        }
        return candidates.OrderBy(item => item.Kind).ThenBy(item => item.Description, StringComparer.Ordinal).ToList();
    }

    public async Task UpdateTargetRelationshipDetailsAsync(TargetLinkRemovalCandidate candidate, string? note, string? sourceCitation)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        using var operation = _operationCoordinator?.EnterOperation();
        if (candidate.GenealogyId != SelectedGenealogy?.Id || candidate.ParticipantId != SelectedPerson?.Id ||
            candidate.SelectionVersion != _selectionVersion || candidate.PersonSelectionVersion != _candidatePersonSelectionVersion ||
            candidate.Kind != TargetLinkRemovalKind.Relationship || candidate.RelationshipSnapshot is not { } relationship ||
            candidate.UnionSnapshot is not null || relationship.Id != candidate.Id)
            throw new InvalidOperationException("当前选择已变化，请重新选择关系。");
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).UpdateRelationshipDetailsAsync(genealogy.Id, relationship, person.Id,
            candidate.FirstName, candidate.SecondName, candidate.UnlinkGeneration, note, sourceCitation);
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id) return;
        if (await RefreshTargetTreeAsync(genealogy.Id, version, "关系资料已保存")) Status = "关系资料已保存。";
    }

    public async Task CorrectParentRelationshipKindAsync(TargetLinkRemovalCandidate candidate, PersonRelationshipKind newKind)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        if (candidate.GenealogyId != SelectedGenealogy?.Id || candidate.ParticipantId != SelectedPerson?.Id ||
            candidate.SelectionVersion != _selectionVersion || candidate.PersonSelectionVersion != _candidatePersonSelectionVersion ||
            candidate.Kind != TargetLinkRemovalKind.Relationship || candidate.RelationshipSnapshot is not { } relationship ||
            candidate.UnionSnapshot is not null || relationship.Id != candidate.Id)
            throw new InvalidOperationException("当前选择已变化，请重新选择关系。");
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).CorrectParentRelationshipKindAsync(genealogy.Id, relationship, person.Id,
            candidate.FirstName, candidate.SecondName, candidate.UnlinkGeneration, newKind);
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id) return;
        if (await RefreshTargetTreeAsync(genealogy.Id, version, "父母关系类型已更正")) Status = "父母关系类型已更正。";
    }

    public async Task CompleteUnknownUnionPartnerAsync(TargetLinkRemovalCandidate candidate, Person partner)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        if (candidate.GenealogyId != SelectedGenealogy?.Id || candidate.ParticipantId != SelectedPerson?.Id ||
            candidate.SelectionVersion != _selectionVersion || candidate.PersonSelectionVersion != _candidatePersonSelectionVersion ||
            candidate.Kind != TargetLinkRemovalKind.FamilyUnion || candidate.UnionSnapshot is not { PartnerTwoId: null } union ||
            candidate.RelationshipSnapshot is not null || union.Id != candidate.Id ||
            partner.Id == candidate.ParticipantId || !People.Any(p => ReferenceEquals(p, partner)) || partner.GenealogyId != candidate.GenealogyId)
            throw new InvalidOperationException("选择已变化或婚配不符合补全条件，请重新选择。");
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).CompleteUnknownUnionPartnerAsync(genealogy.Id, union, person.Id,
            candidate.FirstName, candidate.UnlinkGeneration, partner.Id, partner.Name);
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id) return;
        if (await RefreshTargetTreeAsync(genealogy.Id, version, "婚配对方已补全")) Status = "婚配对方已补全。";
    }

    public async Task CorrectFamilyUnionKindAsync(TargetLinkRemovalCandidate candidate, FamilyUnionKind newKind)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        if (candidate.GenealogyId != SelectedGenealogy?.Id || candidate.ParticipantId != SelectedPerson?.Id ||
            candidate.SelectionVersion != _selectionVersion || candidate.PersonSelectionVersion != _candidatePersonSelectionVersion ||
            candidate.Kind != TargetLinkRemovalKind.FamilyUnion || candidate.UnionSnapshot is not { } union ||
            candidate.RelationshipSnapshot is not null || union.Id != candidate.Id)
            throw new InvalidOperationException("当前选择已变化，请重新选择婚配。");
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).CorrectFamilyUnionKindAsync(genealogy.Id, union, person.Id,
            candidate.FirstName, candidate.SecondName, candidate.UnlinkGeneration, newKind);
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id) return;
        if (await RefreshTargetTreeAsync(genealogy.Id, version, "婚配类型已更正")) Status = "婚配类型已更正。";
    }

    public async Task UpdateTargetFamilyUnionDetailsAsync(TargetLinkRemovalCandidate candidate,
        string? startDateText, string? endDateText, string? note)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        using var operation = _operationCoordinator?.EnterOperation();
        if (candidate.GenealogyId != SelectedGenealogy?.Id || candidate.ParticipantId != SelectedPerson?.Id ||
            candidate.SelectionVersion != _selectionVersion || candidate.PersonSelectionVersion != _candidatePersonSelectionVersion ||
            candidate.Kind != TargetLinkRemovalKind.FamilyUnion || candidate.UnionSnapshot is not { } union ||
            candidate.RelationshipSnapshot is not null || union.Id != candidate.Id)
            throw new InvalidOperationException("当前选择已变化，请重新选择婚配。");
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).UpdateFamilyUnionDetailsAsync(genealogy.Id, union, person.Id,
            candidate.FirstName, candidate.SecondName, candidate.UnlinkGeneration, startDateText, endDateText, note);
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id) return;
        if (await RefreshTargetTreeAsync(genealogy.Id, version, "婚配资料已保存")) Status = "婚配资料已保存。";
    }

    /// <summary>
    /// Explicitly unlinks one currently selected person's relationship or union. The persistence operation is one
    /// participant- and genealogy-scoped DELETE; it never deletes people or another graph record.
    /// </summary>
    public Task RemoveTargetLinkAsync(TargetLinkRemovalCandidate candidate) =>
        RemoveTargetLinkCoreAsync(candidate, candidate.Kind == TargetLinkRemovalKind.Relationship ? "人物关系已解除" : "婚配关系已解除",
            candidate.Kind == TargetLinkRemovalKind.Relationship ? "已解除人物关系。" : "已解除婚配关系。");

    private async Task RemoveTargetLinkCoreAsync(TargetLinkRemovalCandidate candidate, string completedOperation, string successStatus)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        using var operation = _operationCoordinator?.EnterOperation();
        if (candidate.GenealogyId != SelectedGenealogy?.Id || candidate.ParticipantId != SelectedPerson?.Id ||
            candidate.SelectionVersion != _selectionVersion || candidate.PersonSelectionVersion != _candidatePersonSelectionVersion)
        {
            Status = "当前选择已变化，请重新选择解除记录。";
            throw new InvalidOperationException("当前选择已变化，请重新选择解除记录。");
        }

        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        var version = _selectionVersion;
        await using var context = _createContext();
        var service = new GenealogyDataService(context);
        try
        {
            switch (candidate.Kind)
            {
                case TargetLinkRemovalKind.Relationship when candidate.RelationshipSnapshot is { } relationship &&
                                                             candidate.UnionSnapshot is null && relationship.Id == candidate.Id:
                    await service.RemoveRelationshipForParticipantAsync(genealogy.Id, relationship, person.Id,
                        candidate.FirstName, candidate.SecondName, candidate.UnlinkGeneration);
                    break;
                case TargetLinkRemovalKind.FamilyUnion when candidate.UnionSnapshot is { } union &&
                                                            candidate.RelationshipSnapshot is null && union.Id == candidate.Id:
                    await service.RemoveFamilyUnionForParticipantAsync(genealogy.Id, union, person.Id,
                        candidate.FirstName, candidate.SecondName, candidate.UnlinkGeneration);
                    break;
                default:
                    throw new InvalidOperationException("解除类型无效。");
            }
        }
        catch (StaleTargetLinkException exception)
        {
            if (version == _selectionVersion && candidate.PersonSelectionVersion == _candidatePersonSelectionVersion &&
                SelectedGenealogy?.Id == genealogy.Id && SelectedPerson?.Id == person.Id)
            {
                if (await RefreshTargetTreeAsync(genealogy.Id, version, "解除候选已过期", candidate.PersonSelectionVersion,
                    refreshPeople: true))
                {
                    Status = "解除候选已过期，当前图数据已重新加载。";
                    if (candidate.Kind == TargetLinkRemovalKind.Relationship)
                        await LoadSelectedBiologicalChildrenAsync();
                }
            }
            throw new InvalidOperationException($"解除候选已过期：{exception.Message}", exception);
        }

        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id) return;
        if (candidate.Kind == TargetLinkRemovalKind.Relationship)
            await LoadSelectedBiologicalChildrenAsync();
        if (await RefreshTargetTreeAsync(genealogy.Id, version, completedOperation))
            Status = successStatus;
    }

    private (Genealogy Genealogy, Person Person) GetCurrentSelectedPersonForEditing()
    {
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var selected = SelectedPerson ?? throw new InvalidOperationException("请先选择目标人物。");
        var person = People.SingleOrDefault(candidate => candidate.Id == selected.Id && candidate.GenealogyId == genealogy.Id)
            ?? throw new InvalidOperationException("所选人物不属于当前族谱，请重新选择。");
        return (genealogy, person);
    }

    private async Task<GenealogyGraphSnapshot> GetGraphSnapshotForSelectionAsync(Guid genealogyId)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var context = _createContext();
            try { return await new GenealogyDataService(context).GetGraphSnapshotAsync(genealogyId); }
            catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6 && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)));
            }
        }
    }

    private static string GetRelationshipKindName(PersonRelationshipKind kind) => kind switch
    {
        PersonRelationshipKind.BiologicalParent => "生物父母",
        PersonRelationshipKind.AdoptiveParent => "养父母",
        PersonRelationshipKind.HeirParent => "继嗣父母",
        PersonRelationshipKind.Spouse => "配偶",
        PersonRelationshipKind.LostAncestry => "上系失考",
        _ => "未知关系"
    };

    private static string GetFamilyUnionKindName(FamilyUnionKind kind) => kind switch
    {
        FamilyUnionKind.Marriage => "婚姻",
        FamilyUnionKind.Partnership => "伴侣关系",
        FamilyUnionKind.Unknown => "类型未详",
        _ => "未知类型"
    };

    private async Task<bool> RefreshTargetTreeAsync(Guid genealogyId, int version, string completedOperation,
        int? personSelectionVersion = null, bool refreshPeople = false)
    {
        bool IsCurrent() => version == _selectionVersion && SelectedGenealogy?.Id == genealogyId &&
            (personSelectionVersion is null || personSelectionVersion == _candidatePersonSelectionVersion);
        if (!IsCurrent()) return false;
        try
        {
            var snapshot = await GetGraphSnapshotForSelectionAsync(genealogyId);
            var tree = TargetMemberTreeProjection.Create(snapshot);
            if (IsCurrent())
            {
                TargetTree = new ObservableCollection<TargetMemberTreeNode>(tree);
                if (refreshPeople)
                {
                    var selectedId = SelectedPerson?.Id;
                    People = new ObservableCollection<Person>(CreatePeople(snapshot, genealogyId));
                    SelectedPerson = People.SingleOrDefault(person => person.Id == selectedId);
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            if (IsCurrent())
            {
                TargetTree.Clear();
                if (refreshPeople)
                {
                    SelectedPerson = null;
                    People.Clear();
                }
                Status = $"{completedOperation}，但目标谱系刷新失败：{ex.Message}";
            }
        }
        return false;
    }
}
