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
        st is { Phase: SlotPhase.Ready, Estimated: false } && (int)(now - st.ChangedAt).TotalSeconds is var n and >= 1
            ? Math.Min(n, MaxReadyCount) : null;
    public const int MaxReadyCount = 29;

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
