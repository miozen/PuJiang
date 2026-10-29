using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.ViewModels;

/// <summary>创建和选择目标模型族谱；选择仅在当前会话有效。</summary>
public partial class GenealogySelectionViewModel : ObservableObject
{
    private readonly Func<ZupuDbContext> _createContext;
    private readonly TargetContextCoordinator? _operationCoordinator;
    private int _selectionVersion;
    private int _personSelectionVersion;

    [ObservableProperty] private ObservableCollection<Genealogy> _genealogies = new();
    [ObservableProperty] private Genealogy? _selectedGenealogy;
    [ObservableProperty] private ObservableCollection<Person> _people = new();
    [ObservableProperty] private ObservableCollection<TargetMemberTreeNode> _targetTree = new();
    [ObservableProperty] private ObservableCollection<Person> _biologicalChildren = new();
    [ObservableProperty] private Person? _selectedBiologicalChild;
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
        var people = snapshot.People.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Id).Select(p => new Person
        {
            Id = p.Id, GenealogyId = genealogy.Id, Name = p.Name, Sex = p.Sex, BirthDateText = p.BirthDateText,
            DeathDateText = p.DeathDateText, Biography = p.Biography, PhotoPath = p.PhotoPath,
            IsHistoricalRecord = p.IsHistoricalRecord, RecordedGeneration = p.RecordedGeneration,
            DerivedGeneration = p.DerivedGeneration, RecordedBirthOrder = p.RecordedBirthOrder,
            DerivedBirthOrder = p.DerivedBirthOrder, BranchId = p.BranchId
        }).ToList();
        if (version != _selectionVersion) return;
        People = new ObservableCollection<Person>(people);
        TargetTree = new ObservableCollection<TargetMemberTreeNode>(tree);
        Status = $"当前族谱：{genealogy.Name}，{people.Count} 位人物";
    }

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

    public async Task RemoveBiologicalParentRelationshipAsync()
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var parent = SelectedPerson ?? throw new InvalidOperationException("请先选择父母人物。");
        var child = SelectedBiologicalChild ?? throw new InvalidOperationException("请先选择生物子女。");
        var version = _personSelectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).RemoveBiologicalParentRelationshipAsync(genealogy.Id, parent.Id, child.Id);
        if (version == _personSelectionVersion && SelectedGenealogy?.Id == genealogy.Id && SelectedPerson?.Id == parent.Id)
        {
            await LoadSelectedBiologicalChildrenAsync();
            await RefreshTargetTreeAsync(genealogy.Id, _selectionVersion, "生物父子关系已解除");
        }
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
    public async Task AddTargetRelationshipAsync(PersonRelationshipKind kind, Guid? relatedPersonId)
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
            Kind = kind
        });
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id) return;
        if (kind == PersonRelationshipKind.BiologicalParent)
            await LoadSelectedBiologicalChildrenAsync();
        if (await RefreshTargetTreeAsync(genealogy.Id, version, "人物关系已新增"))
            Status = "已新增人物关系。";
    }

    /// <summary>
    /// Creates one explicit, direction-free family union for the selected person. The second partner must be
    /// a currently listed member of the same genealogy; no relationship record or automatic merge is inferred.
    /// </summary>
    public async Task AddTargetFamilyUnionAsync(FamilyUnionKind kind, Guid partnerTwoId)
    {
        using var operation = _operationCoordinator?.EnterOperation();
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        if (!GetCurrentPeopleForEditing().Any(candidate => candidate.Id == partnerTwoId))
            throw new InvalidOperationException("婚配对方必须是当前族谱中的人物。");

        var version = _selectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).AddFamilyUnionAsync(new FamilyUnion
        {
            GenealogyId = genealogy.Id,
            PartnerOneId = person.Id,
            PartnerTwoId = partnerTwoId,
            Kind = kind
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
        var snapshot = await GetGraphSnapshotForSelectionAsync(genealogy.Id);
        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id)
            throw new InvalidOperationException("当前选择已变化，请重新选择解除记录。");
        var names = snapshot.People.ToDictionary(item => item.Id, item => item.Name);
        var candidates = new List<TargetLinkRemovalCandidate>();
        foreach (var relationship in snapshot.Relationships.Where(item => item.PersonId == person.Id || item.RelatedPersonId == person.Id))
        {
            var subject = names.GetValueOrDefault(relationship.PersonId, "已不存在的人物");
            var related = relationship.RelatedPersonId is Guid relatedId
                ? names.GetValueOrDefault(relatedId, "已不存在的人物")
                : "（无对方）";
            candidates.Add(new TargetLinkRemovalCandidate(relationship.Id, TargetLinkRemovalKind.Relationship,
                $"人物关系：{subject} → {related}（{GetRelationshipKindName(relationship.Kind)}）"));
        }
        foreach (var union in snapshot.Unions.Where(item => item.PartnerOneId == person.Id || item.PartnerTwoId == person.Id))
        {
            var first = names.GetValueOrDefault(union.PartnerOneId, "已不存在的人物");
            var second = union.PartnerTwoId is Guid secondId
                ? names.GetValueOrDefault(secondId, "已不存在的人物")
                : "（未详）";
            candidates.Add(new TargetLinkRemovalCandidate(union.Id, TargetLinkRemovalKind.FamilyUnion,
                $"婚配：{first} — {second}（{GetFamilyUnionKindName(union.Kind)}）"));
        }
        return candidates.OrderBy(item => item.Kind).ThenBy(item => item.Description, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Explicitly unlinks one currently selected person's relationship or union. The persistence operation is one
    /// participant- and genealogy-scoped DELETE; it never deletes people or another graph record.
    /// </summary>
    public async Task RemoveTargetLinkAsync(TargetLinkRemovalCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        using var operation = _operationCoordinator?.EnterOperation();
        var (genealogy, person) = GetCurrentSelectedPersonForEditing();
        var version = _selectionVersion;
        await using var context = _createContext();
        var service = new GenealogyDataService(context);
        switch (candidate.Kind)
        {
            case TargetLinkRemovalKind.Relationship:
                await service.RemoveRelationshipForParticipantAsync(genealogy.Id, candidate.Id, person.Id);
                break;
            case TargetLinkRemovalKind.FamilyUnion:
                await service.RemoveFamilyUnionForParticipantAsync(genealogy.Id, candidate.Id, person.Id);
                break;
            default:
                throw new InvalidOperationException("解除类型无效。");
        }

        if (version != _selectionVersion || SelectedGenealogy?.Id != genealogy.Id || SelectedPerson?.Id != person.Id) return;
        if (candidate.Kind == TargetLinkRemovalKind.Relationship)
            await LoadSelectedBiologicalChildrenAsync();
        var completedOperation = candidate.Kind == TargetLinkRemovalKind.Relationship ? "人物关系已解除" : "婚配关系已解除";
        if (await RefreshTargetTreeAsync(genealogy.Id, version, completedOperation))
            Status = candidate.Kind == TargetLinkRemovalKind.Relationship ? "已解除人物关系。" : "已解除婚配关系。";
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

    private async Task<bool> RefreshTargetTreeAsync(Guid genealogyId, int version, string completedOperation)
    {
        try
        {
            var snapshot = await GetGraphSnapshotForSelectionAsync(genealogyId);
            var tree = TargetMemberTreeProjection.Create(snapshot);
            if (version == _selectionVersion && SelectedGenealogy?.Id == genealogyId)
            {
                TargetTree = new ObservableCollection<TargetMemberTreeNode>(tree);
                return true;
            }
        }
        catch (Exception ex)
        {
            if (version == _selectionVersion && SelectedGenealogy?.Id == genealogyId)
            {
                TargetTree.Clear();
                Status = $"{completedOperation}，但目标谱系刷新失败：{ex.Message}";
            }
        }
        return false;
    }
}
