using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public sealed record WorkbenchProfileValues(string Name, PersonSex Sex, string? BirthDate, string? DeathDate, string? Biography, string? RecordedGeneration = null);
public sealed record WorkbenchParentInfo(Guid PersonId, string Name, PersonSex Sex, PersonRelationshipKind Kind);
public sealed record WorkbenchProfileSnapshot(Guid GenealogyId, Guid PersonId, long Generation, WorkbenchProfileValues Values,
    bool IsNew = false, Guid? ParentId = null, PersonRelationshipKind? ParentKind = null,
    Guid? PartnerId = null, FamilyUnionKind? UnionKind = null, Guid? SiblingId = null,
    Guid? SecondParentId = null, bool IsOrdinaryChild = false, PersonSex? ParentSex = null,
    IReadOnlyList<WorkbenchParentInfo>? Parents = null, bool IsOrdinarySibling = false);

/// <summary>Single-person command boundary. No UI or view state is used as data authority.</summary>
public sealed class WorkbenchPersonProfileCommand(ZupuDbContext context)
{
    public async Task<WorkbenchProfileSnapshot> BeginOrdinarySiblingAsync(Guid genealogyId, Guid siblingId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        var sibling = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == siblingId, cancellationToken)
            ?? throw new InvalidOperationException("所选兄妹不属于当前族谱。");
        var parents = await ReadOrdinarySiblingParentsAsync(genealogyId, siblingId, cancellationToken);
        var recorded = RecordedGenerationParser.TryParse(sibling.RecordedGeneration, out var number) ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        await transaction.CommitAsync(cancellationToken);
        return new(genealogyId, Guid.NewGuid(), generation, new("", PersonSex.Male, null, null, null, recorded), IsNew: true,
            ParentId: parents.FirstOrDefault()?.PersonId, ParentKind: parents.Count > 0 ? PersonRelationshipKind.BiologicalParent : null,
            SiblingId: siblingId, SecondParentId: parents.Skip(1).FirstOrDefault()?.PersonId, Parents: parents, IsOrdinarySibling: true);
    }

    private async Task<IReadOnlyList<WorkbenchParentInfo>> ReadOrdinarySiblingParentsAsync(Guid genealogyId, Guid siblingId, CancellationToken cancellationToken)
    {
        var relationships = await context.PersonRelationships.AsNoTracking().Where(r => r.GenealogyId == genealogyId && r.PersonId == siblingId && r.Kind == PersonRelationshipKind.BiologicalParent).ToListAsync(cancellationToken);
        var parents = new List<WorkbenchParentInfo>();
        foreach (var relationship in relationships)
        {
            var person = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == relationship.RelatedPersonId, cancellationToken)
                ?? throw new InvalidOperationException("生父母记录不完整或跨谱，请先核实。");
            if (person.Id == siblingId || person.Sex is not (PersonSex.Male or PersonSex.Female) || parents.Any(p => p.Sex == person.Sex))
                throw new InvalidOperationException("生父母角色不明确或存在重复候选，请先核实。");
            parents.Add(new(person.Id, person.Name, person.Sex, PersonRelationshipKind.BiologicalParent));
        }
        return parents.OrderBy(p => p.Sex).ToArray();
    }
    /// <summary>Reads the complete marriage set under the same version as the new-person draft.</summary>
    public async Task<WorkbenchProfileSnapshot> BeginOrdinaryChildAsync(Guid genealogyId, Guid parentId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        var parent = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == parentId, cancellationToken)
            ?? throw new InvalidOperationException("所选父母不属于当前族谱。");
        var second = await ResolveOrdinarySecondParentAsync(genealogyId, parent, cancellationToken);
        var recorded = RecordedGenerationParser.TryParse(parent.RecordedGeneration, out var number) && number < 1000
            ? (number + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        await transaction.CommitAsync(cancellationToken);
        return new(genealogyId, Guid.NewGuid(), generation, new("", PersonSex.Male, null, null, null, recorded),
            IsNew: true, ParentId: parentId, ParentKind: PersonRelationshipKind.BiologicalParent, SecondParentId: second, IsOrdinaryChild: true, ParentSex: parent.Sex);
    }

    private async Task<Guid?> ResolveOrdinarySecondParentAsync(Guid genealogyId, Person parent, CancellationToken cancellationToken)
    {
        if (parent.Sex is not (PersonSex.Male or PersonSex.Female))
            throw new InvalidOperationException("当前人物性别未明确，无法确定生父母角色。请先完善资料。");
        var parentId = parent.Id;
        var unions = await context.FamilyUnions.AsNoTracking().Where(u => u.GenealogyId == genealogyId && u.Kind == FamilyUnionKind.Marriage &&
            (u.PartnerOneId == parentId || u.PartnerTwoId == parentId)).ToListAsync(cancellationToken);
        // The view uses the same ordering; this is not a claim about historical marriage order.
        var ordered = unions.OrderBy(u => u.Id.ToString("D"), StringComparer.Ordinal).ToArray();
        if (ordered.Any(u => u.PartnerTwoId is null))
            throw new InvalidOperationException("存在对方未详的婚配，无法安全确定默认生父母，请先完善婚配资料。");
        Guid? second = null;
        if (ordered.Length > 0)
        {
            var union = ordered[0];
            second = union.PartnerOneId == parentId ? union.PartnerTwoId : union.PartnerOneId;
            var spouse = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == second, cancellationToken)
                ?? throw new InvalidOperationException("婚配对方不属于当前族谱或已不存在。");
            if (spouse.Id == parent.Id || spouse.Sex is not (PersonSex.Male or PersonSex.Female) || spouse.Sex == parent.Sex)
                throw new InvalidOperationException("默认配偶的生父母角色无法确定，请先核实性别与婚配资料。");
        }
        return second;
    }
    public async Task<WorkbenchProfileSnapshot> ReadAsync(Guid genealogyId, Guid personId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        var person = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == personId, cancellationToken)
            ?? throw new InvalidOperationException("当前族谱中不存在该人物。");
        var parents = await (from relationship in context.PersonRelationships.AsNoTracking()
            join related in context.People.AsNoTracking() on relationship.RelatedPersonId equals (Guid?)related.Id
            where relationship.GenealogyId == genealogyId && relationship.PersonId == personId && related.GenealogyId == genealogyId &&
                (relationship.Kind == PersonRelationshipKind.BiologicalParent || relationship.Kind == PersonRelationshipKind.AdoptiveParent || relationship.Kind == PersonRelationshipKind.HeirParent)
            orderby relationship.Id
            select new WorkbenchParentInfo(related.Id, related.Name, related.Sex, relationship.Kind)).ToListAsync(cancellationToken);
        var snapshot = new WorkbenchProfileSnapshot(genealogyId, personId, generation,
            new(person.Name, person.Sex, person.BirthDateText, person.DeathDateText, person.Biography, person.RecordedGeneration), Parents: parents.AsReadOnly());
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    public async Task<WorkbenchProfileSnapshot> BeginNewAsync(Guid genealogyId, Guid? parentId = null,
        PersonRelationshipKind? parentKind = null, Guid? partnerId = null, FamilyUnionKind? unionKind = null, Guid? siblingId = null, CancellationToken cancellationToken = default)
    {
        if ((parentId is null) != (parentKind is null) || (parentKind is not null && parentKind is not (PersonRelationshipKind.BiologicalParent or PersonRelationshipKind.AdoptiveParent or PersonRelationshipKind.HeirParent)))
            throw new InvalidOperationException("父母关系类型无效。");
        if ((partnerId is null) != (unionKind is null) || (unionKind is not null && !Enum.IsDefined(unionKind.Value)) || (partnerId is not null && (parentId is not null || siblingId is not null)))
            throw new InvalidOperationException("婚配关系类型无效。");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        if (!await context.Genealogies.AsNoTracking().AnyAsync(g => g.Id == genealogyId, cancellationToken))
            throw new InvalidOperationException("目标族谱不存在。");
        string? recorded = null;
        if (parentId is Guid id)
        {
            var parent = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("所选父母不属于当前族谱。");
            if (RecordedGenerationParser.TryParse(parent.RecordedGeneration, out var number) && number < 1000)
                recorded = (number + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var defaultSex = parentId is null && partnerId is null && siblingId is null ? PersonSex.Male : PersonSex.Unknown;
        if (partnerId is Guid partner)
        {
            var existingPartner = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == partner, cancellationToken)
                ?? throw new InvalidOperationException("所选配偶不属于当前族谱。");
            defaultSex = existingPartner.Sex switch { PersonSex.Male => PersonSex.Female, PersonSex.Female => PersonSex.Male, _ => PersonSex.Unknown };
        }
        if (siblingId is Guid sibling)
        {
            var existing = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == sibling, cancellationToken)
                ?? throw new InvalidOperationException("所选兄妹不属于当前族谱。");
            if (parentId is Guid sharedParent && !await context.PersonRelationships.AsNoTracking().AnyAsync(r => r.GenealogyId == genealogyId && r.PersonId == sibling && r.RelatedPersonId == sharedParent &&
                (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent), cancellationToken))
                throw new InvalidOperationException("所选共同父母关系已变化。");
            recorded = RecordedGenerationParser.TryParse(existing.RecordedGeneration, out var siblingGeneration) ? siblingGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        }
        await transaction.CommitAsync(cancellationToken);
        return new(genealogyId, Guid.NewGuid(), generation, new("", defaultSex, null, null, null, recorded), true, parentId, parentKind, partnerId, unionKind, siblingId);
    }

    public async Task SaveAsync(WorkbenchProfileSnapshot original, WorkbenchProfileValues values, CancellationToken cancellationToken = default)
    {
        var name = PersonProfileRules.Validate(values.Name, values.Sex, values.BirthDate, values.DeathDate, values.Biography);
        if (values.RecordedGeneration?.Length > 100) throw new InvalidOperationException("谱载世代不能超过 100 个字符。");
        if (((original.IsNew && original.PartnerId is null) || values.RecordedGeneration != original.Values.RecordedGeneration) &&
            !RecordedGenerationParser.TryParse(values.RecordedGeneration, out _))
            throw new InvalidOperationException("请选择 1 至 1000 的世代。");
        if (original.IsNew)
        {
            if (original.IsOrdinaryChild && (original.ParentId is null || original.ParentKind != PersonRelationshipKind.BiologicalParent ||
                original.SiblingId is not null || original.PartnerId is not null || original.SecondParentId == original.ParentId))
                throw new InvalidOperationException("普通子女草稿的父母上下文无效。");
            if (original.IsOrdinarySibling && (original.SiblingId is null || original.PartnerId is not null || original.IsOrdinaryChild ||
                (original.ParentId is null && original.SecondParentId is not null) || (original.ParentId is not null && original.ParentKind != PersonRelationshipKind.BiologicalParent)))
                throw new InvalidOperationException("普通兄妹草稿的父母上下文无效。");
            if (!original.IsOrdinaryChild && !original.IsOrdinarySibling && original.SecondParentId is not null)
                throw new InvalidOperationException("第二位父母只允许用于普通子女草稿。");
            if ((original.ParentId is null) != (original.ParentKind is null) || (original.ParentKind is not null && original.ParentKind is not (PersonRelationshipKind.BiologicalParent or PersonRelationshipKind.AdoptiveParent or PersonRelationshipKind.HeirParent)))
                throw new InvalidOperationException("父母关系类型无效。");
            if ((original.PartnerId is null) != (original.UnionKind is null) || (original.UnionKind is not null && !Enum.IsDefined(original.UnionKind.Value)) || (original.PartnerId is not null && (original.ParentId is not null || original.SiblingId is not null)))
                throw new InvalidOperationException("婚配关系类型无效。");
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            if (original.IsOrdinarySibling)
            {
                var parents = await ReadOrdinarySiblingParentsAsync(original.GenealogyId, original.SiblingId!.Value, cancellationToken);
                if (!parents.Select(p => p.PersonId).SequenceEqual((original.Parents ?? []).Select(p => p.PersonId)) ||
                    parents.FirstOrDefault()?.PersonId != original.ParentId || parents.Skip(1).FirstOrDefault()?.PersonId != original.SecondParentId)
                    throw new InvalidOperationException("生父母候选已变化，请重新开始新增兄妹。");
            }
            if (original.IsOrdinaryChild)
            {
                var parent = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == original.GenealogyId && p.Id == original.ParentId, cancellationToken)
                    ?? throw new InvalidOperationException("所选父母不属于当前族谱。");
                if (parent.Sex != original.ParentSex) throw new InvalidOperationException("当前父母角色已变化，请重新开始新增子女。");
                if (await ResolveOrdinarySecondParentAsync(original.GenealogyId, parent, cancellationToken) != original.SecondParentId)
                    throw new InvalidOperationException("婚配候选已变化，请重新开始新增子女。");
            }
            var sql = $"INSERT INTO People (Id, GenealogyId, Name, Sex, BirthDateText, DeathDateText, Biography, IsHistoricalRecord, RecordedGeneration) " +
                $"SELECT @id, @genealogy, @name, @sex, @birth, @death, @biography, 0, @recorded WHERE EXISTS (SELECT 1 FROM Genealogies WHERE Id=@genealogy) " +
                $"AND (@parent IS NULL OR EXISTS (SELECT 1 FROM People WHERE Id=@parent AND GenealogyId=@genealogy)) " +
                $"AND (@sibling IS NULL OR EXISTS (SELECT 1 FROM People WHERE Id=@sibling AND GenealogyId=@genealogy)) " +
                $"AND (@sibling IS NULL OR @parent IS NULL OR EXISTS (SELECT 1 FROM PersonRelationships WHERE GenealogyId=@genealogy AND PersonId=@sibling AND RelatedPersonId=@parent AND Kind IN (1,2,3))) " +
                $"AND EXISTS (SELECT 1 FROM ({UnlinkGenerationV1.GuardSql}) AS guard WHERE guard.Value=@generation)";
            var parameters = new object[] { new SqliteParameter("@id", original.PersonId.ToString().ToUpperInvariant()),
                new SqliteParameter("@genealogy", original.GenealogyId.ToString().ToUpperInvariant()), new SqliteParameter("@name", name),
                new SqliteParameter("@sex", (int)values.Sex), new SqliteParameter("@birth", (object?)values.BirthDate ?? DBNull.Value),
                new SqliteParameter("@death", (object?)values.DeathDate ?? DBNull.Value), new SqliteParameter("@biography", (object?)values.Biography ?? DBNull.Value),
                new SqliteParameter("@recorded", (object?)values.RecordedGeneration ?? DBNull.Value), new SqliteParameter("@generation", original.Generation),
                new SqliteParameter("@sibling", original.SiblingId is Guid sibling ? sibling.ToString().ToUpperInvariant() : DBNull.Value),
                new SqliteParameter("@parent", (original.ParentId ?? original.PartnerId) is Guid related ? related.ToString().ToUpperInvariant() : DBNull.Value) };
            var inserted = await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
            if (inserted != 1) throw new InvalidOperationException("族谱或数据版本已变化，请重新读取后确认。");
            if (original.ParentId is Guid parentId)
            {
                var relationship = new PersonRelationship { GenealogyId = original.GenealogyId, PersonId = original.PersonId,
                    RelatedPersonId = parentId, Kind = original.ParentKind!.Value };
                if (original.IsOrdinarySibling) await new PersonRelationshipValidator(context).ValidateForCreateAsync(relationship, cancellationToken);
                try { context.PersonRelationships.Add(relationship); await context.SaveChangesAsync(cancellationToken); }
                catch { context.Entry(relationship).State = EntityState.Detached; throw; }
            }
            if (original.SecondParentId is Guid secondParent)
            {
                var relationship = new PersonRelationship { GenealogyId = original.GenealogyId, PersonId = original.PersonId,
                    RelatedPersonId = secondParent, Kind = PersonRelationshipKind.BiologicalParent };
                await new PersonRelationshipValidator(context).ValidateForCreateAsync(relationship, cancellationToken);
                try { context.PersonRelationships.Add(relationship); await context.SaveChangesAsync(cancellationToken); }
                catch { context.Entry(relationship).State = EntityState.Detached; throw; }
            }
            if (original.SiblingId is Guid siblingId)
            {
                var relationship = new PersonRelationship { GenealogyId = original.GenealogyId, PersonId = original.PersonId,
                    RelatedPersonId = siblingId, Kind = PersonRelationshipKind.Sibling };
                try { context.PersonRelationships.Add(relationship); await context.SaveChangesAsync(cancellationToken); }
                catch { context.Entry(relationship).State = EntityState.Detached; throw; }
            }
            if (original.PartnerId is Guid partnerId)
            {
                var union = new FamilyUnion { GenealogyId = original.GenealogyId, PartnerOneId = partnerId,
                    PartnerTwoId = original.PersonId, Kind = original.UnionKind!.Value };
                await new FamilyUnionValidator(context).ValidateForCreateAsync(union, cancellationToken);
                try { context.FamilyUnions.Add(union); await context.SaveChangesAsync(cancellationToken); }
                catch { context.Entry(union).State = EntityState.Detached; throw; }
            }
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        // One atomic statement scopes identity and tests the protected generation at the write itself.
        var count = await context.People.Where(p => p.GenealogyId == original.GenealogyId && p.Id == original.PersonId)
            .Where(p => context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).Any(v => v == original.Generation))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, name).SetProperty(p => p.Sex, values.Sex)
                .SetProperty(p => p.BirthDateText, values.BirthDate).SetProperty(p => p.DeathDateText, values.DeathDate)
                .SetProperty(p => p.Biography, values.Biography).SetProperty(p => p.RecordedGeneration, values.RecordedGeneration), cancellationToken);
        if (count != 1) throw new InvalidOperationException("人物或数据版本已变化，请重新读取后确认。");
    }
}

