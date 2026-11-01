namespace GaugeOverlay.Core.Domain;

/// <summary>One character's chip on the 전투 기록 page: its automatic and 보관 fights and the latest one's start.</summary>
public sealed record RecordChip(string Character, int Auto, int Kept, DateTime Latest);

/// <summary>전투 기록 per character (records-timeline.design.md): a fight belongs to the class · 직업각인 it was played on, the
/// app's own character (not the in-game name). Pure.</summary>
public static class RecordCharacter
{
    public const string Unknown = "알 수 없음";

    /// <summary>"워로드 · 고독한기사"; the class alone without a 직업각인; <see cref="Unknown"/> without a class.</summary>
    public static string Of(FightRecord r) => Label(r.MyClassName, r.MySubclass);

    /// <summary>The label of a class and 직업각인 (also the character being played now, for the chips and 스킬 등록).</summary>
    public static string Label(string? className, string? subclass) =>
        string.IsNullOrWhiteSpace(className) ? Unknown
            : string.IsNullOrWhiteSpace(subclass) ? className : $"{className} · {subclass}";

    /// <summary>A chip per character with fights: <paramref name="current"/> first, then by latest fight, <see cref="Unknown"/> last.</summary>
    public static IReadOnlyList<RecordChip> Chips(IEnumerable<FightRecord> records, string? current) =>
        [.. records.GroupBy(Of)
            .Select(g => new RecordChip(g.Key, g.Count(r => !r.Kept), g.Count(r => r.Kept), g.Max(r => r.StartedAt)))
            .OrderBy(c => c.Character == Unknown).ThenByDescending(c => c.Character == current).ThenByDescending(c => c.Latest)];
}
