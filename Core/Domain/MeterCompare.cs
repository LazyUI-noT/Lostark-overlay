namespace GaugeOverlay.Core.Domain;

/// <summary>The meter's 비교 line (lostark-api.plan.md phase 6): a saved fight of the same raid (<see cref="Timeline.SameRaid"/>)
/// and character, its my-DPS drawn dashed under my live line on the same 전분 seconds. 자동 = the most recent, failed fights
/// included; 선택 = one picked with 「미터기 비교」, kept per character × raid (<see cref="Settings.MeterCompare"/>) until
/// 「자동으로」. Pure.</summary>
public static class MeterCompare
{
    public enum Kind { 자동, 선택 }

    /// <summary>The <see cref="Settings.MeterCompare"/> key of a character's raid.</summary>
    public static string Key(string character, string gate, string? difficulty) => $"{character}|{gate}|{difficulty}";

    /// <summary>The fight to compare with: <paramref name="chosen"/> while it is still a candidate, else the most recent one;
    /// null with no character, no gate or no candidate (then no line and no label).</summary>
    public static (string File, FightRecord Record, Kind How)? Pick(IReadOnlyList<(string File, FightRecord Record)> records,
        string? character, string? gate, string? difficulty, string? chosen)
    {
        if (string.IsNullOrEmpty(character) || string.IsNullOrEmpty(gate)) return null;
        var candidates = records.Where(x => x.Record.Character == character && x.Record.Gate == gate && x.Record.Difficulty == difficulty).ToList();
        if (chosen is not null && candidates.FirstOrDefault(x => x.File == chosen) is { Record: not null } picked) return (picked.File, picked.Record, Kind.선택);
        return candidates.OrderByDescending(x => x.Record.StartedAt).FirstOrDefault() is { Record: not null } last ? (last.File, last.Record, Kind.자동) : null;
    }

    /// <summary>My DPS per 전분 second in <paramref name="r"/>, in time order: the samples are on the 전분 timer, as the live graph.</summary>
    public static IReadOnlyList<(TimeSpan At, long Dps)> Ghost(FightRecord r) =>
        [.. r.Samples.Where(s => s.Name == r.Me).OrderBy(s => s.T).Select(s => (TimeSpan.FromSeconds(s.T), s.Dps))];

    /// <summary>A fight 「미터기 비교」 can pick: one with its character and gate known.</summary>
    public static bool CanChoose(FightRecord r) => !string.IsNullOrEmpty(r.Character) && !string.IsNullOrEmpty(r.Gate);

    static string KeyOf(FightRecord r) => Key(r.Character!, r.Gate!, r.Difficulty);

    /// <summary>Whether <paramref name="file"/> is the fight picked for its character's raid.</summary>
    public static bool IsChosen(IReadOnlyDictionary<string, string> picks, FightRecord r, string file) =>
        CanChoose(r) && picks.TryGetValue(KeyOf(r), out var f) && f == file;

    /// <summary>「미터기 비교」: <paramref name="file"/> picked for its character's raid, in a new map.</summary>
    public static Dictionary<string, string> Choose(IReadOnlyDictionary<string, string> picks, FightRecord r, string file) =>
        new(picks) { [KeyOf(r)] = file };

    /// <summary>「자동으로」: the raid's pick dropped, in a new map.</summary>
    public static Dictionary<string, string> Auto(IReadOnlyDictionary<string, string> picks, FightRecord r)
    {
        var next = new Dictionary<string, string>(picks);
        if (CanChoose(r)) next.Remove(KeyOf(r));
        return next;
    }

    /// <summary>The text beside the graph.</summary>
    public static string Label(Kind how) => $"비교 · {how}";
}

/// <summary>What the meter's graph draws for 비교: the saved fight's my-DPS and how it was picked.</summary>
public sealed record MeterGhost(IReadOnlyList<(TimeSpan At, long Dps)> Points, MeterCompare.Kind How);
