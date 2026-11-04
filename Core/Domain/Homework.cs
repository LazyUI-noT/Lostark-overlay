using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>A 숙제 tag's colour: the game's difficulty colours (노말 white, 하드 orange, 나이트메어 purple).</summary>
public enum HomeworkColor { White, Orange, Purple }

/// <summary>One difficulty of a 숙제 raid (raids.json "homework"), in rank order: its default tag ("세하"), colour, and the
/// gate(s) whose 클리어 finishes the raid on it.</summary>
public sealed record HomeworkLevel(string Difficulty, string Tag, HomeworkColor Color, IReadOnlyList<string> Last, int? Entry = null);

/// <summary>A raid tracked weekly: its gates (raids.json ids) and difficulties, lowest first.</summary>
public sealed record HomeworkRaid(string Id, string Name, IReadOnlyList<string> Gates, IReadOnlyList<HomeworkLevel> Levels);

/// <summary>A raid the user added on 설정 → 숙제 (a missing or time-limited one, like 종막 EX): no gates or entry levels, so no fight
/// clears it and no level allocates it; its tag is clicked. <see cref="Auto"/>: put on every character, new ones too.</summary>
public sealed record HomeworkCustomRaid(string Id, string Name, IReadOnlyList<HomeworkLevel> Levels, bool Auto);

/// <summary>A raid a character picked to track, at the difficulty picked.</summary>
public sealed record HomeworkPick(string Raid, string Difficulty);

/// <summary>A raid cleared: by whom, on which difficulty, and when the fight started (its week).</summary>
public sealed record HomeworkClear(string Character, string Raid, string Difficulty, DateTime At);

/// <summary>A pick as drawn: the text, its colour, crossed out when cleared this week, and ↓ (−1) / ↑ (+1) when cleared on a
/// lower / higher difficulty than picked.</summary>
public sealed record HomeworkTag(string Text, HomeworkColor Color, bool Done, int Arrow);

/// <summary>숙제 (homework.plan.md): weekly raid clears per character. A raid's weekly entry is shared by its difficulties, so a
/// clear on any of them crosses the pick out; one above the pick raises it. Pure.</summary>
public static class Homework
{
    static readonly TimeSpan ResetHour = TimeSpan.FromHours(6);

    /// <summary>The start of the 숙제 week <paramref name="local"/> is in: the last Wednesday 06:00 at or before it.</summary>
    public static DateTime WeekStart(DateTime local)
    {
        var day = local.Date.AddDays(-(((int)local.DayOfWeek - (int)DayOfWeek.Wednesday + 7) % 7));
        var start = day + ResetHour;
        return start <= local ? start : start.AddDays(-7);
    }

    /// <summary>The clear a saved fight makes: 클리어 on its difficulty's last gate, by a known character; else null.</summary>
    public static HomeworkClear? ClearOf(FightRecord r, IReadOnlyList<RaidGate> gates, IReadOnlyList<HomeworkRaid> raids)
    {
        if (r.Cleared != true || string.IsNullOrWhiteSpace(r.Character) || r.Difficulty is not { } difficulty) return null;
        if (gates.FirstOrDefault(g => RaidData.Label(g) == r.Gate) is not { } gate) return null;
        foreach (var raid in raids)
            if (raid.Levels.FirstOrDefault(l => l.Difficulty == difficulty) is { } level && level.Last.Contains(gate.Id))
                return new HomeworkClear(r.Character, raid.Id, difficulty, r.StartedAt);
        return null;
    }

    /// <summary>The key of a renamed tag in <see cref="HomeworkSettings.Tags"/>.</summary>
    public static string TagKey(string raid, string difficulty) => $"{raid}|{difficulty}";

    /// <summary>The tag text: the user's name for it, else the default.</summary>
    public static string TagText(HomeworkRaid raid, HomeworkLevel level, IReadOnlyDictionary<string, string> names) =>
        names.TryGetValue(TagKey(raid.Id, level.Difficulty), out var name) && !string.IsNullOrWhiteSpace(name) ? name : level.Tag;

    /// <summary>A pick as drawn, given the character's clears; null when its raid or difficulty is no longer known.</summary>
    public static HomeworkTag? Tag(HomeworkPick pick, IEnumerable<HomeworkClear> clears, IReadOnlyList<HomeworkRaid> raids,
        IReadOnlyDictionary<string, string> names, DateTime weekStart)
    {
        if (raids.FirstOrDefault(r => r.Id == pick.Raid) is not { } raid) return null;
        int picked = Rank(raid, pick.Difficulty);
        if (picked < 0) return null;
        var clear = clears.Where(c => c.Raid == raid.Id && c.At >= weekStart && Rank(raid, c.Difficulty) >= 0).MaxBy(c => c.At);
        if (clear is null) return new HomeworkTag(TagText(raid, raid.Levels[picked], names), raid.Levels[picked].Color, false, 0);
        int done = Rank(raid, clear.Difficulty);
        return new HomeworkTag(TagText(raid, raid.Levels[done], names), raid.Levels[done].Color, true, Math.Sign(done - picked));
    }

