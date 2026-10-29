using ZupuPaiBan.Models;

namespace ZupuPaiBan.Domain;

/// <summary>Provides the stable, direction-free identity used for a complete family union.</summary>
public static class FamilyUnionIdentity
{
    public static (Guid PartnerOneId, Guid? PartnerTwoId) Normalize(Guid partnerOneId, Guid? partnerTwoId)
    {
        if (partnerTwoId is not Guid secondId || partnerOneId.CompareTo(secondId) <= 0)
            return (partnerOneId, partnerTwoId);
        return (secondId, partnerOneId);
    }

    public static void Normalize(FamilyUnion union)
    {
        var (partnerOneId, partnerTwoId) = Normalize(union.PartnerOneId, union.PartnerTwoId);
        union.PartnerOneId = partnerOneId;
        union.PartnerTwoId = partnerTwoId;
    }
}
