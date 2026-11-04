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

    /// <summary>A fight saved in 테스트 모드 (footage, not the user's play; user, 2026-11-05): its class until 편집 sets one. The class
    /// id and 직업각인 stay the active character's, so its timeline still has skill icons.</summary>
    public const string TestClass = "[Test]";

    /// <summary>"워로드 · 고독한기사": the class · 직업각인 the fight was played on.</summary>
    public static string ClassOf(FightRecord r) => Label(r.MyClassName, r.MySubclass);

    /// <summary>The label of a class and 직업각인; the class alone without a 직업각인 (and <see cref="TestClass"/> always alone);
    /// <see cref="NoClass"/> without a class.</summary>
    public static string Label(string? className, string? subclass) =>
        string.IsNullOrWhiteSpace(className) ? NoClass
            : string.IsNullOrWhiteSpace(subclass) || className == TestClass ? className : $"{className} · {subclass}";

    /// <summary>The class · 직업각인 saved for a character by name: 숙제's record of it, else its latest fight that has one; null when
    /// neither knows. For 편집, which offers it and leaves it changeable (user, 2026-11-04).</summary>
    public static string? SavedClass(string? name, IEnumerable<HomeworkCharacter> characters, IEnumerable<FightRecord> records)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (characters.FirstOrDefault(c => c.Name == name && !string.IsNullOrWhiteSpace(c.ClassName) && !string.IsNullOrWhiteSpace(c.Subclass)) is { } known)
            return Label(known.ClassName, known.Subclass);
        return records.Where(r => r.Character == name && !string.IsNullOrWhiteSpace(r.MyClassName) && r.MyClassName != TestClass && !string.IsNullOrWhiteSpace(r.MySubclass))
            .OrderByDescending(r => r.StartedAt).Select(ClassOf).FirstOrDefault();
    }

    /// <summary>A chip per character with fights: <paramref name="current"/> first, then by latest fight, <see cref="Unknown"/> last.</summary>
    public static IReadOnlyList<RecordChip> Chips(IEnumerable<FightRecord> records, string? current) =>
        [.. records.GroupBy(Of)
            .Select(g => new RecordChip(g.Key, g.Count(r => !r.Kept), g.Count(r => r.Kept), g.Max(r => r.StartedAt)))
            .OrderBy(c => c.Character == Unknown).ThenByDescending(c => c.Character == current).ThenByDescending(c => c.Latest)];
}
