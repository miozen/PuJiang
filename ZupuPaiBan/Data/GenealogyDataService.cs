using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

/// <summary>目标模型的最小应用服务：所有读取均显式按族谱范围过滤。</summary>
public sealed class GenealogyDataService
{
    private readonly ZupuDbContext _context;

    public GenealogyDataService(ZupuDbContext context) => _context = context;

    public async Task<Genealogy> CreateGenealogyAsync(Genealogy genealogy, CancellationToken cancellationToken = default)
    {
        _context.Genealogies.Add(genealogy);
        await _context.SaveChangesAsync(cancellationToken);
        return genealogy;
    }

    public Task<List<Person>> GetPeopleAsync(Guid genealogyId, CancellationToken cancellationToken = default) =>
        _context.People.Where(p => p.GenealogyId == genealogyId)
            .OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<Person> AddPersonAsync(Person person, CancellationToken cancellationToken = default)
    {
        var exists = await _context.Genealogies.AnyAsync(g => g.Id == person.GenealogyId, cancellationToken);
        if (!exists) throw new InvalidOperationException("目标族谱不存在。");
        _context.People.Add(person);
        await _context.SaveChangesAsync(cancellationToken);
        return person;
    }

    public async Task<PersonRelationship> AddRelationshipAsync(PersonRelationship relationship, CancellationToken cancellationToken = default)
    {
        await new PersonRelationshipValidator(_context).ValidateForCreateAsync(relationship, cancellationToken);
        _context.PersonRelationships.Add(relationship);
        await _context.SaveChangesAsync(cancellationToken);
        return relationship;
    }

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
                await validator.ValidateForCreateAsync(relationship, cancellationToken);

            _context.PersonRelationships.AddRange(relationships);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
