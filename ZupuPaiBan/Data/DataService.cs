using System.IO;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public class DataService
{
    private readonly TargetContextCoordinator _coordinator;

    public DataService(TargetContextCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public IDisposable EnterOperation() => _coordinator.EnterOperation();

    public async Task<List<FamilyMember>> GetAllMembersAsync()
    {
        await using var context = _coordinator.CreateContext();
        return await context.Members.AsNoTracking()
            .OrderBy(m => m.Generation)
            .ThenBy(m => m.BirthOrder)
            .ToListAsync();
    }

    public async Task<List<FamilyMember>> GetRootMembersAsync()
    {
        await using var context = _coordinator.CreateContext();
        return await context.Members.AsNoTracking()
            .Where(m => m.FatherId == null)
            .OrderBy(m => m.BirthOrder)
            .ToListAsync();
    }

    public async Task<List<FamilyMember>> GetChildrenAsync(int fatherId)
    {
        await using var context = _coordinator.CreateContext();
        return await context.Members.AsNoTracking()
            .Where(m => m.FatherId == fatherId)
            .OrderBy(m => m.BirthOrder)
            .ToListAsync();
    }

    public async Task<FamilyMember?> GetMemberByIdAsync(int id)
    {
        await using var context = _coordinator.CreateContext();
        return await context.Members.AsNoTracking()
            .Include(m => m.Children)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<FamilyMember> AddMemberAsync(FamilyMember member)
    {
        await using var context = _coordinator.CreateContext();
        context.Members.Add(member);
        await context.SaveChangesAsync();
        return member;
    }

    public async Task UpdateMemberAsync(FamilyMember member)
    {
        await using var context = _coordinator.CreateContext();
        context.Members.Update(member);
        await context.SaveChangesAsync();
    }

    public async Task DeleteMemberAsync(int id)
    {
        await using var context = _coordinator.CreateContext();
        var member = await context.Members.FindAsync(id);
        if (member != null)
        {
            // 将子成员的 FatherId 置空
            var children = await context.Members
                .Where(c => c.FatherId == id)
                .ToListAsync();
            foreach (var child in children)
            {
                child.FatherId = null;
            }
            context.Members.Remove(member);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>Atomically replaces the legacy member table after validating imported references.</summary>
    public async Task ReplaceMembersAsync(IEnumerable<FamilyMember> importedMembers)
    {
        var members = PrepareImportedMembers(importedMembers);
        ValidateImportedMembers(members, []);

        await using var context = _coordinator.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            // The legacy self-reference is restrictive, so detach it before clearing the table.
            await context.Members.ExecuteUpdateAsync(setters => setters
                .SetProperty(member => member.FatherId, (int?)null));
            await context.Members.ExecuteDeleteAsync();
            context.Members.AddRange(members);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>Atomically appends a validated legacy member import.</summary>
    public async Task AppendMembersAsync(IEnumerable<FamilyMember> importedMembers)
    {
        var members = PrepareImportedMembers(importedMembers);
        await using var context = _coordinator.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var existingIds = await context.Members.Select(member => member.Id).ToListAsync();
            ValidateImportedMembers(members, existingIds);
            context.Members.AddRange(members);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static List<FamilyMember> PrepareImportedMembers(IEnumerable<FamilyMember> importedMembers)
    {
        ArgumentNullException.ThrowIfNull(importedMembers);
        var members = importedMembers.Select(member => new FamilyMember
        {
            Id = member.Id,
            Name = member.Name,
            Gender = member.Gender,
            BirthDate = member.BirthDate,
            DeathDate = member.DeathDate,
            SpouseName = member.SpouseName,
            Biography = member.Biography,
            PhotoPath = member.PhotoPath,
            FatherId = member.FatherId,
            Generation = member.Generation,
            BirthOrder = member.BirthOrder
        }).ToList();
        if (members.Count == 0) throw new InvalidDataException("导入文件中没有成员数据。");
        return members;
    }

    private static void ValidateImportedMembers(IReadOnlyList<FamilyMember> members, IEnumerable<int> existingIds)
    {
        var importedIds = new HashSet<int>();
        foreach (var member in members)
        {
            if (member.Id <= 0) throw new InvalidDataException("导入成员 ID 必须为正整数。");
            if (string.IsNullOrWhiteSpace(member.Name)) throw new InvalidDataException("导入成员姓名不能为空。");
            if (!importedIds.Add(member.Id)) throw new InvalidDataException("导入文件包含重复的成员 ID。");
            if (member.FatherId == member.Id) throw new InvalidDataException("成员不能将自己设为父亲。");
        }

        var availableParents = importedIds.Concat(existingIds).ToHashSet();
        foreach (var member in members)
        {
            if (member.FatherId is int fatherId && !availableParents.Contains(fatherId))
                throw new InvalidDataException("导入成员引用了不存在的父亲。");
        }

        var fathers = members.ToDictionary(member => member.Id, member => member.FatherId);
        foreach (var member in members)
        {
            var visited = new HashSet<int>();
            var current = member.Id;
            while (fathers.TryGetValue(current, out var fatherId) && fatherId is int father && fathers.ContainsKey(father))
            {
                if (!visited.Add(current)) throw new InvalidDataException("导入成员的父子关系存在循环。");
                current = father;
            }
        }
    }

    public async Task<int> GetMaxGenerationAsync()
    {
        await using var context = _coordinator.CreateContext();
        return await context.Members.AnyAsync()
            ? await context.Members.MaxAsync(m => m.Generation)
            : 0;
    }

    public async Task<int> GetNextBirthOrderAsync(int? fatherId)
    {
        await using var context = _coordinator.CreateContext();
        if (fatherId == null)
        {
            var count = await context.Members.CountAsync(m => m.FatherId == null);
            return count + 1;
        }
        var childCount = await context.Members.CountAsync(m => m.FatherId == fatherId);
        return childCount + 1;
    }

    [Obsolete("Use Database.MigrateAsync through the application startup path.")]
    public async Task EnsureCreatedAsync()
    {
        await using var context = _coordinator.CreateContext();
        await context.Database.EnsureCreatedAsync();
    }
}
