namespace GaugeOverlay.Core.Domain;

/// <summary>What happened at an event: a skill used (a slot going ready → cooldown), one of my buffs starting or ending, or a
/// bomb on the boss (암흑수류탄 / 빛나는, recorded while its switch is on) starting or ending.</summary>
public enum RecordEventKind { 사용, 버프시작, 버프끝, 암수시작, 암수끝, 대난투입장, 대난투종료 }

/// <summary>A player in a record (spec § D): name, class, role (null: did not share figures), and 1·2파티.</summary>
public sealed record RecordMember(string Name, int ClassId, MeterRole? Role, int Party);

/// <summary>A player's figures at fight second <paramref name="T"/> (피해량, or 조력 피해량 for a support).</summary>
public sealed record RecordSample(double T, string Name, long Damage, long Dps);

/// <summary>The boss at fight second <paramref name="T"/>: the damage taken (the meter's one boss figure) and its HP bar, when read.</summary>
public sealed record RecordBoss(double T, long Taken, long? Current, long? Max, long? Shield = null);

/// <summary>An event at fight second <paramref name="T"/> in <paramref name="Name"/>'s lane; <paramref name="What"/> is the
/// <see cref="SlotId"/> for 사용, the <see cref="BuffKind"/> for a buff, the <see cref="Vision.Bomb"/> for a bomb, 0 for 대난투 (the 그림자 gimmick: the boss bar gone, a fill bar showing the damage).</summary>
public sealed record RecordEvent(double T, string Name, RecordEventKind Kind, int What);

/// <summary>A skill as the fight saw it on a slot (skill-icons.plan.md): its name, its icon on Smilegate's CDN
/// ("gl_skill/gl_skill_01_30.png", Data.SkillIconSource) and its kind. Self-contained, so an import or a later library still draws it.</summary>
public sealed record RecordSkill(string Name, string Icon, SlotGroup Kind);

/// <summary>A saved fight (spec § D): when it started, the gate recognised (null: 알 수 없음), its length and phase starts in
/// fight seconds, the difficulty the dungeon panel showed, whether it was cleared (FightRecorder, FightVerdict, or set in 편집; null: 알 수 없음), who I am (my lane gets my skill icons), the players, 1 Hz samples and boss reads, the events, and 보관; since 0.19 the skill on each slot (<paramref name="Skills"/>: identified ones only), the
/// candidates of a slot left unsure (<paramref name="Unsure"/>, for 편집) and a [Test] fight's proposed class · 직업각인; since 0.20
/// the character's best 전투력 for the 직업각인 played and its item level from the last API lookup (<see cref="RecordPower"/>; null
/// without one, and in older records).</summary>
public sealed record FightRecord(
    DateTime StartedAt,
    string? Gate,
    double Duration,
    IReadOnlyList<double> Phases,
    string Me,
    int MyClass,
    string? MySubclass,
    IReadOnlyList<RecordMember> Members,
    IReadOnlyList<RecordSample> Samples,
    IReadOnlyList<RecordBoss> Boss,
    IReadOnlyList<RecordEvent> Events,
    bool Kept = false,
    string? MyClassName = null,
    string? Difficulty = null,
    bool? Cleared = null,
    bool AmsuOn = false,
    string? Character = null,
    IReadOnlyDictionary<SlotId, RecordSkill>? Skills = null,
    IReadOnlyDictionary<SlotId, IReadOnlyList<RecordSkill>>? Unsure = null,
    string? ProposedClass = null,
    double? CombatPower = null,
    double? ItemLevel = null)
{
    /// <summary>My share at the end (my last figure over the last boss figure; on a clear over all the boss's HP, the part its
    /// bar showed last counted as taken); null under the meter's dash rule.</summary>
    public double? MyShare =>
        Samples.LastOrDefault(s => s.Name == Me) is { Damage: >= MeterMath.DashUnder } mine && Boss.LastOrDefault() is { Taken: >= MeterMath.DashUnder } boss
            ? (double)mine.Damage / BossTotal(boss) : null;

    /// <summary>The boss's damage taken in all: as read, or on a clear the HP its bar showed last as well (the kill is often
    /// unread), and at least the bar's whole HP (a record that started a read late).</summary>
    long BossTotal(RecordBoss last) =>
        Cleared == true && last.Current is { } left ? Math.Max(last.Taken + left, last.Max ?? 0) : last.Taken;
}
