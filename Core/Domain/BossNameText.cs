using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>자동 인식 by the boss's name as text (Windows OCR of the strip above the HP bar): any resolution, any mode (싱글 모드
/// HP isn't in raids.json). Spaces and punctuation are ignored, and a few misread letters are forgiven (one per five letters
/// of the name); a name that two gates fit equally well (the same boss in both) gives no gate, never a guess. Pure.</summary>
public static class BossNameText
{
    const int LettersPerError = 5;

    /// <summary>The gate whose boss name one of <paramref name="lines"/> holds; null when none fits. When gates fit as well
    /// (the same boss in 종막 2-1 and EX 2-1), the one of them in <paramref name="prefer"/> (gates with a window on), else null.</summary>
    public static string? Match(IReadOnlyList<string> lines, IReadOnlyList<RaidGate> gates, IReadOnlyCollection<string>? prefer = null)
    {
        var read = lines.Select(Normalize).Where(l => l.Length > 0).ToList();
        if (read.Count == 0) return null;
        var best = new List<string>();
        int bestErrors = int.MaxValue;
        foreach (var gate in gates)
            foreach (var boss in gate.Bosses.Select(Normalize).Where(b => b.Length > 0))
            {
                int errors = read.Min(line => Errors(line, boss));
                if (errors > Math.Max(1, boss.Length / LettersPerError) || errors > bestErrors) continue;
                if (errors < bestErrors) (best, bestErrors) = ([], errors);
                if (!best.Contains(gate.Id)) best.Add(gate.Id);
            }
        if (best.Count == 1) return best[0];
        var preferred = best.Where(id => prefer?.Contains(id) == true).ToList();
        return preferred.Count == 1 ? preferred[0] : null;
    }

    /// <summary>The gates the dungeon panel allows: its raid's (the raid name sharing the most letters with the panel's title,
    /// at least half), then the EX gates for an EX difficulty and the others for any other. A raid raids.json doesn't know
    /// narrows nothing.</summary>
    public static IReadOnlyList<RaidGate> Candidates(IReadOnlyList<RaidGate> gates, DungeonInfo? panel)
    {
        if (panel is not { Kind: DungeonKind.Raid, Title: { } title }) return gates;
        static string NameOf(RaidGate g) => g.Raid[(g.Raid.IndexOf(':') + 1)..];   // "4막: 파멸의 성채" → " 파멸의 성채"
        var scored = gates.Select(g => (Gate: g, Score: DungeonPanel.Shared(title, NameOf(g)))).Where(x => x.Score >= 0.5).ToList();
        if (scored.Count == 0) return gates;   // a raid raids.json doesn't know: its difficulty says nothing about the gates
        List<RaidGate> shown = [.. scored.Where(x => x.Score == scored.Max(y => y.Score)).Select(x => x.Gate)];
        if (panel.Difficulty is not { } d) return shown;
        bool ex = d.StartsWith("EX", StringComparison.Ordinal);
        static IEnumerable<string> Levels(RaidGate g) => g.Hp.Keys.Concat(g.HiddenHp.Keys);
        static bool IsEx(RaidGate g) => g.Gate.Contains("EX") || Levels(g).Any(k => k.StartsWith("EX", StringComparison.Ordinal));
        // EX or not only: an exact difficulty would drop a gate whose HP for it isn't in raids.json yet (4막 2관문 싱글).
        List<RaidGate> level = [.. shown.Where(g => IsEx(g) == ex)];
        return level.Count > 0 ? level : shown;
    }

    /// <summary>Letters and digits only: "광신의 인도자, 아르세노스" → "광신의인도자아르세노스".</summary>
    static string Normalize(string s) => new([.. s.Where(char.IsLetterOrDigit)]);

    /// <summary>The fewest edits that turn some stretch of <paramref name="line"/> into <paramref name="name"/> (the line may hold
    /// more: the red 보스 tag).</summary>
    static int Errors(string line, string name)
    {
        if (line.Length <= name.Length + 1) return Distance(line, name);
        int best = int.MaxValue;
        for (int w = name.Length - 1; w <= name.Length + 1; w++)
            for (int i = 0; i + w <= line.Length; i++)
                best = Math.Min(best, Distance(line.Substring(i, w), name));
        return best;
    }

    /// <summary>Levenshtein distance.</summary>
    static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1], prev[j]) + 1, prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
