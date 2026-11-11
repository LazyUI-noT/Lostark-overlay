using System.Text.RegularExpressions;

namespace GaugeOverlay.Core.Domain;

/// <summary>In-game character names read by OCR (homework.plan.md): cleaned to letters and digits, and snapped to a known name
/// when one letter came out garbled, so a misread doesn't make a new character. Pure.</summary>
public static partial class CharacterName
{
    const int MinLength = 2, MaxLength = 16;

    /// <summary>The name in <paramref name="text"/>: Hangul, Latin letters and digits, no more digits than letters; null for an
    /// item level ("1791.66"), a number read with its icon ("수164이00912" became a character and named fights, user 2026-11-10)
    /// or a scrap.</summary>
    public static string? Clean(string text)
    {
        if (Decimal().IsMatch(text)) return null;
        string name = string.Concat(text.Where(c => IsHangul(c) || char.IsAsciiLetterOrDigit(c)));
        int digits = name.Count(char.IsAsciiDigit);
        return name.Length is >= MinLength and <= MaxLength && digits <= name.Length - digits ? name : null;
    }

    /// <summary>A grid card's small name (<see cref="Clean"/>), with the small font's misreads put back: "Ot" between Hangul is
    /// 아. Card reads only: a typed name stays as typed.</summary>
    public static string? CardName(string text) => Clean(text) is { } name ? OtIsA().Replace(name, "아") : null;

    /// <summary>A known name when <paramref name="read"/> is it or one letter off from it alone; else the read. One letter swapped
    /// for another of its kind is another character (너무아파요살살, 너무아파용살살), not a misread: <see cref="NameVote"/> stops
    /// the misreads that vary, and merging two characters would mislabel fights for good.</summary>
    public static string Snap(string read, IEnumerable<string> known)
    {
        var names = known.ToList();
        if (names.Contains(read)) return read;
        var near = names.Where(n => n.Length >= 3 && Distance(n, read) <= 1).ToList();
        return near.Count == 1 && !SameKindSwap(near[0], read) ? near[0] : read;
    }

    static bool SameKindSwap(string name, string read) =>
        name.Length == read.Length && name.Zip(read).Any(p => p.First != p.Second && Kind(p.First) == Kind(p.Second));

    static int Kind(char c) => IsHangul(c) ? 0 : char.IsAsciiDigit(c) ? 1 : 2;

    static bool IsHangul(char c) => c is >= '가' and <= '힣';

    /// <summary>The known name a grid card's small, often misread name is (Windows OCR reads "아" as "Ot", "키" as "기"): the
    /// nearest, within half its length (at least 2 letters), and strictly nearer than any other; else null.</summary>
    public static string? Nearest(string read, IEnumerable<string> known)
    {
        var ranked = known.Select(n => (Name: n, D: Distance(n, read))).OrderBy(x => x.D).ToList();
        if (ranked.Count == 0 || ranked[0].D > Reach(ranked[0].Name)) return null;
        return ranked.Count > 1 && ranked[1].D == ranked[0].D ? null : ranked[0].Name;
    }

    /// <summary>The known names in reach of <paramref name="read"/> when <see cref="Nearest"/> finds one in reach at all (the
    /// nearest within half its length, at least 2 letters), each within its own; with their distance: for a choice it can't make
    /// by the name alone, a tie (Homework.CardOwner).</summary>
    public static IReadOnlyList<(string Name, int Distance)> Near(string read, IEnumerable<string> known)
    {
        var ranked = known.Distinct().Select(n => (Name: n, Distance: Distance(n, read))).OrderBy(x => x.Distance).ToList();
        if (ranked.Count == 0 || ranked[0].Distance > Reach(ranked[0].Name)) return [];
        return [.. ranked.Where(x => x.Distance <= Reach(x.Name))];
    }

    static int Reach(string name) => Math.Max(2, name.Length / 2);

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

    /// <summary>The card's small font: 아 between Hangul reads "Ot" (너무아파요살살 as "너무Ot파요살살", user 2026-11-08).</summary>
    [GeneratedRegex("(?<=[가-힣])Ot(?=[가-힣])")]
    private static partial Regex OtIsA();

    [GeneratedRegex(@"\d[.,]\d")]
    private static partial Regex Decimal();
}
