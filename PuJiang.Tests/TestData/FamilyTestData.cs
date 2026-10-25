using ZupuPaiBan.Models;

namespace PuJiang.Tests.TestData;

internal static class FamilyTestData
{
    public static List<FamilyMember> ThreeGenerations() =>
    [
        new() { Id = 1, Name = "虚构始祖", BirthOrder = 1, Generation = 1 },
        new() { Id = 2, Name = "虚构长子", FatherId = 1, BirthOrder = 1, Generation = 2 },
        new() { Id = 3, Name = "虚构次子", FatherId = 1, BirthOrder = 2, Generation = 2 },
        new() { Id = 4, Name = "虚构长孙", FatherId = 2, BirthOrder = 1, Generation = 3 }
    ];

    public static List<FamilyMember> ThousandPersonSample()
    {
        var members = new List<FamilyMember>(capacity: 1_000)
        {
            new() { Id = 1, Name = "虚构始祖", BirthOrder = 1, Generation = 1 }
        };

        for (var id = 2; id <= 1_000; id++)
        {
            var parentId = ((id - 2) / 3) + 1;
            members.Add(new FamilyMember
            {
                Id = id,
                Name = $"虚构成员{id}",
                FatherId = parentId,
                BirthOrder = ((id - 2) % 3) + 1,
                Generation = members[parentId - 1].Generation + 1
            });
        }

        return members;
    }
}
