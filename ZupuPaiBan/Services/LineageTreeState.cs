using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ZupuPaiBan.Services;

public sealed partial class LineageTreeNode : ObservableObject
{
    public LineagePersonProjection Projection { get; }
    public Guid PersonId => Projection.Person.Id;
    public string Label => Projection.Label;
    public LineageTreeNode? Parent { get; internal set; }
    public ObservableCollection<LineageTreeNode> Children { get; } = new();
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isPathAncestor;

    public LineageTreeNode(LineagePersonProjection projection) => Projection = projection;
}

/// <summary>会话内树形视图状态，不写入谱系或持久化选线。</summary>
public sealed class LineageTreeState
{
    private readonly LineageProjectionSession _session;
    private Dictionary<Guid, LineageTreeNode> _nodes = new();
    public IReadOnlyList<LineageTreeNode> Roots { get; private set; } = Array.Empty<LineageTreeNode>();
    public IReadOnlyList<LineageTreeNode> People => _nodes.Values.OrderBy(n => n.Projection.Person.Name, StringComparer.Ordinal).ThenBy(n => n.PersonId).ToList().AsReadOnly();

    public LineageTreeState(LineageProjectionSession session)
    {
        _session = session;
        Rebuild(null);
    }

    public LineageTreeNode GetPerson(Guid id) => _nodes.TryGetValue(id, out var node) ? node :
        throw new InvalidOperationException("人物已失效，请重新打开世系浏览。");

    public IReadOnlyList<LineageTreeNode> Search(string? query)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length == 0) return Array.Empty<LineageTreeNode>();
        return People.Where(n => n.Projection.Person.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            n.PersonId.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)).ToList().AsReadOnly();
    }

    public void Choose(Guid personId, Guid? relationshipId)
    {
        _session.Choose(personId, relationshipId);
        Rebuild(personId);
    }

    public LineageTreeNode Reveal(Guid personId)
    {
        var node = GetPerson(personId);
        foreach (var item in _nodes.Values) item.IsSelected = false;
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent) ancestor.IsExpanded = true;
        node.IsSelected = true;
        Highlight(personId);
        return node;
    }

    public string Highlight(Guid? personId)
    {
        foreach (var item in _nodes.Values) item.IsPathAncestor = false;
        if (personId is not Guid id) return string.Empty;
        var node = GetPerson(id);
        var boundary = node;
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.IsPathAncestor = true;
            boundary = ancestor;
        }
        if (boundary.Projection.NeedsChoice) return "路径止于待选择的父母关系。";
        return boundary.Projection.LostAncestry.Count > 0
            ? "路径止于上系失考或未记录父母；不推断始祖。"
            : "路径止于未记录父母；不推断始祖。";
    }

    public void ClearSelection()
    {
        foreach (var item in _nodes.Values) item.IsSelected = false;
        Highlight(null);
    }

    private void Rebuild(Guid? selectedPersonId)
    {
        var expanded = _nodes.Values.Where(n => n.IsExpanded).Select(n => n.PersonId).ToHashSet();
        var rows = _session.Project();
        var nodes = rows.ToDictionary(row => row.Person.Id, row => new LineageTreeNode(row) { IsExpanded = expanded.Contains(row.Person.Id) });
        var roots = new List<LineageTreeNode>();
        foreach (var row in rows)
        {
            var node = nodes[row.Person.Id];
            if (row.SelectedParent is not { } selected) { roots.Add(node); continue; }
            if (!nodes.TryGetValue(selected.Parent.Id, out var parent)) throw new InvalidOperationException("主线父母不存在，无法显示。");
            node.Parent = parent;
            parent.Children.Add(node);
        }
        _nodes = nodes;
        Roots = roots.AsReadOnly();
        if (selectedPersonId is Guid id) Reveal(id);
    }
}
