using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace ZupuPaiBan.ViewModels;

/// <summary>创建和选择目标模型族谱；选择仅在当前会话有效。</summary>
public partial class GenealogySelectionViewModel : ObservableObject
{
    private readonly Func<ZupuDbContext> _createContext;
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
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var person = SelectedPerson ?? throw new InvalidOperationException("请先选择目标人物。");
        if (person.GenealogyId != genealogy.Id || !People.Any(p => ReferenceEquals(p, person)))
            throw new InvalidOperationException("所选人物不属于当前族谱，请重新选择。");
        return TargetTree.SingleOrDefault(node => node.PersonId == person.Id)
            ?? throw new InvalidOperationException("所选人物已变化，请重新选择。");
    }

    public async Task SaveTreePersonProfileAsync(TargetMemberTreeNode node, string? name, PersonSex sex,
        string? birthDate, string? deathDate, string? biography)
    {
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

    public GenealogySelectionViewModel(Func<ZupuDbContext> createContext) => _createContext = createContext;

    public async Task LoadAsync()
    {
        await using var context = _createContext();
        var items = await new GenealogyDataService(context).GetGenealogiesAsync();
        Genealogies = new ObservableCollection<Genealogy>(items);
    }

    public async Task CreateAsync()
    {
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
        await using var context = _createContext();
        var snapshot = await new GenealogyDataService(context).GetGraphSnapshotAsync(genealogy.Id);
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
        var genealogy = SelectedGenealogy ?? throw new InvalidOperationException("请先选择族谱。");
        var parent = SelectedPerson ?? throw new InvalidOperationException("请先选择父母人物。");
        var child = SelectedBiologicalChild ?? throw new InvalidOperationException("请先选择生物子女。");
        var version = _personSelectionVersion;
        await using var context = _createContext();
        await new GenealogyDataService(context).RemoveBiologicalParentRelationshipAsync(genealogy.Id, parent.Id, child.Id);
        if (version == _personSelectionVersion && SelectedGenealogy?.Id == genealogy.Id && SelectedPerson?.Id == parent.Id)
        {
            await LoadSelectedBiologicalChildrenAsync();
            await RefreshTargetTreeAsync(genealogy.Id, _selectionVersion);
        }
    }

    public async Task AddExistingBiologicalChildAsync()
    {
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
            await RefreshTargetTreeAsync(genealogy.Id, _selectionVersion);
        }
    }

    private async Task RefreshTargetTreeAsync(Guid genealogyId, int version)
    {
        try
        {
            await using var context = _createContext();
            var snapshot = await new GenealogyDataService(context).GetGraphSnapshotAsync(genealogyId);
            var tree = TargetMemberTreeProjection.Create(snapshot);
            if (version == _selectionVersion && SelectedGenealogy?.Id == genealogyId)
                TargetTree = new ObservableCollection<TargetMemberTreeNode>(tree);
        }
        catch (Exception ex)
        {
            if (version == _selectionVersion && SelectedGenealogy?.Id == genealogyId)
            {
                TargetTree.Clear();
                Status = $"关系已保存，但目标谱系刷新失败：{ex.Message}";
            }
        }
    }
}
