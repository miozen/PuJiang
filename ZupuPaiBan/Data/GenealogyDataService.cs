using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

/// <summary>目标模型的最小应用服务：所有读取均显式按族谱范围过滤。</summary>
public sealed class GenealogyDataService
{
    private readonly ZupuDbContext _context;

    public GenealogyDataService(ZupuDbContext context) => _context = context;

    public Task<List<Genealogy>> GetGenealogiesAsync(CancellationToken cancellationToken = default) =>
        _context.Genealogies.AsNoTracking().OrderBy(g => g.Name).ToListAsync(cancellationToken);

    public async Task<GenealogyGraphSnapshot> GetGraphSnapshotAsync(Guid genealogyId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var genealogy = await _context.Genealogies.AsNoTracking().Where(g => g.Id == genealogyId)
            .Select(g => new GenealogyGraphInfo(g.Id, g.Name, g.Surname, g.Description, g.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("目标族谱不存在。");
        var people = await _context.People.AsNoTracking().Where(p => p.GenealogyId == genealogyId).OrderBy(p => p.Id)
            .Select(p => new GenealogyPersonData(p.Id, p.Name, p.Sex, p.BirthDateText, p.DeathDateText,
                p.Biography, p.PhotoPath, p.IsHistoricalRecord, p.RecordedGeneration, p.DerivedGeneration,
                p.RecordedBirthOrder, p.DerivedBirthOrder, p.BranchId)).ToListAsync(cancellationToken);
        var relationships = await _context.PersonRelationships.AsNoTracking().Where(r => r.GenealogyId == genealogyId).OrderBy(r => r.Id)
            .Select(r => new GenealogyRelationshipData(r.Id, r.PersonId, r.RelatedPersonId, r.Kind,
                r.IsPreferredForLineage, r.Note, r.SourceCitation)).ToListAsync(cancellationToken);
        var unions = await _context.FamilyUnions.AsNoTracking().Where(u => u.GenealogyId == genealogyId).OrderBy(u => u.Id)
            .Select(u => new GenealogyUnionData(u.Id, u.PartnerOneId, u.PartnerTwoId, u.Kind,
                u.StartDateText, u.EndDateText, u.Note)).ToListAsync(cancellationToken);
        var branches = await _context.FamilyBranches.AsNoTracking().Where(b => b.GenealogyId == genealogyId).OrderBy(b => b.Id)
            .Select(b => new GenealogyBranchData(b.Id, b.ParentBranchId, b.Name, b.SortOrder)).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new GenealogyGraphSnapshot(genealogy, people.AsReadOnly(), relationships.AsReadOnly(), unions.AsReadOnly(), branches.AsReadOnly());
    }

    public async Task<Genealogy> CreateGenealogyAsync(Genealogy genealogy, CancellationToken cancellationToken = default)
    {
        _context.Genealogies.Add(genealogy);
        await _context.SaveChangesAsync(cancellationToken);
        return genealogy;
    }

    public Task<List<Person>> GetPeopleAsync(Guid genealogyId, CancellationToken cancellationToken = default) =>
        _context.People.Where(p => p.GenealogyId == genealogyId)
            .OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public Task<List<Person>> GetBiologicalChildrenAsync(Guid genealogyId, Guid parentId, CancellationToken cancellationToken = default) =>
        (from relationship in _context.PersonRelationships.AsNoTracking()
         join person in _context.People.AsNoTracking() on relationship.PersonId equals person.Id
         where relationship.GenealogyId == genealogyId && person.GenealogyId == genealogyId &&
               relationship.RelatedPersonId == parentId && relationship.Kind == PersonRelationshipKind.BiologicalParent
         orderby person.Name, person.Id
         select person).ToListAsync(cancellationToken);

    public async Task RemoveBiologicalParentRelationshipAsync(Guid genealogyId, Guid parentId, Guid childId, CancellationToken cancellationToken = default)
    {
        var count = await _context.PersonRelationships
            .Where(r => r.GenealogyId == genealogyId && r.PersonId == childId &&
                        r.RelatedPersonId == parentId && r.Kind == PersonRelationshipKind.BiologicalParent)
            .ExecuteDeleteAsync(cancellationToken);
        if (count == 0) throw new InvalidOperationException("当前族谱中不存在指定的生物父母关系。");
    }

    public async Task<Person> AddPersonAsync(Person person, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);
        var name = person.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
            throw new InvalidOperationException("人物姓名须为 1 至 200 个字符。");
        var exists = await _context.Genealogies.AnyAsync(g => g.Id == person.GenealogyId, cancellationToken);
        if (!exists) throw new InvalidOperationException("目标族谱不存在。");
        person.Name = name;
        _context.People.Add(person);
        await _context.SaveChangesAsync(cancellationToken);
        return person;
    }

    public async Task<Person> AddBiologicalChildAsync(Guid genealogyId, Guid parentId, string? name, CancellationToken cancellationToken = default)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 200)
            throw new InvalidOperationException("子女姓名须为 1 至 200 个字符。");

