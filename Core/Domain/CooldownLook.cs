namespace GaugeOverlay.Core.Domain;

/// <summary>How a cooling box shows the time gone by (오버레이 tab "쿨타임 표시"): a clockwise sweep, a fill that refills
/// one step brighter, or a fill in full colour with slashes (which stay until Ready, so an almost-full box never looks
/// ready).</summary>
public enum CooldownStyle { Sweep, Fill, FillHatched }

/// <summary>The edge a fill grows from.</summary>
public enum FillFrom { Bottom, Top, Left, Right }

/// <summary>The colours of a box on a seconds cooldown; OverlayRenderer draws the shape.</summary>
/// <param name="Base">Lightness of the box colour under the refilled part (1 = full colour; see OverlayRenderer.Level).</param>
/// <param name="Grey">The base is drawn grey (a stack skill with no use left).</param>
/// <param name="Refill">Lightness of the refilled part in the box colour; null = none, a white line marks the progress.</param>
/// <param name="Hatched">Slashes over the refilled part.</param>
public readonly record struct CooldownLook(double Base, bool Grey, double? Refill, bool Hatched)
{
    public const double CooldownLevel = 0.30;   // lightness while on cooldown / unknown
    public const double SweepLevel = 0.55;      // lightness of the refilled part

    /// <summary>True when progress is a white line over a full-colour box instead of a refilled part.</summary>
    public bool Line => Refill is null;

    /// <summary>A stack skill with a use left can be cast now: full colour, a white line for the recharge. With no use
    /// left it is grey and refills in its colour. Anything else is dim and refills one step brighter, or in full colour
    /// under slashes.</summary>
    public static CooldownLook For(SlotState st, CooldownStyle style) =>
        st.Stacked ? new(1, false, null, false)
        : style == CooldownStyle.FillHatched ? new(CooldownLevel, st.StackSkill, 1, true)
        : new(CooldownLevel, st.StackSkill, SweepLevel, false);

    /// <summary>준비 후 경과 시간: whole seconds <paramref name="st"/> has been ready, from 1 and held at 29; null while it isn't
    /// ready and read (cooling, greyed, unreadable) and in its first second.</summary>
    public static int? ReadyCount(SlotState st, TimeSpan now) =>
        st is { Phase: SlotPhase.Ready, Estimated: false } ? Seconds(now - st.ChangedAt) : null;
    public const int MaxReadyCount = 29;

    /// <summary>Whole seconds of <paramref name="span"/>, from 1 and held at 29; null under 1 s.</summary>
    internal static int? Seconds(TimeSpan span) => (int)span.TotalSeconds is var n and >= 1 ? Math.Min(n, MaxReadyCount) : null;

    /// <summary>쿨타임 중 숨기기: a box set to hide is gone while a read cooldown runs (seconds left, or minutes), and
    /// back when it is ready, its time is up, or the state is unsure (unread, estimated, greyed). A stack skill with a
    /// use left stays, as does a box showing its held count (<paramref name="held"/>, 사용 후 3초 유지).</summary>
    public static bool Hides(SlotState st, TimeSpan now, bool held) => !held && !st.Estimated && !st.Stacked && st.Phase switch
    {
        SlotPhase.Cooldown => st.Remaining(now) > TimeSpan.Zero,
        SlotPhase.LongCooldown => true,
        _ => false,
    };

    /// <summary>The part of the box (<paramref name="x"/>, <paramref name="y"/>, <paramref name="w"/>, <paramref name="h"/>)
    /// filled at progress <paramref name="p"/> (clamped to 0..1), growing from <paramref name="from"/>.</summary>
    public static (double X, double Y, double W, double H) FillRect(double x, double y, double w, double h, double p, FillFrom from)
    {
        p = Math.Clamp(p, 0, 1);
        return from switch
        {
            FillFrom.Bottom => (x, y + h * (1 - p), w, h * p),
            FillFrom.Top => (x, y, w, h * p),
            FillFrom.Left => (x, y, w * p, h),
            _ => (x + w * (1 - p), y, w * p, h),
        };
    }
}

/// <summary>사용 후 경과 시간 유지: how long a box was ready before the skill was used, shown for <see cref="Hold"/> after
/// the use. Stepped once per frame with the box's state. Only a ready period read from its start (entered from a read
/// cooldown, not estimated, not unread on the way) counts; becoming ready again ends it.</summary>
/// <param name="Cooling">The last state was a read cooldown (or greyed), so a Ready after it starts a real ready period.</param>
/// <param name="ReadySince">When the current, trusted ready period began.</param>
/// <param name="Count">The seconds held before the last use (1–29), shown until <paramref name="Until"/>.</param>
public readonly record struct HeldCount(bool Cooling = false, TimeSpan? ReadySince = null, int? Count = null, TimeSpan Until = default)
{
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(3);

    public HeldCount Next(SlotState st) => st switch
    {
        // A ready period other than the one seen starting (its cooldown fell between frames not drawn) isn't trusted.
        { Phase: SlotPhase.Ready, Estimated: false } => new(false, Cooling || ReadySince == st.ChangedAt ? st.ChangedAt : null),
        { Phase: SlotPhase.Cooldown or SlotPhase.LongCooldown } when ReadySince is { } since =>
            new(true, null, CooldownLook.Seconds(st.ChangedAt - since), st.ChangedAt + Hold),
        { Phase: SlotPhase.Cooldown or SlotPhase.LongCooldown or SlotPhase.Unavailable, Estimated: false } => this with { Cooling = true, ReadySince = null },
        _ => new(),
    };

    public int? Shown(TimeSpan now) => now < Until ? Count : null;
}
