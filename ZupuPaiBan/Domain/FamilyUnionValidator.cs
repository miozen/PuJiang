using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Domain;

/// <summary>在写入前校验婚配记录的族谱范围、人物引用和重复项。</summary>
public sealed class FamilyUnionValidator
{
    private readonly ZupuDbContext _context;

    public FamilyUnionValidator(ZupuDbContext context) => _context = context;

    public async Task ValidateForCreateAsync(FamilyUnion union, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(union);
        if (union.Id == Guid.Empty || union.PartnerOneId == Guid.Empty)
            throw new InvalidOperationException("婚配记录及其第一方人物不能为空。");
        if (!Enum.IsDefined(union.Kind)) throw new InvalidOperationException("婚配类型无效。");
        if (union.StartDateText?.Length > 100 || union.EndDateText?.Length > 100 || union.Note?.Length > 2000)
            throw new InvalidOperationException("婚配资料超过允许长度。");

        var first = await _context.People.SingleOrDefaultAsync(person => person.Id == union.PartnerOneId, cancellationToken)
            ?? throw new InvalidOperationException("婚配第一方人物不存在。");
        if (first.GenealogyId != union.GenealogyId)
            throw new InvalidOperationException("婚配人物必须属于指定族谱。");

        if (union.PartnerTwoId is Guid secondId)
        {
            if (secondId == union.PartnerOneId) throw new InvalidOperationException("人物不能与自身建立婚配关系。");
            var second = await _context.People.SingleOrDefaultAsync(person => person.Id == secondId, cancellationToken)
                ?? throw new InvalidOperationException("婚配第二方人物不存在。");
            if (second.GenealogyId != union.GenealogyId)
                throw new InvalidOperationException("婚配关系不能跨族谱。");
        }

        var duplicate = await _context.FamilyUnions.AnyAsync(item =>
            item.GenealogyId == union.GenealogyId && item.PartnerOneId == union.PartnerOneId &&
            item.PartnerTwoId == union.PartnerTwoId && item.Kind == union.Kind, cancellationToken);
        if (duplicate) throw new InvalidOperationException("相同婚配关系已经存在。");
    }
}
