using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

/// <summary>A scoped unlink found no matching current record.</summary>
public sealed class StaleTargetLinkException(string message) : InvalidOperationException(message);

/// <summary>目标模型的最小应用服务：所有读取均显式按族谱范围过滤。</summary>
public sealed class GenealogyDataService
{
    private readonly ZupuDbContext _context;

    public GenealogyDataService(ZupuDbContext context) => _context = context;

    public Task<List<Genealogy>> GetGenealogiesAsync(CancellationToken cancellationToken = default) =>
        _context.Genealogies.AsNoTracking().OrderBy(g => g.Name).ToListAsync(cancellationToken);

    /// <summary>Reads immutable import provenance for one genealogy without exposing other genealogies' history.</summary>
    public async Task<List<GenealogyImportRecord>> GetImportRecordsAsync(Guid genealogyId, CancellationToken cancellationToken = default)
    {
        var records = await _context.GenealogyImportRecords.AsNoTracking()
            .Where(record => record.GenealogyId == genealogyId).ToListAsync(cancellationToken);
        // SQLite cannot translate DateTimeOffset ordering; preserve chronological API semantics after the scoped query.
        return records.OrderBy(record => record.ImportedAt).ThenBy(record => record.Id).ToList();
    }

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

    /// <summary>
    /// Deletes exactly one relationship only when it still belongs to the specified genealogy and includes the
    /// currently selected participant and all fields still match the confirmed snapshot. This is one scoped SQL
    /// DELETE, so a failed/stale request changes nothing.
    /// </summary>
    public async Task RemoveRelationshipForParticipantAsync(Guid genealogyId, GenealogyRelationshipData expected, Guid participantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var count = await _context.PersonRelationships
            .Where(relationship => relationship.GenealogyId == genealogyId && relationship.Id == expected.Id &&
                (relationship.PersonId == participantId || relationship.RelatedPersonId == participantId) &&
                relationship.PersonId == expected.PersonId && relationship.RelatedPersonId == expected.RelatedPersonId &&
                relationship.Kind == expected.Kind && relationship.IsPreferredForLineage == expected.IsPreferredForLineage &&
                relationship.Note == expected.Note && relationship.SourceCitation == expected.SourceCitation)
            .ExecuteDeleteAsync(cancellationToken);
        if (count == 0)
            throw new StaleTargetLinkException("当前族谱中不存在指定人物参与的关系，可能已被更改或解除。");
    }