/// <summary>Holds one draft; a successful write is never retried with the old token.</summary>
public sealed class WorkbenchPersonDraft(WorkbenchProfileSnapshot original)
{
    private readonly object _gate = new();
    private WorkbenchProfileValues _values = original.Values;
    private bool _saving;
    private bool _isSaved;
    public WorkbenchProfileSnapshot Original { get; } = original;
    public WorkbenchProfileValues Values
    {
        get { lock (_gate) return _values; }
        set { lock (_gate) { if (_saving || _isSaved) throw new InvalidOperationException("保存中或已保存的草稿不能修改。"); _values = value; } }
    }
    public bool IsDirty { get { lock (_gate) return !_isSaved && (Original.IsNew || _values != Original.Values); } }
    public bool IsSaved { get { lock (_gate) return _isSaved; } }

    public async Task SaveAsync(WorkbenchPersonProfileCommand command, CancellationToken cancellationToken = default)
    {
        WorkbenchProfileValues values;
        lock (_gate)
        {
            if (_isSaved || _saving) throw new InvalidOperationException("草稿正在保存或已保存，请重新读取人物。");
            _saving = true;
            values = _values;
        }
        try
        {
            await command.SaveAsync(Original, values, cancellationToken);
            lock (_gate) _isSaved = true;
        }
        finally { lock (_gate) _saving = false; }
    }
}
