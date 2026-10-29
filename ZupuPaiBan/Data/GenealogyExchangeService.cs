using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

/// <summary>Versioned, lossless JSON exchange contract for one target-model genealogy.</summary>
public sealed record GenealogyExchangeDocument(
    int FormatVersion,
    GenealogyGraphInfo Genealogy,
    IReadOnlyList<GenealogyPersonData> People,
    IReadOnlyList<GenealogyRelationshipData> Relationships,
    IReadOnlyList<GenealogyUnionData> Unions,
    IReadOnlyList<GenealogyBranchData> Branches);

/// <summary>Only an in-memory mapping; persisting or replacing a genealogy is deliberately out of scope.</summary>
public sealed record GenealogyExchangeTarget(
    Genealogy Genealogy,
    IReadOnlyList<Person> People,
    IReadOnlyList<PersonRelationship> Relationships,
    IReadOnlyList<FamilyUnion> Unions,
    IReadOnlyList<FamilyBranch> Branches);

public sealed record GenealogyExchangePreflightReport(int FormatVersion, int People, int Relationships,
    int Unions, int Branches, int PhotoPathReferences);

public static class GenealogyExchangeService
{
    public const int CurrentFormatVersion = 1;
    private const long MaxDocumentBytes = 16L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static GenealogyExchangeDocument Export(GenealogyGraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(CurrentFormatVersion, snapshot.Genealogy, snapshot.People, snapshot.Relationships, snapshot.Unions, snapshot.Branches);
    }

