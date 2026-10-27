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