    /// <summary>The picks after <paramref name="clear"/>: its raid's pick raised to the difficulty cleared when that is higher;
    /// the same list when nothing changes (a lower or equal clear never lowers a pick).</summary>
    public static IReadOnlyList<HomeworkPick> Raise(IReadOnlyList<HomeworkPick> picks, HomeworkClear clear, IReadOnlyList<HomeworkRaid> raids)
    {
        if (raids.FirstOrDefault(r => r.Id == clear.Raid) is not { } raid) return picks;
        int done = Rank(raid, clear.Difficulty);
        if (!picks.Any(p => p.Raid == raid.Id && Rank(raid, p.Difficulty) < done)) return picks;
        return [.. picks.Select(p => p.Raid == raid.Id && Rank(raid, p.Difficulty) < done ? p with { Difficulty = clear.Difficulty } : p)];
    }

    /// <summary>The raids a character gets by item level (user, 2026-11-04): each raid at the highest difficulty it can enter (of
    /// two at one entry level, the higher), the <see cref="AutoPicks"/> with the highest entry level, a tie going to the raid whose
    /// top difficulty is highest. A raid without entry levels is picked by hand only.</summary>
    public static IReadOnlyList<HomeworkPick> Allocate(int itemLevel, IReadOnlyList<HomeworkRaid> raids) =>
        [.. raids.Select(r => (Raid: r, Open: r.Levels.LastOrDefault(l => l.Entry <= itemLevel)))
            .Where(x => x.Open is not null)
            .OrderByDescending(x => x.Open!.Entry).ThenByDescending(x => x.Raid.Levels.Max(l => l.Entry))
            .Take(AutoPicks).Select(x => new HomeworkPick(x.Raid.Id, x.Open!.Difficulty))];

    /// <summary>The picks once a character's level goes from <paramref name="was"/> to <paramref name="now"/>. A first level gives
    /// a character without picks its raids. A new level redoes them while <paramref name="follow"/> (the one 자동 switch, for all
    /// characters) is on; with no raid open at that level the picks stay.</summary>
    public static IReadOnlyList<HomeworkPick> PicksAfter(IReadOnlyList<HomeworkPick> picks, int? was, int now, bool follow, IReadOnlyList<HomeworkRaid> raids)
    {
        var added = picks.Where(IsAdded).ToList();
        bool allocate = was is null ? picks.Count == added.Count : was != now && follow;
        return allocate && Allocate(now, raids) is { Count: > 0 } picked ? [.. picked.Concat(added).Take(HomeworkCharacter.MaxPicks)] : picks;
    }

    /// <summary>A pick of a raid the user added: it stays through a new level, on top of the automatic ones.</summary>
    public static bool IsAdded(HomeworkPick pick) => pick.Raid.StartsWith(CustomPrefix, StringComparison.Ordinal);

    /// <summary>An added raid from its form: rows without a difficulty or tag are left out, either one names the other when alone;
    /// null without a name or a difficulty.</summary>
    public static HomeworkCustomRaid? NewCustom(string id, string name, IEnumerable<(string Difficulty, string Tag, HomeworkColor Color)> rows, bool auto)
    {
        var levels = rows.Select(r => (Difficulty: r.Difficulty.Trim(), Tag: r.Tag.Trim(), r.Color))
            .Where(r => r.Difficulty.Length > 0 || r.Tag.Length > 0)
            .Select(r => new HomeworkLevel(r.Difficulty.Length > 0 ? r.Difficulty : r.Tag, r.Tag.Length > 0 ? r.Tag : r.Difficulty, r.Color, []))
            .DistinctBy(l => l.Difficulty).ToList();
        return string.IsNullOrWhiteSpace(name) || levels.Count == 0 ? null : new HomeworkCustomRaid(id, name.Trim(), levels, auto);
    }

    public static HomeworkRaid AsRaid(HomeworkCustomRaid custom) => new(custom.Id, custom.Name, [], custom.Levels);

    /// <summary>The shipped raids and the added ones that are whole (settings.json can be edited by hand).</summary>
    public static IReadOnlyList<HomeworkRaid> AllRaids(IReadOnlyList<HomeworkRaid> shipped, IEnumerable<HomeworkCustomRaid> added) =>
        [.. shipped, .. added.Where(c => c is { Id: not null, Name: not null, Levels.Count: > 0 } && c.Id.StartsWith(CustomPrefix, StringComparison.Ordinal)
            && c.Levels.All(l => l is { Difficulty: not null, Tag: not null, Last: not null })).Select(AsRaid)];

    /// <summary>The picks with <paramref name="raid"/> on, at its top difficulty; the same list when it is on already or they are full.</summary>
    public static IReadOnlyList<HomeworkPick> WithRaid(IReadOnlyList<HomeworkPick> picks, HomeworkRaid raid) =>
        picks.Count >= HomeworkCharacter.MaxPicks || picks.Any(p => p.Raid == raid.Id) ? picks : [.. picks, new HomeworkPick(raid.Id, raid.Levels[^1].Difficulty)];

    public const string CustomPrefix = "custom-";

    /// <summary>The level kept after a read: a level doesn't fall, nor rise more than <see cref="MaxLevelStep"/> at once (a misread
    /// that would stick; a real jump is typed on 설정 → 숙제).</summary>
    public static int LevelFrom(int? stored, int read) =>
        stored is not { } level ? read : read > level && read - level <= MaxLevelStep ? read : level;

    public const int AutoPicks = 3, MaxLevelStep = 60;

    static int Rank(HomeworkRaid raid, string difficulty) => raid.Levels.ToList().FindIndex(l => l.Difficulty == difficulty);
}