    /// <summary>Returns a stable SHA-256 identity for the semantic v1 document, independent of its local file path.</summary>
    public static string ComputeDocumentFingerprint(GenealogyExchangeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions)));
    }

    public static async Task WriteAsync(GenealogyGraphSnapshot snapshot, string path, CancellationToken cancellationToken = default)
    {
        var document = Export(snapshot);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(output, document, JsonOptions, cancellationToken);
    }

    public static async Task<(GenealogyExchangeDocument Document, GenealogyExchangePreflightReport Report)> PreflightAsync(
        string path, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("交换文件必须使用绝对路径。", nameof(path));
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("交换文件不存在。", path);
        if (info.Length <= 0 || info.Length > MaxDocumentBytes) throw new InvalidDataException("交换文件大小超出支持范围。");

        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        GenealogyExchangeDocument document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<GenealogyExchangeDocument>(input, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("交换文件为空。");
        }
        catch (JsonException exception) { throw new InvalidDataException("交换文件不是有效的 JSON。", exception); }
        return (document, Preflight(document));
    }

    public static GenealogyExchangePreflightReport Preflight(GenealogyExchangeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.FormatVersion != CurrentFormatVersion) throw new InvalidDataException("不支持该交换文件版本。");
        if (document.Genealogy is null || document.Genealogy.Id == Guid.Empty || string.IsNullOrWhiteSpace(document.Genealogy.Name) || document.Genealogy.Name.Length > 200)
            throw new InvalidDataException("交换文件的族谱信息无效。");
        if (document.People is null || document.Relationships is null || document.Unions is null || document.Branches is null)
            throw new InvalidDataException("交换文件缺少必需的数据集合。");

        var people = document.People.ToDictionary(person => person.Id, person => person);
        if (people.Count != document.People.Count || people.Keys.Any(id => id == Guid.Empty)) throw new InvalidDataException("人物 ID 无效或重复。");
        if (people.Values.Any(person => string.IsNullOrWhiteSpace(person.Name) || person.Name.Length > 200 || !Enum.IsDefined(person.Sex)))
            throw new InvalidDataException("人物资料无效。");

        var branches = document.Branches.ToDictionary(branch => branch.Id, branch => branch);
        if (branches.Count != document.Branches.Count || branches.Keys.Any(id => id == Guid.Empty) ||
            branches.Values.Any(branch => string.IsNullOrWhiteSpace(branch.Name) || branch.Name.Length > 200))
            throw new InvalidDataException("房支资料无效或重复。");
        foreach (var branch in branches.Values)
            if (branch.ParentBranchId is Guid parent && (!branches.ContainsKey(parent) || parent == branch.Id))
                throw new InvalidDataException("房支父级引用无效。");
        EnsureBranchAcyclic(branches);
        if (people.Values.Any(person => person.BranchId is Guid branch && !branches.ContainsKey(branch)))
            throw new InvalidDataException("人物引用了不存在的房支。");

        var relationshipIds = new HashSet<Guid>();
        var relationshipKeys = new HashSet<(Guid Person, Guid? Related, PersonRelationshipKind Kind)>();
        foreach (var relationship in document.Relationships)
        {
            if (relationship.Id == Guid.Empty || !relationshipIds.Add(relationship.Id) || !people.ContainsKey(relationship.PersonId) ||
                !Enum.IsDefined(relationship.Kind) || (relationship.Kind == PersonRelationshipKind.LostAncestry) != (relationship.RelatedPersonId is null) ||
                (relationship.RelatedPersonId is Guid related && (!people.ContainsKey(related) || related == relationship.PersonId)) ||
                !relationshipKeys.Add((relationship.PersonId, relationship.RelatedPersonId, relationship.Kind)))
                throw new InvalidDataException("人物关系无效、重复或跨出当前族谱。");
        }

        var unionIds = new HashSet<Guid>();
        var unionKeys = new HashSet<(Guid One, Guid? Two, FamilyUnionKind Kind)>();
        foreach (var union in document.Unions)
        {
            var (partnerOneId, partnerTwoId) = FamilyUnionIdentity.Normalize(union.PartnerOneId, union.PartnerTwoId);
            if (union.Id == Guid.Empty || !unionIds.Add(union.Id) || !people.ContainsKey(union.PartnerOneId) || !Enum.IsDefined(union.Kind) ||
                (union.PartnerTwoId is Guid two && (!people.ContainsKey(two) || two == union.PartnerOneId)) ||
                !unionKeys.Add((partnerOneId, partnerTwoId, union.Kind)))
                throw new InvalidDataException("婚配关系无效、重复或跨出当前族谱。");
        }

        return new(document.FormatVersion, document.People.Count, document.Relationships.Count, document.Unions.Count,
            document.Branches.Count, document.People.Count(person => !string.IsNullOrWhiteSpace(person.PhotoPath)));
    }

    public static GenealogyExchangeTarget MapToTarget(GenealogyExchangeDocument document)
    {
        Preflight(document);
        var genealogy = new Genealogy
        {
            Id = document.Genealogy.Id,
            Name = document.Genealogy.Name,
            Surname = document.Genealogy.Surname,
            Description = document.Genealogy.Description,
            CreatedAt = document.Genealogy.CreatedAt
        };
        var people = document.People.Select(person => new Person
        {
            Id = person.Id, GenealogyId = genealogy.Id, Name = person.Name, Sex = person.Sex,
            BirthDateText = person.BirthDateText, DeathDateText = person.DeathDateText, Biography = person.Biography,
            PhotoPath = person.PhotoPath, IsHistoricalRecord = person.IsHistoricalRecord,
            RecordedGeneration = person.RecordedGeneration, DerivedGeneration = person.DerivedGeneration,
            RecordedBirthOrder = person.RecordedBirthOrder, DerivedBirthOrder = person.DerivedBirthOrder, BranchId = person.BranchId
        }).ToList();
        var relationships = document.Relationships.Select(relationship => new PersonRelationship
        {
            Id = relationship.Id, GenealogyId = genealogy.Id, PersonId = relationship.PersonId,
            RelatedPersonId = relationship.RelatedPersonId, Kind = relationship.Kind,
            IsPreferredForLineage = relationship.IsPreferredForLineage, Note = relationship.Note, SourceCitation = relationship.SourceCitation
        }).ToList();
        var unions = document.Unions.Select(union => new FamilyUnion
        {
            Id = union.Id, GenealogyId = genealogy.Id, PartnerOneId = union.PartnerOneId, PartnerTwoId = union.PartnerTwoId,
            Kind = union.Kind, StartDateText = union.StartDateText, EndDateText = union.EndDateText, Note = union.Note
        }).ToList();
        var branches = document.Branches.Select(branch => new FamilyBranch
        {
            Id = branch.Id, GenealogyId = genealogy.Id, ParentBranchId = branch.ParentBranchId, Name = branch.Name, SortOrder = branch.SortOrder
        }).ToList();
        return new(genealogy, people.AsReadOnly(), relationships.AsReadOnly(), unions.AsReadOnly(), branches.AsReadOnly());
    }

    private static void EnsureBranchAcyclic(IReadOnlyDictionary<Guid, GenealogyBranchData> branches)
    {
        foreach (var branch in branches.Values)
        {
            var visited = new HashSet<Guid>();
            var current = branch;
            while (current.ParentBranchId is Guid parent)
            {
                if (!visited.Add(current.Id) || !branches.TryGetValue(parent, out var next))
                    throw new InvalidDataException("房支存在循环引用。");
                current = next;
            }
        }
    }
}
