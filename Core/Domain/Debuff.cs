namespace GaugeOverlay.Core.Domain;

/// <summary>The raid whose debuffs are read (PLAN §4 디버프); each has its own icons. 없음: the debuff row isn't read.</summary>
public enum Raid { 없음, 모르둠 }

/// <summary>모르둠 (G3)'s debuffs: the blue and red sigils (your side, permanent), the lightning (urgent, timed), the
/// swap (timed: you and everyone near you change to the other colour) and the 결투자 (no timer read: <see cref="DuelMark"/>).
/// Appended in this order: the order is the reader's template order.</summary>
public enum DebuffKind { 파랑, 빨강, 번개, 교체, 결투자 }

/// <summary>A debuff icon found in the debuff row, with its timer when one could be read.</summary>
public sealed record DebuffReading(DebuffKind Kind, double? Seconds);

/// <summary>One debuff as last seen: <see cref="SeenAt"/>, and when its timer runs out (null: none read).</summary>
public sealed record DebuffMark(TimeSpan SeenAt, TimeSpan? EndsAt)
{
    /// <summary>Whole seconds left, rounded down as the game shows them; null with no timer.</summary>
    public int? Seconds(TimeSpan now) => EndsAt is { } end ? (int)Math.Max(0, (end - now).TotalSeconds) : null;
}

/// <summary>The 결투자 cycle: when it was first seen. The 4 → 0 countdown and the 30 s hold run from that instant on the
/// app's own clock (the game's "38초" is not read), whether or not the icon is still read after it. The 30 s are a hard
/// cap: the game's own debuff lasts longer, so a mark whose time is over stays (<see cref="Ended"/>) while the icon is read
/// without a gap, and only a read without it lets the next sighting start a new cycle.
/// <paramref name="InRow"/>: the latest read had the icon. <paramref name="Ended"/>: the 30 s passed with the icon still read.</summary>
public sealed record DuelMark(TimeSpan FirstSeenAt, bool InRow = true, bool Ended = false)
{
    /// <summary>The countdown shows 4, 3, 2, 1, 0: <see cref="CountSteps"/> steps of <see cref="CountStep"/>.</summary>
    public const int CountSteps = 5;
    public static readonly TimeSpan CountStep = TimeSpan.FromSeconds(1);
    /// <summary>How long after the first sighting the icon is kept (a cutscene hides it; it may come back, and the cycle
    /// does not restart).</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(30);

    /// <summary>True from the first sighting until <see cref="Hold"/> has passed.</summary>
    public bool IsActive(TimeSpan now) => now - FirstSeenAt < Hold;
}

/// <summary>What the 결투자 box shows: nothing, the countdown (<see cref="DuelPhase.Number"/> 4..0), or the icon alone.</summary>
public enum DuelStage { None, Countdown, Icon }

/// <summary>The 결투자 box at one instant (<see cref="At"/>). Pure: the renderer only draws it.</summary>
public readonly record struct DuelPhase(DuelStage Stage, int Number = 0)
{
    public static DuelPhase At(DuelMark? mark, TimeSpan now)
    {
        if (mark is null || now < mark.FirstSeenAt || !mark.IsActive(now)) return new(DuelStage.None);
        int step = (int)((now - mark.FirstSeenAt) / DuelMark.CountStep);
        return step < DuelMark.CountSteps ? new(DuelStage.Countdown, DuelMark.CountSteps - 1 - step) : new(DuelStage.Icon);
    }
}

/// <summary>What the debuff window draws: your debuffs (null = not on you) and the boss's red and blue counts.</summary>
public sealed record DebuffStatus(DebuffMark? Blue, DebuffMark? Red, DebuffMark? Lightning, DebuffMark? Swap, int? BossRed, int? BossBlue,
    TimeSpan BossSeenAt = default, DuelMark? Duelist = null)
{
    public static readonly DebuffStatus None = new(null, null, null, null, null, null);

    /// <summary>Your colour: the sigil seen last.</summary>
    public DebuffKind? Colour => (Blue, Red) switch
    {
        ({ } b, { } r) => b.SeenAt >= r.SeenAt ? DebuffKind.파랑 : DebuffKind.빨강,
        ({ }, null) => DebuffKind.파랑,
        (null, { }) => DebuffKind.빨강,
        _ => null,
    };
}

/// <summary>Who to take along into a swap so the boss's counts end up even (the user's rule: half of the total each).</summary>
public static class DebuffAdvice
{
    /// <summary>The colour and number of teammates to bring. You flip, and so does everyone you bring: with d = your
    /// colour's count less half the total, bring d − 1 of your own colour when d ≥ 1, else 1 − d of the other.
    /// (0 of your colour: go alone.) Null when a count is unknown.</summary>
    public static (DebuffKind Colour, int Count)? Bring(DebuffKind mine, int? red, int? blue)
    {
        if (red is not { } r || blue is not { } b || mine is not (DebuffKind.빨강 or DebuffKind.파랑)) return null;
        int own = mine == DebuffKind.빨강 ? r : b, d = own - (r + b) / 2;
        var other = mine == DebuffKind.빨강 ? DebuffKind.파랑 : DebuffKind.빨강;
        return d >= 1 ? (mine, d - 1) : (other, 1 - d);
    }
}
