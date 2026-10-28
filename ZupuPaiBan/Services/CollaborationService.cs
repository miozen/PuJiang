using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Services;

public class CollaborationMember
{
    [JsonPropertyName("tempId")]
    public int TempId { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "Male";
    [JsonPropertyName("birthDate")]
    public string BirthDate { get; set; } = "";
    [JsonPropertyName("deathDate")]
    public string? DeathDate { get; set; }
    [JsonPropertyName("spouseName")]
    public string? SpouseName { get; set; }
    [JsonPropertyName("biography")]
    public string? Biography { get; set; }
    [JsonPropertyName("fatherTempId")]
    public int? FatherTempId { get; set; }
    [JsonPropertyName("generation")]
    public int Generation { get; set; }
    [JsonPropertyName("birthOrder")]
    public int BirthOrder { get; set; }
}

public class CollaborationPackage
{
    [JsonPropertyName("exportTime")]
    public string ExportTime { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("rootMemberName")]
    public string RootMemberName { get; set; } = "";
    [JsonPropertyName("members")]
    public List<CollaborationMember> Members { get; set; } = new();
}

public class CollaborationService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 导出指定成员及其所有后代为 JSON 协作包
    /// </summary>
    public async Task ExportSubtreeJson(int rootMemberId, List<FamilyMember> allMembers, string filePath)
    {
        // BFS 收集子树
        var subtree = new List<FamilyMember>();
        var root = allMembers.FirstOrDefault(m => m.Id == rootMemberId);
        if (root == null) throw new ArgumentException("找不到指定的成员");

        var queue = new Queue<FamilyMember>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var member = queue.Dequeue();
            subtree.Add(member);
            var children = allMembers.Where(m => m.FatherId == member.Id);
            foreach (var child in children)
                queue.Enqueue(child);
        }

        // 映射为相对 ID
        var idMap = new Dictionary<int, int>(); // original ID -> tempId
        int tempIdCounter = 1;
        foreach (var member in subtree)
        {
            idMap[member.Id] = tempIdCounter++;
        }

        var pkg = new CollaborationPackage
        {
            RootMemberName = root.Name,
            Members = subtree.Select(m => new CollaborationMember
            {
                TempId = idMap[m.Id],
                Name = m.Name,
                Gender = m.Gender == Gender.Male ? "Male" : "Female",
                BirthDate = m.BirthDate,
                DeathDate = m.DeathDate,
                SpouseName = m.SpouseName,
                Biography = m.Biography,
                FatherTempId = m.FatherId != null && idMap.ContainsKey(m.FatherId.Value) ? idMap[m.FatherId.Value] : null,
                Generation = m.Generation,
                BirthOrder = m.BirthOrder
            }).ToList()
        };

        var json = JsonSerializer.Serialize(pkg, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// 导入 JSON 协作包，挂在指定目标成员下
    /// </summary>
    public async Task<int> ImportSubtreeJson(string filePath, int targetParentId, DataService dataService)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var pkg = JsonSerializer.Deserialize<CollaborationPackage>(json);
        if (pkg == null || pkg.Members.Count == 0)
            throw new InvalidOperationException("协作包中没有成员数据");

        // 获取当前最大ID
        var allMembers = await dataService.GetAllMembersAsync();
        int maxId = allMembers.Count > 0 ? allMembers.Max(m => m.Id) : 0;

        // 获取目标父成员的代数
        var targetParent = allMembers.FirstOrDefault(m => m.Id == targetParentId)
            ?? throw new InvalidDataException("找不到协作导入的目标父成员。");
        int baseGeneration = targetParent.Generation;
        int rootOrigGen = pkg.Members.Where(m => m.FatherTempId == null).Select(m => m.Generation).FirstOrDefault();
        if (rootOrigGen == 0) rootOrigGen = 1;

        // 映射 tempId -> newId
        var idMap = new Dictionary<int, int>();
        int newIdCounter = maxId + 1;
        foreach (var cm in pkg.Members)
        {
            idMap[cm.TempId] = newIdCounter++;
        }

        int importedCount = 0;
        var membersToAppend = new List<FamilyMember>();

        foreach (var cm in pkg.Members)
        {
            int? fatherDbId = null;
            if (cm.FatherTempId is int fatherTempId)
            {
                if (!idMap.TryGetValue(fatherTempId, out var mappedFatherId))
                    throw new InvalidDataException("协作包引用了不存在的父成员。");
                fatherDbId = mappedFatherId;
            }
            else
            {
                // 根成员挂在目标父成员下
                fatherDbId = targetParentId;
            }

            int gen = baseGeneration + (cm.Generation - rootOrigGen) + 1;

            var member = new FamilyMember
            {
                Id = idMap[cm.TempId],
                Name = cm.Name,
                Gender = cm.Gender == "Female" ? Gender.Female : Gender.Male,
                BirthDate = cm.BirthDate,
                DeathDate = cm.DeathDate,
                SpouseName = cm.SpouseName,
                Biography = cm.Biography,
                FatherId = fatherDbId,
                Generation = gen,
                BirthOrder = cm.BirthOrder
            };

            membersToAppend.Add(member);
            importedCount++;
        }

        await dataService.AppendMembersAsync(membersToAppend);
        return importedCount;
    }
}