    /// <summary>
    /// Deletes exactly one union only when it still belongs to the specified genealogy and includes the current
    /// participant and all fields still match the confirmed snapshot. It never deletes either person or a related
    /// relationship record.
    /// </summary>
    public async Task RemoveFamilyUnionForParticipantAsync(Guid genealogyId, GenealogyUnionData expected, Guid participantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var count = await _context.FamilyUnions
            .Where(union => union.GenealogyId == genealogyId && union.Id == expected.Id &&
                (union.PartnerOneId == participantId || union.PartnerTwoId == participantId) &&
                union.PartnerOneId == expected.PartnerOneId && union.PartnerTwoId == expected.PartnerTwoId &&
                union.Kind == expected.Kind && union.StartDateText == expected.StartDateText &&
                union.EndDateText == expected.EndDateText && union.Note == expected.Note)
            .ExecuteDeleteAsync(cancellationToken);
        if (count == 0)
            throw new StaleTargetLinkException("当前族谱中不存在指定人物参与的婚配，可能已被更改或解除。");
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

    public async Task<FamilyUnion> AddFamilyUnionAsync(FamilyUnion union, CancellationToken cancellationToken = default)
    {
        await new FamilyUnionValidator(_context).ValidateForCreateAsync(union, cancellationToken);
        _context.FamilyUnions.Add(union);
        await _context.SaveChangesAsync(cancellationToken);
        return union;
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
        => await AddGraphRecordsAsync(genealogyId, [], people, relationships, [], cancellationToken);

    /// <summary>
    /// Atomically persists same-genealogy branches, people, relationships and unions.
    /// It is deliberately a graph-record primitive only; it never replaces or merges an existing genealogy.
    /// </summary>
    public async Task AddGraphRecordsAsync(
        Guid genealogyId,
        IReadOnlyCollection<FamilyBranch> branches,
        IReadOnlyCollection<Person> people,
        IReadOnlyCollection<PersonRelationship> relationships,
        IReadOnlyCollection<FamilyUnion> unions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(relationships);
        ArgumentNullException.ThrowIfNull(unions);

        await ValidateGraphRecordShapeAsync(genealogyId, branches, people, relationships, unions, cancellationToken);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await PersistGraphRecordsAsync(branches, people, relationships, unions, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            DetachGraphEntries(branches, people, relationships, unions);
            throw;
        }
    }

    /// <summary>
    /// Atomically creates a previously absent genealogy and all of its graph records.
    /// It is intentionally an insert-only primitive: callers must preflight and reject any existing source IDs.
    /// </summary>
    public async Task AddGenealogyAndGraphRecordsAsync(
        Genealogy genealogy,
        GenealogyImportRecord importRecord,
        IReadOnlyCollection<FamilyBranch> branches,
        IReadOnlyCollection<Person> people,
        IReadOnlyCollection<PersonRelationship> relationships,
        IReadOnlyCollection<FamilyUnion> unions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(genealogy);
        ArgumentNullException.ThrowIfNull(importRecord);
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(relationships);
        ArgumentNullException.ThrowIfNull(unions);

        await ValidateNewGenealogyAsync(genealogy, cancellationToken);
        ValidateImportRecord(genealogy.Id, importRecord);
        await ValidateGraphRecordShapeAsync(genealogy.Id, branches, people, relationships, unions, cancellationToken,
            requireExistingGenealogy: false);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _context.Genealogies.Add(genealogy);
            await _context.SaveChangesAsync(cancellationToken);
            _context.GenealogyImportRecords.Add(importRecord);
            await _context.SaveChangesAsync(cancellationToken);
            await PersistGraphRecordsAsync(branches, people, relationships, unions, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (_context.Entry(genealogy).State != EntityState.Detached)
                _context.Entry(genealogy).State = EntityState.Detached;
            if (_context.Entry(importRecord).State != EntityState.Detached)
                _context.Entry(importRecord).State = EntityState.Detached;
            DetachGraphEntries(branches, people, relationships, unions);
            throw;
        }
    }

    private async Task ValidateGraphRecordShapeAsync(
        Guid genealogyId,
        IReadOnlyCollection<FamilyBranch> branches,
        IReadOnlyCollection<Person> people,
        IReadOnlyCollection<PersonRelationship> relationships,
        IReadOnlyCollection<FamilyUnion> unions,
        CancellationToken cancellationToken,
        bool requireExistingGenealogy = true)
    {
        if (genealogyId == Guid.Empty || (requireExistingGenealogy &&
            !await _context.Genealogies.AnyAsync(genealogy => genealogy.Id == genealogyId, cancellationToken)))
            throw new InvalidOperationException("目标族谱不存在。");
        if (branches.Any(branch => branch.Id == Guid.Empty || branch.GenealogyId != genealogyId) ||
            people.Any(person => person.Id == Guid.Empty || person.GenealogyId != genealogyId) ||
            relationships.Any(relationship => relationship.Id == Guid.Empty || relationship.GenealogyId != genealogyId) ||
            unions.Any(union => union.Id == Guid.Empty || union.GenealogyId != genealogyId))
            throw new InvalidOperationException("图数据必须属于指定族谱且具有有效 ID。");
        EnsureDistinctIds(branches.Select(branch => branch.Id), "房支");
        EnsureDistinctIds(people.Select(person => person.Id), "人物");
        EnsureDistinctIds(relationships.Select(relationship => relationship.Id), "人物关系");
        EnsureDistinctIds(unions.Select(union => union.Id), "婚配关系");
        if (await _context.FamilyBranches.AnyAsync(branch => branches.Select(item => item.Id).Contains(branch.Id), cancellationToken) ||
            await _context.People.AnyAsync(person => people.Select(item => item.Id).Contains(person.Id), cancellationToken) ||
            await _context.PersonRelationships.AnyAsync(relationship => relationships.Select(item => item.Id).Contains(relationship.Id), cancellationToken) ||
            await _context.FamilyUnions.AnyAsync(union => unions.Select(item => item.Id).Contains(union.Id), cancellationToken))
            throw new InvalidOperationException("图数据 ID 已存在，拒绝覆盖已有记录。");

        var branchById = branches.ToDictionary(branch => branch.Id);
        if (branches.Any(branch => string.IsNullOrWhiteSpace(branch.Name) || branch.Name.Trim().Length > 200 ||
                                   branch.ParentBranchId == branch.Id))
            throw new InvalidOperationException("房支资料或父级引用无效。");
        foreach (var branch in branches) branch.Name = branch.Name.Trim();
        if (branches.GroupBy(branch => (branch.ParentBranchId, branch.Name)).Any(group => group.Count() > 1))
            throw new InvalidOperationException("同一父级下不能包含重复房支名称。");
        EnsureBranchBatchAcyclic(branchById);

        var externalParentIds = branches.Where(branch => branch.ParentBranchId is Guid parent && !branchById.ContainsKey(parent))
            .Select(branch => branch.ParentBranchId!.Value).Distinct().ToList();
        if (externalParentIds.Count > 0)
        {
            var existing = await _context.FamilyBranches.Where(branch => branch.GenealogyId == genealogyId && externalParentIds.Contains(branch.Id))
                .Select(branch => branch.Id).ToListAsync(cancellationToken);
            if (existing.Count != externalParentIds.Count) throw new InvalidOperationException("房支父级必须属于指定族谱。");
        }

        if (people.Any(person => string.IsNullOrWhiteSpace(person.Name) || person.Name.Trim().Length > 200 || !Enum.IsDefined(person.Sex) ||
                                 person.BirthDateText?.Length > 100 || person.DeathDateText?.Length > 100 || person.Biography?.Length > 4000 ||
                                 person.PhotoPath?.Length > 500 || person.RecordedGeneration?.Length > 100 || person.RecordedBirthOrder?.Length > 100))
            throw new InvalidOperationException("人物资料无效。");
        foreach (var person in people) person.Name = person.Name.Trim();
        var externalBranchIds = people.Where(person => person.BranchId is Guid branch && !branchById.ContainsKey(branch))
            .Select(person => person.BranchId!.Value).Distinct().ToList();
        if (externalBranchIds.Count > 0)
        {
            var existing = await _context.FamilyBranches.Where(branch => branch.GenealogyId == genealogyId && externalBranchIds.Contains(branch.Id))
                .Select(branch => branch.Id).ToListAsync(cancellationToken);
            if (existing.Count != externalBranchIds.Count) throw new InvalidOperationException("人物房支必须属于指定族谱。");
        }
    }

    private async Task ValidateNewGenealogyAsync(Genealogy genealogy, CancellationToken cancellationToken)
    {
        var normalizedName = genealogy.Name?.Trim();
        if (genealogy.Id == Guid.Empty || string.IsNullOrEmpty(normalizedName) || normalizedName.Length > 200 ||
            genealogy.Surname?.Length > 100 || genealogy.Description?.Length > 2000)
            throw new InvalidOperationException("族谱资料无效。");
        if (await _context.Genealogies.AnyAsync(item => item.Id == genealogy.Id, cancellationToken))
            throw new InvalidOperationException("族谱 ID 已存在，拒绝覆盖已有族谱。");
        genealogy.Name = normalizedName;
    }

    private static void ValidateImportRecord(Guid genealogyId, GenealogyImportRecord importRecord)
    {
        if (importRecord.Id == Guid.Empty || importRecord.GenealogyId != genealogyId ||
            string.IsNullOrWhiteSpace(importRecord.SourceKind) || importRecord.SourceKind.Length > 64 ||
            (importRecord.ImportMode != "Default" && importRecord.ImportMode != "Copy") ||
            importRecord.SourceFormatVersion <= 0 || importRecord.SourceGenealogyId == Guid.Empty ||
            importRecord.SourceDocumentFingerprint.Length != 64 ||
            !importRecord.SourceDocumentFingerprint.All(Uri.IsHexDigit))
            throw new InvalidOperationException("导入来源记录无效。");
    }

    private async Task PersistGraphRecordsAsync(
        IReadOnlyCollection<FamilyBranch> branches,
        IReadOnlyCollection<Person> people,
        IReadOnlyCollection<PersonRelationship> relationships,
        IReadOnlyCollection<FamilyUnion> unions,
        CancellationToken cancellationToken)
    {
        _context.FamilyBranches.AddRange(branches);
        await _context.SaveChangesAsync(cancellationToken);
        _context.People.AddRange(people);
        await _context.SaveChangesAsync(cancellationToken);

        var validator = new PersonRelationshipValidator(_context);
        foreach (var relationship in relationships)
        {
            await validator.ValidateForCreateAsync(relationship, cancellationToken);
            _context.PersonRelationships.Add(relationship);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var unionValidator = new FamilyUnionValidator(_context);
        foreach (var union in unions)
        {
            await unionValidator.ValidateForCreateAsync(union, cancellationToken);
            _context.FamilyUnions.Add(union);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private void DetachGraphEntries(
        IEnumerable<FamilyBranch> branches,
        IEnumerable<Person> people,
        IEnumerable<PersonRelationship> relationships,
        IEnumerable<FamilyUnion> unions)
    {
        foreach (var relationship in relationships)
            if (_context.Entry(relationship).State != EntityState.Detached)
                _context.Entry(relationship).State = EntityState.Detached;
        foreach (var person in people)
            if (_context.Entry(person).State != EntityState.Detached)
                _context.Entry(person).State = EntityState.Detached;
        foreach (var branch in branches)
            if (_context.Entry(branch).State != EntityState.Detached)
                _context.Entry(branch).State = EntityState.Detached;
        foreach (var union in unions)
            if (_context.Entry(union).State != EntityState.Detached)
                _context.Entry(union).State = EntityState.Detached;
    }

    private static void EnsureDistinctIds(IEnumerable<Guid> ids, string label)
    {
        var values = ids.ToList();
        if (values.Count != values.Distinct().Count()) throw new InvalidOperationException($"{label} ID 不能重复。");
    }

    private static void EnsureBranchBatchAcyclic(IReadOnlyDictionary<Guid, FamilyBranch> branches)
    {
        foreach (var branch in branches.Values)
        {
            var visited = new HashSet<Guid>();
            var current = branch;
            while (current.ParentBranchId is Guid parent && branches.TryGetValue(parent, out var next))
            {
                if (!visited.Add(current.Id)) throw new InvalidOperationException("房支不能形成循环引用。");
                current = next;
            }
        }
    }
}
