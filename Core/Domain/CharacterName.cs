using System.Text.RegularExpressions;

namespace GaugeOverlay.Core.Domain;

/// <summary>In-game character names read by OCR (homework.plan.md): cleaned to letters and digits, and snapped to a known name
/// when one letter was misread, so a misread doesn't make a new character. Pure.</summary>
public static partial class CharacterName
{
    const int MinLength = 2, MaxLength = 16;

    /// <summary>The name in <paramref name="text"/>: Hangul, Latin letters and digits with at least one letter; null for an
    /// item level ("1791.66") or a scrap.</summary>
    public static string? Clean(string text)
    {
        if (Decimal().IsMatch(text)) return null;
        string name = string.Concat(text.Where(c => c is >= '가' and <= '힣' || char.IsAsciiLetterOrDigit(c)));
        return name.Length is >= MinLength and <= MaxLength && name.Any(c => !char.IsAsciiDigit(c)) ? name : null;
    }

    /// <summary>A known name when <paramref name="read"/> is it or one letter off from it alone; else the read.</summary>
    public static string Snap(string read, IEnumerable<string> known)
    {
        var names = known.ToList();
        if (names.Contains(read)) return read;
        var near = names.Where(n => n.Length >= 3 && Distance(n, read) <= 1).ToList();
        return near.Count == 1 ? near[0] : read;
    }

    /// <summary>The known name a grid card's small, often misread name is (Windows OCR reads "아" as "Ot", "키" as "기"): the
    /// nearest, within half its length (at least 2 letters), and strictly nearer than any other; else null.</summary>
    public static string? Nearest(string read, IEnumerable<string> known)
    {
        var ranked = known.Select(n => (Name: n, D: Distance(n, read))).OrderBy(x => x.D).ToList();
        if (ranked.Count == 0 || ranked[0].D > Math.Max(2, ranked[0].Name.Length / 2)) return null;
        return ranked.Count > 1 && ranked[1].D == ranked[0].D ? null : ranked[0].Name;
    }

    /// <summary>Levenshtein distance.</summary>
    static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int diag = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int up = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diag + (a[i - 1] == b[j - 1] ? 0 : 1));
                diag = up;
            }
        }
        return row[b.Length];
    }

    [GeneratedRegex(@"\d[.,]\d")]
    private static partial Regex Decimal();
}
