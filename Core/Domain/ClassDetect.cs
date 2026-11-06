using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>클래스 인식 (class-detect.plan.md). 끄기: the class only by picking. 수동: on request (스킬창 다시 맞추기). 자동: 수동, and a
/// check whenever the skill bar comes back outside a dungeon.</summary>
public enum ClassDetectMode { 끄기, 수동, 자동 }

/// <summary>A class read from the skill bar, to switch to. <see cref="Subclass"/> null: the library doesn't say.</summary>
public sealed record ClassPick(string Class, string? Subclass);

/// <summary>What 클래스 인식 does with a read. Unsure always keeps the class: never 기본 on its own, never a guess. Pure.</summary>
public static class ClassDetect
{
    /// <summary>Tries for one return of the bar, <see cref="RetryAfter"/> apart: then it waits for the next return.</summary>
    public const int MaxTries = 3;
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

    /// <summary>The bar came back: read the class? 자동 only, not in a dungeon or raid, not in 테스트 모드 (a video isn't a character)
    /// unless the dev flag asks for it.</summary>
    public static bool OnBarBack(ClassDetectMode mode, bool inDungeon, bool testMode, bool devFlag) =>
        mode == ClassDetectMode.자동 && !inDungeon && (!testMode || devFlag);

    /// <summary>The class to switch to, or null to keep <paramref name="active"/>. In colour the 일반 slots must confirm; on a greyed
    /// bar they can't, so <see cref="SkillClassFinder.Find"/>'s stricter greyed gate stands alone.</summary>
    public static ClassPick? Switch(SkillClassFinder.Found found, SkillClassFinder.Verdict slots, string? active, bool greyed) =>
        found.Class is { } cls && cls != active && (greyed || slots == SkillClassFinder.Verdict.Confirmed) ? new(cls, found.Subclass) : null;

    /// <summary>Worth another read: it couldn't tell, or it saw another class it couldn't confirm.</summary>
    public static bool Unsure(SkillClassFinder.Found found, ClassPick? pick, string? active) =>
        pick is null && found.Class != active;
}