        var child = new Person { GenealogyId = genealogyId, Name = normalized };
        var relationship = new PersonRelationship
        {
            GenealogyId = genealogyId,
            PersonId = child.Id,
            RelatedPersonId = parentId,
            Kind = PersonRelationshipKind.BiologicalParent
        };
        await AddPeopleAndRelationshipsAsync(genealogyId, [child], [relationship], cancellationToken);
        return child;
    }

    public async Task RenamePersonAsync(Guid genealogyId, Guid personId, string? name, CancellationToken cancellationToken = default)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 200)
            throw new InvalidOperationException("人物姓名须为 1 至 200 个字符。");
        var count = await _context.People
            .Where(person => person.GenealogyId == genealogyId && person.Id == personId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(person => person.Name, normalized), cancellationToken);
        if (count == 0) throw new InvalidOperationException("当前族谱中不存在该人物。");
    }

    public async Task UpdatePersonProfileAsync(Guid genealogyId, Guid personId, PersonSex sex,
        string? birthDate, string? deathDate, string? biography, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(sex)) throw new InvalidOperationException("性别选项无效。");
        if (birthDate?.Length > 100 || deathDate?.Length > 100)
            throw new InvalidOperationException("生卒日期文字不能超过 100 个字符。");
        if (biography?.Length > 4000) throw new InvalidOperationException("生平不能超过 4000 个字符。");
        var count = await _context.People.Where(p => p.GenealogyId == genealogyId && p.Id == personId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Sex, sex)
                .SetProperty(p => p.BirthDateText, birthDate)
                .SetProperty(p => p.DeathDateText, deathDate)
                .SetProperty(p => p.Biography, biography), cancellationToken);
        if (count == 0) throw new InvalidOperationException("当前族谱中不存在该人物。");
    }

    public async Task UpdatePersonBasicProfileAsync(Guid genealogyId, Guid personId, string? name, PersonSex sex,
        string? birthDate, string? deathDate, string? biography, CancellationToken cancellationToken = default)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 200)
            throw new InvalidOperationException("人物姓名须为 1 至 200 个字符。");
        if (!Enum.IsDefined(sex)) throw new InvalidOperationException("性别选项无效。");
        if (birthDate?.Length > 100 || deathDate?.Length > 100)
            throw new InvalidOperationException("生卒日期文字不能超过 100 个字符。");
        if (biography?.Length > 4000) throw new InvalidOperationException("生平不能超过 4000 个字符。");
        // 单条 UPDATE 作为原子写入，同时限制族谱与人物 ID。
        var count = await _context.People.Where(p => p.GenealogyId == genealogyId && p.Id == personId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Name, normalized)
                .SetProperty(p => p.Sex, sex).SetProperty(p => p.BirthDateText, birthDate)
                .SetProperty(p => p.DeathDateText, deathDate).SetProperty(p => p.Biography, biography), cancellationToken);
        if (count == 0) throw new InvalidOperationException("当前族谱中不存在该人物。");
    }

    /// <summary>仅删除当前族谱中没有任何关系或婚配记录的人物。</summary>
    public async Task DeleteUnrelatedPersonAsync(Guid genealogyId, Guid personId, CancellationToken cancellationToken = default)
    {
        var count = await _context.People
            .Where(p => p.GenealogyId == genealogyId && p.Id == personId)
            .Where(p => !_context.PersonRelationships.Any(r => r.GenealogyId == genealogyId &&
                (r.PersonId == p.Id || r.RelatedPersonId == p.Id)))
            .Where(p => !_context.FamilyUnions.Any(u => u.GenealogyId == genealogyId &&
                (u.PartnerOneId == p.Id || u.PartnerTwoId == p.Id)))
            .ExecuteDeleteAsync(cancellationToken);
        if (count == 0)
            throw new InvalidOperationException("人物不存在、已不属于当前族谱，或仍有关联关系，无法删除。");
    }

    public async Task<PersonRelationship> AddRelationshipAsync(PersonRelationship relationship, CancellationToken cancellationToken = default)
    {
        await new PersonRelationshipValidator(_context).ValidateForCreateAsync(relationship, cancellationToken);
        _context.PersonRelationships.Add(relationship);
        await _context.SaveChangesAsync(cancellationToken);
        return relationship;
    }

    public Task<PersonRelationship> AddExistingBiologicalChildAsync(Guid genealogyId, Guid parentId, Guid childId, CancellationToken cancellationToken = default) =>
        AddRelationshipAsync(new PersonRelationship
        {
            GenealogyId = genealogyId,
            PersonId = childId,
            RelatedPersonId = parentId,
            Kind = PersonRelationshipKind.BiologicalParent
        }, cancellationToken);

    /// <summary>
    /// Atomically persists a same-genealogy person batch and its relationships.
    /// A failed relationship validation rolls back the people staged by this operation.
    /// </summary>
    public async Task AddPeopleAndRelationshipsAsync(
        Guid genealogyId,
        IReadOnlyCollection<Person> people,
        IReadOnlyCollection<PersonRelationship> relationships,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(relationships);

        if (!await _context.Genealogies.AnyAsync(g => g.Id == genealogyId, cancellationToken))
            throw new InvalidOperationException("目标族谱不存在。");
        if (people.Any(person => person.GenealogyId != genealogyId))
            throw new InvalidOperationException("人物必须属于指定族谱。");
        if (relationships.Any(relationship => relationship.GenealogyId != genealogyId))
            throw new InvalidOperationException("关系必须属于指定族谱。");

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _context.People.AddRange(people);
            await _context.SaveChangesAsync(cancellationToken);

            var validator = new PersonRelationshipValidator(_context);
            foreach (var relationship in relationships)
            {
                await validator.ValidateForCreateAsync(relationship, cancellationToken);
                _context.PersonRelationships.Add(relationship);
                await _context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            foreach (var relationship in relationships)
                if (_context.Entry(relationship).State != EntityState.Detached)
                    _context.Entry(relationship).State = EntityState.Detached;
            foreach (var person in people)
                if (_context.Entry(person).State != EntityState.Detached)
                    _context.Entry(person).State = EntityState.Detached;
            throw;
        }
    }
}
