using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ZupuPaiBan.Infrastructure;

namespace ZupuPaiBan.Data;

/// <summary>
/// EF Core tools entry point. It only constructs options and never creates or migrates a database.
/// </summary>
public sealed class ZupuDbContextFactory : IDesignTimeDbContextFactory<ZupuDbContext>
{
    public ZupuDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PUJIANG_DESIGNTIME_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = $"Data Source={AppDataPaths.DatabasePath}";

        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new ZupuDbContext(options);
    }
}
