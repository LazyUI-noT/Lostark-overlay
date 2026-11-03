namespace GaugeOverlay.Core.Domain;

/// <summary>One character's chip on the 전투 기록 page: its automatic and 보관 fights and the latest one's start.</summary>
public sealed record RecordChip(string Character, int Auto, int Kept, DateTime Latest);

/// <summary>전투 기록 per character (homework.plan.md): a fight belongs to the in-game character it was played on, as read from
/// the game menu (CharacterWatch), or to <see cref="Unknown"/>. Its class · 직업각인 still picks the skill icons. Pure.</summary>
public static class RecordCharacter
{
    public const string Unknown = "캐릭터 불명";

    /// <summary>A fight without a class (saved before the app knew one).</summary>
    public const string NoClass = "알 수 없음";

    /// <summary>The fight's character; <see cref="Unknown"/> when none was known.</summary>
    public static string Of(FightRecord r) => string.IsNullOrWhiteSpace(r.Character) ? Unknown : r.Character;

    /// <summary>"워로드 · 고독한기사": the class · 직업각인 the fight was played on.</summary>
    public static string ClassOf(FightRecord r) => Label(r.MyClassName, r.MySubclass);

    /// <summary>The label of a class and 직업각인; the class alone without a 직업각인; <see cref="NoClass"/> without a class.</summary>
    public static string Label(string? className, string? subclass) =>
        string.IsNullOrWhiteSpace(className) ? NoClass
            : string.IsNullOrWhiteSpace(subclass) ? className : $"{className} · {subclass}";

    /// <summary>A chip per character with fights: <paramref name="current"/> first, then by latest fight, <see cref="Unknown"/> last.</summary>
    public static IReadOnlyList<RecordChip> Chips(IEnumerable<FightRecord> records, string? current) =>
        [.. records.GroupBy(Of)
            .Select(g => new RecordChip(g.Key, g.Count(r => !r.Kept), g.Count(r => r.Kept), g.Max(r => r.StartedAt)))
            .OrderBy(c => c.Character == Unknown).ThenByDescending(c => c.Character == current).ThenByDescending(c => c.Latest)];
}
