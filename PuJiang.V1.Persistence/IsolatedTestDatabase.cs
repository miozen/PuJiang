using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;

namespace PuJiang.V1.Persistence;

// A capability for a newly allocated test database only; no caller-supplied path or production registration.
public sealed class IsolatedTestDatabase
{
    private readonly string _connectionString;
    public string DatabasePath { get; }
    private IsolatedTestDatabase(string path)
    {
        DatabasePath = path;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString();
    }
    public static IsolatedTestDatabase CreateNew()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuJiang.V1.IsolatedTests");
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Test root cannot be a link.");
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".sqlite");
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        return new IsolatedTestDatabase(path);
    }
    public V1Context OpenContext() => new(_connectionString);
}

public sealed class V1Context : DbContext
{
    private readonly string _connectionString;
    internal V1Context(string connectionString) => _connectionString = connectionString;
    public DbSet<Genealogy> Genealogies => Set<Genealogy>();
    public DbSet<Lineage> Lineages => Set<Lineage>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Placement> Placements => Set<Placement>();
    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connectionString);
    protected override void OnModelCreating(ModelBuilder b)
    {
        const string nonempty = "Id <> '00000000-0000-0000-0000-000000000000'";
        b.Entity<Genealogy>().ToTable("Genealogies", t => t.HasCheckConstraint("CK_Genealogy_Id", nonempty)).HasKey(x => x.Id);
        b.Entity<Lineage>().ToTable("Lineages", t => t.HasCheckConstraint("CK_Lineage_Id", nonempty)).HasKey(x => x.Id);
        b.Entity<Lineage>().HasOne<Genealogy>().WithMany().HasForeignKey(x => x.GenealogyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Person>().ToTable("People", t => t.HasCheckConstraint("CK_Person_Id", nonempty)).HasKey(x => x.Id);
        b.Entity<Person>().HasAlternateKey(x => new { x.LineageId, x.Id });
        b.Entity<Person>().Property(x => x.Name).IsRequired();
        b.Entity<Person>().HasOne<Lineage>().WithMany().HasForeignKey(x => x.LineageId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Placement>().ToTable("Placements", t => t.HasCheckConstraint("CK_Placement_Id", nonempty)).HasKey(x => x.Id);
        b.Entity<Placement>().HasOne<Person>().WithMany().HasForeignKey(x => new { x.LineageId, x.PersonId }).HasPrincipalKey(x => new { x.LineageId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
