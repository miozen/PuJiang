using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;

namespace PuJiang.V1.Persistence;

// Test fixture setup only: always allocates a fresh isolated database with fictional data.
public sealed record IsolatedSample(IsolatedTestDatabase Database, Guid GenealogyId, Guid LineageId, string Surname)
{
    public static async Task<IsolatedSample> CreateAsync()
    {
        var database = IsolatedTestDatabase.CreateNew();
        var genealogy = Guid.NewGuid(); var lineage = Guid.NewGuid();
        await using var context = database.OpenContext();
        await context.Database.EnsureCreatedAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Genealogies.Add(new Genealogy(genealogy, "虚构修谱项目", "冯"));
        context.Lineages.Add(new Lineage(lineage, genealogy, "虚构空谱系"));
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new IsolatedSample(database, genealogy, lineage, "冯");
    }
}
