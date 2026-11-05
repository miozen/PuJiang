using System.Globalization;

namespace ZupuPaiBan.Domain;

/// <summary>Conservative recognition for display only; never changes the source text.</summary>
public static class RecordedGenerationParser
{
    public static bool TryParse(string? value, out int generation)
    {
        generation = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.StartsWith('第')) text = text[1..].TrimStart();
        if (text.EndsWith('世')) text = text[..^1].TrimEnd();
        if (text.Length == 0) return false;
        if (text.All(c => c is >= '0' and <= '9'))
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out generation) && generation is >= 1 and <= 1000;
        static int Digit(char c) => c switch { '零' or '〇' => 0, '一' => 1, '二' or '两' => 2, '三' => 3, '四' => 4, '五' => 5, '六' => 6, '七' => 7, '八' => 8, '九' => 9, _ => -1 };
        if (text.All(c => Digit(c) >= 0))
        {
            if (text.Length > 4) return false;
            var number = 0;
            foreach (var c in text) number = number * 10 + Digit(c);
            if (number is < 1 or > 1000) return false;
            generation = number; return true;
        }
        var total = 0; var pending = -1; var previousUnit = 10000; var sawUnit = false; var zeroBeforeDigit = false;
        foreach (var c in text)
        {
            var digit = Digit(c);
            if (digit >= 0)
            {
                if (digit == 0)
                {
                    if (!sawUnit || previousUnit == 10 || pending >= 0 || zeroBeforeDigit) return false;
                    zeroBeforeDigit = true; continue;
                }
                if (pending >= 0) return false;
                pending = digit; continue;
            }
            var unit = c switch { '十' => 10, '百' => 100, '千' => 1000, _ => 0 };
            if (unit == 0 || unit >= previousUnit || (zeroBeforeDigit && pending < 0)) return false;
            if (pending < 0 && !(unit == 10 && !sawUnit)) return false;
            total += (pending < 0 ? 1 : pending) * unit;
            pending = -1; previousUnit = unit; sawUnit = true; zeroBeforeDigit = false;
        }
        if (!sawUnit || (zeroBeforeDigit && pending < 0)) return false;
        if (pending >= 0)
        {
            // “一百二” is ambiguous; require a zero or an explicit tens unit.
            if (previousUnit >= 100 && !zeroBeforeDigit) return false;
            total += pending;
        }
        if (total is < 1 or > 1000) return false;
        generation = total; return true;
    }
}
