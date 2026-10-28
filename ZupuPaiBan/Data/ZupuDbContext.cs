using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public class ZupuDbContext : DbContext
{
    // Legacy prototype table kept temporarily so the existing WPF prototype remains buildable.
    public DbSet<FamilyMember> Members => Set<FamilyMember>();
    public DbSet<Genealogy> Genealogies => Set<Genealogy>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<PersonRelationship> PersonRelationships => Set<PersonRelationship>();
    public DbSet<FamilyUnion> FamilyUnions => Set<FamilyUnion>();
    public DbSet<FamilyBranch> FamilyBranches => Set<FamilyBranch>();

    private readonly string _dbPath;
    private readonly Action<bool>? _onDispose;
    private int _released;

    public ZupuDbContext()
    {
        AppDataPaths.EnsureDirectories();
        _dbPath = AppDataPaths.DatabasePath;
    }

    public ZupuDbContext(DbContextOptions<ZupuDbContext> options) : base(options)
    {
        _dbPath = string.Empty;
    }

    internal ZupuDbContext(DbContextOptions<ZupuDbContext> options, Action<bool> onDispose) : this(options)
    {
        _onDispose = onDispose;
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0) return;
        var failed = true;
        try { base.Dispose(); failed = false; }
        finally { _onDispose?.Invoke(failed); }
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0) return;
        var failed = true;
        try { await base.DisposeAsync(); failed = false; }
        finally { _onDispose?.Invoke(failed); }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite(new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString());
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureTargetModel(modelBuilder);

        modelBuilder.Entity<FamilyMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.BirthDate).HasMaxLength(20);
            entity.Property(e => e.DeathDate).HasMaxLength(20);
            entity.Property(e => e.SpouseName).HasMaxLength(50);
            entity.Property(e => e.PhotoPath).HasMaxLength(500);
            entity.Property(e => e.Biography).HasMaxLength(2000);

            entity.HasOne(e => e.Father)
                  .WithMany(e => e.Children)
                  .HasForeignKey(e => e.FatherId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Genealogy>(entity =>
        {
            entity.ToTable("Genealogies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Surname).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.HasIndex(e => e.Name);
        });

        modelBuilder.Entity<FamilyBranch>(entity =>
        {
            entity.ToTable("FamilyBranches", table => table.HasCheckConstraint(
                "CK_FamilyBranches_NotOwnParent", "ParentBranchId IS NULL OR ParentBranchId <> Id"));
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.Id, e.GenealogyId });
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => new { e.GenealogyId, e.ParentBranchId, e.Name }).IsUnique();
            entity.HasOne(e => e.Genealogy).WithMany(g => g.Branches)
                .HasForeignKey(e => e.GenealogyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ParentBranch).WithMany(b => b.ChildBranches)
                .HasForeignKey(e => new { e.ParentBranchId, e.GenealogyId })
                .HasPrincipalKey(b => new { b.Id, b.GenealogyId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Person>(entity =>
        {
            entity.ToTable("People");
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.Id, e.GenealogyId });
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.BirthDateText).HasMaxLength(100);
            entity.Property(e => e.DeathDateText).HasMaxLength(100);
            entity.Property(e => e.Biography).HasMaxLength(4000);
            entity.Property(e => e.PhotoPath).HasMaxLength(500);
            entity.Property(e => e.RecordedGeneration).HasMaxLength(100);
            entity.Property(e => e.RecordedBirthOrder).HasMaxLength(100);
            entity.HasIndex(e => new { e.GenealogyId, e.Name });
            entity.HasOne(e => e.Genealogy).WithMany(g => g.People)
                .HasForeignKey(e => e.GenealogyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Branch).WithMany()
                .HasForeignKey(e => new { e.BranchId, e.GenealogyId })
                .HasPrincipalKey(b => new { b.Id, b.GenealogyId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PersonRelationship>(entity =>
        {
            entity.ToTable("PersonRelationships", table =>
            {
                table.HasCheckConstraint("CK_PersonRelationships_NotSelf", "RelatedPersonId IS NULL OR PersonId <> RelatedPersonId");
                table.HasCheckConstraint("CK_PersonRelationships_LostAncestry", "(Kind = 5 AND RelatedPersonId IS NULL) OR (Kind <> 5 AND RelatedPersonId IS NOT NULL)");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Note).HasMaxLength(2000);
            entity.Property(e => e.SourceCitation).HasMaxLength(1000);
            entity.HasIndex(e => new { e.GenealogyId, e.PersonId, e.RelatedPersonId, e.Kind }).IsUnique();
            entity.HasOne(e => e.Genealogy).WithMany()
                .HasForeignKey(e => e.GenealogyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Person).WithMany()
                .HasForeignKey(e => new { e.PersonId, e.GenealogyId })
                .HasPrincipalKey(p => new { p.Id, p.GenealogyId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.RelatedPerson).WithMany()
                .HasForeignKey(e => new { e.RelatedPersonId, e.GenealogyId })
                .HasPrincipalKey(p => new { p.Id, p.GenealogyId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FamilyUnion>(entity =>
        {
            entity.ToTable("FamilyUnions", table => table.HasCheckConstraint(
                "CK_FamilyUnions_NotSamePartner", "PartnerTwoId IS NULL OR PartnerOneId <> PartnerTwoId"));
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StartDateText).HasMaxLength(100);
            entity.Property(e => e.EndDateText).HasMaxLength(100);
            entity.Property(e => e.Note).HasMaxLength(2000);
            entity.HasIndex(e => new { e.GenealogyId, e.PartnerOneId, e.PartnerTwoId, e.Kind }).IsUnique();
            entity.HasOne(e => e.Genealogy).WithMany()
                .HasForeignKey(e => e.GenealogyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.PartnerOne).WithMany()
                .HasForeignKey(e => new { e.PartnerOneId, e.GenealogyId })
                .HasPrincipalKey(p => new { p.Id, p.GenealogyId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PartnerTwo).WithMany()
                .HasForeignKey(e => new { e.PartnerTwoId, e.GenealogyId })
                .HasPrincipalKey(p => new { p.Id, p.GenealogyId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
