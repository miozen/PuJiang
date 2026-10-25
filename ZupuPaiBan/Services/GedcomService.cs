using System.IO;
using System.Text;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Services;

public class GedcomService
{
    public async Task<List<FamilyMember>> ImportFromGedcomAsync(string filePath)
    {
        var members = new List<FamilyMember>();
        var indiDict = new Dictionary<string, int>(); // GEDCOM ID -> our ID
        var famDict = new Dictionary<string, (string? husb, string? wife)>(); // Family ID -> (husband, wife)

        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
        int currentId = 1;
        string? currentIndi = null;
        string? currentFam = null;
        FamilyMember? currentMember = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = line.Split(' ', 3);
            if (parts.Length < 2) continue;

            int level = int.Parse(parts[0]);
            string tag = parts[1];
            string? value = parts.Length > 2 ? parts[2] : null;

            if (level == 0)
            {
                if (tag == "@INDI@" || (value != null && value.Contains("@") && tag.StartsWith("@")))
                {
                    // New individual
                    if (currentMember != null)
                    {
                        members.Add(currentMember);
                    }
                    currentIndi = tag.Trim('@');
                    currentMember = new FamilyMember { Id = currentId++ };
                    indiDict[currentIndi] = currentMember.Id;
                }
                else if (tag == "@FAM@" || (value != null && value.Contains("@") && tag.StartsWith("@")))
                {
                    currentFam = tag.Trim('@');
                    currentIndi = null;
                    currentMember = null;
                }
                else
                {
                    currentIndi = null;
                    currentFam = null;
                    currentMember = null;
                }
            }
            else if (level == 1 && currentMember != null)
            {
                switch (tag)
                {
                    case "NAME":
                        currentMember.Name = value?.Replace("/", "") ?? "";
                        break;
                    case "SEX":
                        currentMember.Gender = value == "F" ? Gender.Female : Gender.Male;
                        break;
                    case "BIRT":
                        // Next level will have DATE
                        break;
                    case "DEAT":
                        // Next level will have DATE
                        break;
                    case "FAMS":
                        // Family where this person is a spouse
                        break;
                    case "FAMC":
                        // Family where this person is a child
                        break;
                }
            }
            else if (level == 2 && currentMember != null)
            {
                switch (tag)
                {
                    case "DATE":
                        // Try to set birth/death date based on context
                        if (currentMember.BirthDate == "")
                            currentMember.BirthDate = value ?? "";
                        else if (currentMember.DeathDate == null)
                            currentMember.DeathDate = value;
                        break;
                    case "NOTE":
                        currentMember.Biography = value;
                        break;
                }
            }
        }

        if (currentMember != null)
        {
            members.Add(currentMember);
        }

        // Second pass: establish parent-child relationships
        // This is simplified - real GEDCOM parsing would be more complex
        return members;
    }

    public async Task ExportToGedcomAsync(List<FamilyMember> members, string filePath)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("0 HEAD");
        sb.AppendLine("1 SOUR 谱匠 PuJiang");
        sb.AppendLine("2 VERS 1.0");
        sb.AppendLine("1 GEDC");
        sb.AppendLine("2 VERS 5.5.1");
        sb.AppendLine("1 CHAR UTF-8");

        int indiNum = 1;
        var idMap = new Dictionary<int, string>();

        // Individuals
        foreach (var member in members)
        {
            string indiId = $"I{indiNum:D4}";
            idMap[member.Id] = indiId;
            indiNum++;

            sb.AppendLine($"0 @{indiId}@ INDI");
            sb.AppendLine($"1 NAME {member.Name.Replace("/", "//")}");
            sb.AppendLine($"1 SEX {(member.Gender == Gender.Male ? "M" : "F")}");

            if (!string.IsNullOrEmpty(member.BirthDate))
            {
                sb.AppendLine("1 BIRT");
                sb.AppendLine($"2 DATE {member.BirthDate}");
            }

            if (!string.IsNullOrEmpty(member.DeathDate))
            {
                sb.AppendLine("1 DEAT");
                sb.AppendLine($"2 DATE {member.DeathDate}");
            }

            if (!string.IsNullOrEmpty(member.Biography))
            {
                sb.AppendLine($"1 NOTE {member.Biography}");
            }
        }

        // Families
        int famNum = 1;
        var processedFamilies = new HashSet<int>();

        foreach (var member in members.Where(m => m.Children.Any() || !string.IsNullOrEmpty(m.SpouseName)))
        {
            if (processedFamilies.Contains(member.Id)) continue;

            string famId = $"F{famNum:D4}";
            famNum++;

            sb.AppendLine($"0 @{famId}@ FAM");

            if (idMap.TryGetValue(member.Id, out var husbId))
            {
                sb.AppendLine($"1 HUSB @{husbId}@");
            }

            // Add children
            foreach (var child in member.Children)
            {
                if (idMap.TryGetValue(child.Id, out var childId))
                {
                    sb.AppendLine($"1 CHIL @{childId}@");
                }
            }

            processedFamilies.Add(member.Id);
        }

        sb.AppendLine("0 TRLR");

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }
}
