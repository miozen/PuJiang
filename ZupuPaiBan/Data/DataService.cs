using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public class DataService
{
    private readonly ZupuDbContext _context;

    public DataService(ZupuDbContext context)
    {
        _context = context;
    }

    public async Task<List<FamilyMember>> GetAllMembersAsync()
    {
        return await _context.Members
            .OrderBy(m => m.Generation)
            .ThenBy(m => m.BirthOrder)
            .ToListAsync();
    }

    public async Task<List<FamilyMember>> GetRootMembersAsync()
    {
        return await _context.Members
            .Where(m => m.FatherId == null)
            .OrderBy(m => m.BirthOrder)
            .ToListAsync();
    }

    public async Task<List<FamilyMember>> GetChildrenAsync(int fatherId)
    {
        return await _context.Members
            .Where(m => m.FatherId == fatherId)
            .OrderBy(m => m.BirthOrder)
            .ToListAsync();
    }

    public async Task<FamilyMember?> GetMemberByIdAsync(int id)
    {
        return await _context.Members
            .Include(m => m.Children)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<FamilyMember> AddMemberAsync(FamilyMember member)
    {
        _context.Members.Add(member);
        await _context.SaveChangesAsync();
        return member;
    }

    public async Task UpdateMemberAsync(FamilyMember member)
    {
        _context.Members.Update(member);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteMemberAsync(int id)
    {
        var member = await _context.Members.FindAsync(id);
        if (member != null)
        {
            // 将子成员的 FatherId 置空
            var children = await _context.Members
                .Where(c => c.FatherId == id)
                .ToListAsync();
            foreach (var child in children)
            {
                child.FatherId = null;
            }
            _context.Members.Remove(member);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<int> GetMaxGenerationAsync()
    {
        return await _context.Members.AnyAsync()
            ? await _context.Members.MaxAsync(m => m.Generation)
            : 0;
    }

    public async Task<int> GetNextBirthOrderAsync(int? fatherId)
    {
        if (fatherId == null)
        {
            var count = await _context.Members.CountAsync(m => m.FatherId == null);
            return count + 1;
        }
        var childCount = await _context.Members.CountAsync(m => m.FatherId == fatherId);
        return childCount + 1;
    }

    [Obsolete("Use Database.MigrateAsync through the application startup path.")]
    public async Task EnsureCreatedAsync()
    {
        await _context.Database.EnsureCreatedAsync();
    }
}
