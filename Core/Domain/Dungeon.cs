using System.Collections.Immutable;

namespace GaugeOverlay.Core.Domain;

public enum DungeonKind { Raid, Guardian }

/// <summary>What the top-left panel says: a raid (its gate name after "N막 :" and the difficulty line, either may be null when
/// unread) or a guardian raid.</summary>
public sealed record DungeonInfo(DungeonKind Kind, string? Title, string? Difficulty);

/// <summary>The dungeon as settled over several reads: what to show, and how often each name was read in this dungeon.</summary>
public sealed record DungeonState(DungeonInfo? Shown, ImmutableDictionary<string, int> Votes)
{
    public static readonly DungeonState None = new(null, ImmutableDictionary<string, int>.Empty);
}

/// <summary>The top-left dungeon panel from its OCR lines (spec § D): a raid shows "N막 : name", a difficulty in brackets
/// ([싱글 모드] / [노말] / [하드] / [나이트메어], EX in front for EX raids) and 재시작; a guardian raid shows 목표 가디언 and
/// 토벌 중단 instead. OCR misreads Korean now and then ("심글", "니이트에어"), so the difficulty is the word sharing the most
/// letters with the bracket line. Pure.</summary>
public static class DungeonPanel
{
    static readonly (string Name, string Letters)[] Difficulties =
        [("싱글", "싱글모드"), ("매칭", "매칭"), ("노말", "노말"), ("하드", "하드"), ("나이트메어", "나이트메어")];

    public static DungeonInfo? Parse(IReadOnlyList<string> lines)
    {
        if (lines.Any(l => l.Contains("가디언") || l.Contains("토벌"))) return new(DungeonKind.Guardian, null, null);
        int bracket = lines.ToList().FindIndex(l => l.Contains('[') || l.Contains(']'));
        // Some raids (벨가르딘) show the name alone, without "N막 :": then it's the line just above the difficulty.
        string? title = lines.Select(Title).FirstOrDefault(t => t is not null)
            ?? (bracket > 0 && !lines[bracket - 1].Contains("재시작") ? Name(lines[bracket - 1]) : null);
        string? difficulty = lines.Where(l => l.Contains('[') || l.Contains(']')).Select(Difficulty).FirstOrDefault(d => d is not null);
        return title is null && difficulty is null && !lines.Any(l => l.Contains("재시작")) ? null
            : new(DungeonKind.Raid, title, difficulty);
    }

    /// <summary>Adds a read: the name read most in this dungeon wins over an odd misread ("질혹" for "칠흑"), a line a read
    /// missed keeps the earlier one, and a name sharing under half its letters with the one shown (or a guardian raid) is
    /// another dungeon, so the count starts over.</summary>
    public static DungeonState Next(DungeonState s, DungeonInfo d)
    {
        bool same = s.Shown is { } was && was.Kind == d.Kind && (d.Title is null || was.Title is null || Shared(d.Title, was.Title) >= 0.5);
        var votes = same ? s.Votes : ImmutableDictionary<string, int>.Empty;
        if (d.Title is { } t) votes = votes.SetItem(t, votes.GetValueOrDefault(t) + 1);
        string? shown = same ? s.Shown!.Title : null;   // a tie keeps the name shown, so two readings don't take turns
        string? title = votes.IsEmpty ? null
            : shown is not null && votes.GetValueOrDefault(shown) >= votes.Values.Max() ? shown : votes.MaxBy(v => v.Value).Key;
        // OCR drops "EX" now and then but never makes it up: an EX difficulty stays over the same one read without it.
        string? difficulty = same && s.Shown!.Difficulty is { } had && (d.Difficulty is null || had == "EX " + d.Difficulty) ? had : d.Difficulty;
        return new(same ? new(d.Kind, title ?? s.Shown!.Title, difficulty) : d with { Title = title }, votes);
    }

    static double Shared(string a, string b)
    {
        var letters = a.Where(c => !char.IsWhiteSpace(c)).Distinct().ToList();
        return letters.Count == 0 ? 0 : letters.Count(b.Contains) / (double)letters.Count;
    }

    // "3막 : name" (the act's digit is often misread, or lost with the 막): the name after the colon, when it has Hangul;
    // marks no name has (a stray ' or |) are dropped.
    static string? Title(string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0 || !(line[..colon].Contains('막') || line[..colon].Trim().Length == 0)) return null;
        return Name(line[(colon + 1)..]);
    }

    static string? Name(string text)
    {
        string name = string.Join(' ', new string([.. text.Where(c => char.IsLetterOrDigit(c) || c is ' ' or ',')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return name.Any(c => c is >= '가' and <= '힣') ? name : null;
    }

    static string? Difficulty(string line)
    {
        var (name, score) = Difficulties.Select(d => (d.Name, Score: d.Letters.Count(line.Contains) / (double)d.Letters.Length))
            .MaxBy(d => d.Score);
        if (score < 0.5) return null;
        return line.Contains("EX", StringComparison.OrdinalIgnoreCase) ? "EX " + name : name;
    }
}
