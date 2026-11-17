using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;

namespace PuJiang.V1.Persistence;

// A capability for a newly allocated test database only; no caller-supplied path or production registration.
public sealed class IsolatedTestDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly IDisposable? _pathLease;
    private bool _disposed;
    public string DatabasePath { get; }
    private IsolatedTestDatabase(string path, IDisposable? pathLease = null)
    {
        DatabasePath = path;
        _pathLease = pathLease;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString();
    }
    internal static IsolatedTestDatabase FromControlledFile(string path, IDisposable pathLease) => new(path, pathLease);
    public void Dispose() { _disposed = true; _pathLease?.Dispose(); }
    public static IsolatedTestDatabase CreateNew()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuJiang.V1.IsolatedTests");
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Test root cannot be a link.");
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".sqlite");
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        return new IsolatedTestDatabase(path);
    }
    public V1Context OpenContext() { ObjectDisposedException.ThrowIf(_disposed, this); return new(_connectionString); }
}

public sealed class V1Context : DbContext
{
    private readonly string _connectionString;
    internal V1Context(string connectionString) => _connectionString = connectionString;
    public DbSet<Genealogy> Genealogies => Set<Genealogy>();
    public DbSet<Lineage> Lineages => Set<Lineage>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Placement> Placements => Set<Placement>();
    public DbSet<BiologicalParent> BiologicalParents => Set<BiologicalParent>();
    public DbSet<SpouseUnion> SpouseUnions => Set<SpouseUnion>();
    public DbSet<SpouseOrderGroup> SpouseOrderGroups => Set<SpouseOrderGroup>();
    public DbSet<SpouseOrderEntry> SpouseOrderEntries => Set<SpouseOrderEntry>();
    public DbSet<SpouseOrderRepairReceipt> SpouseOrderRepairReceipts => Set<SpouseOrderRepairReceipt>();
    public DbSet<LineageActivity> LineageActivities => Set<LineageActivity>();
    internal DbSet<LineageCreateReceipt> LineageCreateReceipts => Set<LineageCreateReceipt>();
    internal DbSet<LineageUpdateReceipt> LineageUpdateReceipts => Set<LineageUpdateReceipt>();
    internal DbSet<RootDetailsReceipt> RootDetailsReceipts => Set<RootDetailsReceipt>();
    internal DbSet<SpouseOrderReceipt> SpouseOrderReceipts => Set<SpouseOrderReceipt>();
    internal DbSet<FirstRootReceipt> FirstRootReceipts => Set<FirstRootReceipt>();
    internal DbSet<ChildReceipt> ChildReceipts => Set<ChildReceipt>();
    internal DbSet<SiblingReceipt> SiblingReceipts => Set<SiblingReceipt>();
    internal DbSet<GenerationReceipt> GenerationReceipts => Set<GenerationReceipt>();
    internal DbSet<ChildOrderVersion> ChildOrderVersions => Set<ChildOrderVersion>();
    internal DbSet<ChildOrderReceipt> ChildOrderReceipts => Set<ChildOrderReceipt>();
    internal DbSet<SpouseReceipt> SpouseReceipts => Set<SpouseReceipt>();
    internal DbSet<MotherReceipt> MotherReceipts => Set<MotherReceipt>();
    internal DbSet<PersonProfileReceipt> PersonProfileReceipts => Set<PersonProfileReceipt>();
    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connectionString);
    protected override void OnModelCreating(ModelBuilder b)
    {
        const string nonempty = "Id <> '00000000-0000-0000-0000-000000000000'";
        b.Entity<Genealogy>().ToTable("Genealogies", t => t.HasCheckConstraint("CK_Genealogy_Id", nonempty)).HasKey(x => x.Id);
        b.Entity<Lineage>().ToTable("Lineages", t => t.HasCheckConstraint("CK_Lineage_Id", nonempty)).HasKey(x => x.Id);
        b.Entity<Lineage>().HasOne<Genealogy>().WithMany().HasForeignKey(x => x.GenealogyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LineageActivity>().HasKey(x => x.Id);
        b.Entity<LineageActivity>().HasIndex(x => x.OperationId).IsUnique();
        b.Entity<LineageActivity>().HasOne<Lineage>().WithMany().HasForeignKey(x => x.LineageId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LineageCreateReceipt>().HasKey(x => x.OperationId);
        b.Entity<LineageUpdateReceipt>().HasKey(x => x.OperationId);
        b.Entity<RootDetailsReceipt>().HasKey(x => x.OperationId);
        b.Entity<SiblingReceipt>().HasKey(x => x.OperationId);
        b.Entity<GenerationReceipt>().HasKey(x => x.OperationId);
        b.Entity<ChildOrderVersion>().HasKey(x => new { x.LineageId, x.ParentPlacementId });
        b.Entity<ChildOrderVersion>().HasOne<Placement>().WithMany().HasForeignKey(x => new { x.LineageId, Id = x.ParentPlacementId }).HasPrincipalKey(x => new { x.LineageId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ChildOrderReceipt>().HasKey(x => x.OperationId);
        b.Entity<Person>().ToTable("People", t => t.HasCheckConstraint("CK_Person_Id", nonempty)).HasKey(x => x.Id);
        b.Entity<Person>().HasAlternateKey(x => new { x.LineageId, x.Id });
        b.Entity<Person>().Property(x => x.Name).IsRequired();
        b.Entity<Person>().ToTable(t => t.HasCheckConstraint("CK_Person_Name", "length(trim(replace(replace(replace(Name, char(9), ''), char(10), ''), char(13), ''))) > 0"));
        b.Entity<Person>().ToTable(t => t.HasCheckConstraint("CK_Person_Sex", "Sex IN (0, 1, 2)"));
        b.Entity<Person>().HasOne<Lineage>().WithMany().HasForeignKey(x => x.LineageId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Placement>().ToTable("Placements", t => t.HasCheckConstraint("CK_Placement_Id", nonempty)).HasKey(x => x.Id);
        b.Entity<Placement>().ToTable(t => t.HasCheckConstraint("CK_Placement_Order", "(ParentPlacementId IS NULL AND Position IS NULL) OR (ParentPlacementId IS NOT NULL AND Position IS NOT NULL AND Position > 0)"));
        b.Entity<Placement>().HasAlternateKey(x => new { x.LineageId, x.Id });
        b.Entity<Placement>().HasOne<Person>().WithMany().HasForeignKey(x => new { x.LineageId, x.PersonId }).HasPrincipalKey(x => new { x.LineageId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Placement>().HasOne<Placement>().WithMany().HasForeignKey(x => new { x.LineageId, x.ParentPlacementId }).HasPrincipalKey(x => new { x.LineageId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BiologicalParent>().ToTable("BiologicalParents", t => { t.HasCheckConstraint("CK_BiologicalParent_Id", nonempty); t.HasCheckConstraint("CK_BiologicalParent_Role", "Role IN (1, 2)"); t.HasCheckConstraint("CK_BiologicalParent_Distinct", "ChildId <> ParentId"); }).HasKey(x => x.Id);
        b.Entity<BiologicalParent>().HasIndex(x => new { x.LineageId, x.ChildId, x.Role }).IsUnique();
        b.Entity<BiologicalParent>().HasOne<Person>().WithMany().HasForeignKey(x => new { x.LineageId, x.ChildId }).HasPrincipalKey(x => new { x.LineageId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BiologicalParent>().HasOne<Person>().WithMany().HasForeignKey(x => new { x.LineageId, x.ParentId }).HasPrincipalKey(x => new { x.LineageId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SpouseUnion>().ToTable("SpouseUnions", t => { t.HasCheckConstraint("CK_SpouseUnion_Id", nonempty); t.HasCheckConstraint("CK_SpouseUnion_Distinct", "FatherId <> SpouseId"); }).HasKey(x => x.Id);
        b.Entity<SpouseUnion>().HasAlternateKey(x => new { x.LineageId, x.FatherId, x.Id });
        b.Entity<SpouseUnion>().HasIndex(x => new { x.LineageId, x.FatherId, x.SpouseId }).IsUnique();
        b.Entity<SpouseUnion>().HasOne<Person>().WithMany().HasForeignKey(x => new { x.LineageId, x.FatherId }).HasPrincipalKey(x => new { x.LineageId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SpouseUnion>().HasOne<Person>().WithMany().HasForeignKey(x => new { x.LineageId, x.SpouseId }).HasPrincipalKey(x => new { x.LineageId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SpouseOrderGroup>().ToTable("SpouseOrderGroups", t => t.HasCheckConstraint("CK_SpouseOrder_Revision", "Revision >= 0")).HasKey(x => new { x.LineageId, x.FatherId });
        b.Entity<SpouseOrderGroup>().HasOne<Person>().WithMany().HasForeignKey(x => new { x.LineageId, x.FatherId }).HasPrincipalKey(x => new { x.LineageId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SpouseOrderEntry>().ToTable("SpouseOrderEntries", t => t.HasCheckConstraint("CK_SpouseOrder_Position", "Position > 0")).HasKey(x => new { x.LineageId, x.FatherId, x.UnionId });
        b.Entity<SpouseOrderEntry>().HasIndex(x => new { x.LineageId, x.FatherId, x.Position }).IsUnique();
        b.Entity<SpouseOrderEntry>().HasOne<SpouseOrderGroup>().WithMany().HasForeignKey(x => new { x.LineageId, x.FatherId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SpouseOrderEntry>().HasOne<SpouseUnion>().WithMany().HasForeignKey(x => new { x.LineageId, x.FatherId, Id = x.UnionId }).HasPrincipalKey(x => new { x.LineageId, x.FatherId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SpouseOrderReceipt>().HasKey(x => x.OperationId);
        b.Entity<SpouseOrderRepairReceipt>().HasKey(x => x.OperationId);
        b.Entity<FirstRootReceipt>().HasKey(x => x.OperationId);
        b.Entity<FirstRootReceipt>().Property(x => x.Name).IsRequired();
        b.Entity<FirstRootReceipt>().HasOne<Placement>().WithMany().HasForeignKey(x => x.PlacementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ChildReceipt>().HasKey(x => x.OperationId);
        b.Entity<ChildReceipt>().Property(x => x.Name).IsRequired();
        b.Entity<ChildReceipt>().HasOne<Placement>().WithMany().HasForeignKey(x => x.PlacementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SpouseReceipt>().HasKey(x => x.OperationId);
        b.Entity<SpouseReceipt>().Property(x => x.Name).IsRequired();
        b.Entity<SpouseReceipt>().HasOne<SpouseUnion>().WithMany().HasForeignKey(x => x.UnionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MotherReceipt>().HasKey(x => x.OperationId);
        b.Entity<PersonProfileReceipt>().HasKey(x => x.OperationId);
    }
}
