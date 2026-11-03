namespace GaugeOverlay.Core.Domain;

/// <summary>A boss's name and difficulty as the record names it.</summary>
public sealed record BossBookEntry(string Name, string? Difficulty);

/// <summary>Naming records by the boss's max HP (spec § D): each gate and difficulty has its own, read exactly from the HP bar.
/// The book (Settings.BossBook) learns an HP the first time from the dungeon panel and names by it from then on, so a
/// misread name on a later fight doesn't matter; after a nerf or on another difficulty the HP is new and is learned again.
/// Pure.</summary>
public static class BossBook
{
    /// <summary>The record's name and difficulty: the book's for this HP, else the gate 자동 인식 knows, else the panel's.</summary>
    public static (string? Name, string? Difficulty) Resolve(IReadOnlyDictionary<long, BossBookEntry> book, long? hp, DungeonInfo? panel, string? knownGate) =>
        hp is { } h && book.TryGetValue(h, out var e) ? (e.Name, e.Difficulty ?? panel?.Difficulty)
            : (knownGate ?? panel?.Title, panel?.Difficulty);

    /// <summary>What to add for this HP: the panel's name and difficulty when the book has none yet (one there, maybe edited,
    /// stays); null when nothing is to be learned.</summary>
    public static BossBookEntry? ToLearn(IReadOnlyDictionary<long, BossBookEntry> book, long? hp, DungeonInfo? panel) =>
        hp is { } h && !book.ContainsKey(h) && panel is { Kind: DungeonKind.Raid, Title: { } title } ? new(title, panel.Difficulty) : null;

    /// <summary>전투 기록 편집: a fight whose name or difficulty was set by hand teaches its boss HP (replacing what the book had,
    /// so later fights are named alike); null when it has no HP read, no name, or the edit left both alone.</summary>
    public static (long Hp, BossBookEntry Entry)? Taught(FightRecord before, FightRecord after) =>
        FightHp(after.Boss) is { } hp && !string.IsNullOrWhiteSpace(after.Gate) && (after.Gate != before.Gate || after.Difficulty != before.Difficulty)
            ? (hp, new(after.Gate.Trim(), after.Difficulty)) : null;

    /// <summary>The fight's boss HP: the max read most often (a phase on another bar is shorter), or null when none was read.</summary>
    public static long? FightHp(IReadOnlyList<RecordBoss> boss) =>
        boss.Where(b => b.Max is not null).GroupBy(b => b.Max!.Value).MaxBy(g => g.Count())?.Key;
}
