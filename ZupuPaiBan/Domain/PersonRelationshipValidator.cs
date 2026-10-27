using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Domain;

/// <summary>在写入前校验跨谱、重复、自关联及父系循环。</summary>
public sealed class PersonRelationshipValidator
{
    private static readonly PersonRelationshipKind[] ParentKinds =
    [
        PersonRelationshipKind.BiologicalParent,
        PersonRelationshipKind.AdoptiveParent,
        PersonRelationshipKind.HeirParent
    ];

    private readonly ZupuDbContext _context;

    public PersonRelationshipValidator(ZupuDbContext context) => _context = context;

    public async Task ValidateForCreateAsync(PersonRelationship relationship, CancellationToken cancellationToken = default)
    {
        if (relationship.PersonId == Guid.Empty)
            throw new InvalidOperationException("关系主体不能为空。");

        var subject = await _context.People
            .SingleOrDefaultAsync(p => p.Id == relationship.PersonId, cancellationToken)
            ?? throw new InvalidOperationException("关系主体不存在。");

        if (subject.GenealogyId != relationship.GenealogyId)
            throw new InvalidOperationException("关系主体必须属于指定族谱。");

        var isDuplicate = await _context.PersonRelationships.AnyAsync(r =>
            r.GenealogyId == relationship.GenealogyId &&
            r.PersonId == relationship.PersonId &&
            r.RelatedPersonId == relationship.RelatedPersonId &&
            r.Kind == relationship.Kind, cancellationToken);
        if (isDuplicate)
            throw new InvalidOperationException("相同关系已经存在。");

        if (relationship.Kind == PersonRelationshipKind.LostAncestry)
        {
            if (relationship.RelatedPersonId != null)
                throw new InvalidOperationException("失考关系不能指定对方人物。");
            return;
        }

        if (relationship.RelatedPersonId is not Guid relatedPersonId)
            throw new InvalidOperationException("该关系必须指定对方人物。");
        if (relatedPersonId == relationship.PersonId)
            throw new InvalidOperationException("人物不能与自身建立关系。");

        var related = await _context.People
            .SingleOrDefaultAsync(p => p.Id == relatedPersonId, cancellationToken)
            ?? throw new InvalidOperationException("关系对方不存在。");
        if (related.GenealogyId != relationship.GenealogyId)
            throw new InvalidOperationException("关系不能跨族谱。");

        if (ParentKinds.Contains(relationship.Kind) &&
            await HasAncestorAsync(relatedPersonId, relationship.PersonId, relationship.GenealogyId, cancellationToken))
        {
            throw new InvalidOperationException("父母关系会形成循环谱系。");
        }
    }

    private async Task<bool> HasAncestorAsync(Guid startPersonId, Guid soughtPersonId, Guid genealogyId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid>();
        var frontier = new Queue<Guid>();
        frontier.Enqueue(startPersonId);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            if (!visited.Add(current)) continue;
            if (current == soughtPersonId) return true;

            var parents = await _context.PersonRelationships
                .Where(r => r.GenealogyId == genealogyId &&
                            r.PersonId == current &&
                            ParentKinds.Contains(r.Kind) &&
                            r.RelatedPersonId != null)
                .Select(r => r.RelatedPersonId!.Value)
                .ToListAsync(cancellationToken);
            foreach (var parentId in parents) frontier.Enqueue(parentId);
        }

        return false;
    }
}
