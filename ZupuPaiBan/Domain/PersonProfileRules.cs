using ZupuPaiBan.Models;

namespace ZupuPaiBan.Domain;

public static class PersonProfileRules
{
    public static string Validate(string? name, PersonSex sex, string? birthDate, string? deathDate, string? biography)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 200)
            throw new InvalidOperationException("人物姓名须为 1 至 200 个字符。");
        if (!Enum.IsDefined(sex)) throw new InvalidOperationException("性别选项无效。");
        if (birthDate?.Length > 100 || deathDate?.Length > 100)
            throw new InvalidOperationException("生卒日期文字不能超过 100 个字符。");
        if (biography?.Length > 4000) throw new InvalidOperationException("生平不能超过 4000 个字符。");
        return normalized;
    }
}
