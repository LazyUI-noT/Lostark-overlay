namespace GaugeOverlay.Core.Domain;

/// <summary>The 줄 수 창's count as shown: <see cref="Shown"/> (null: none yet, or lost), a different read waiting for a second
/// to agree (<see cref="Pending"/>), when a read last came in, the alert that fired last (its threshold and time), and the
/// lowest count shown in this pull (alerts above it have fired: a shield lifting the count doesn't fire them again).</summary>
public sealed record BarCountState(int? Shown, int? Pending, TimeSpan SeenAt, int? FiredAt, TimeSpan FiredTime, int? Low = null)
{
    public static readonly BarCountState None = new(null, null, TimeSpan.Zero, null, TimeSpan.Zero);
}

/// <summary>The boss bar count's alerts (spec § B): the count goes down through a fight; an alert at target t with offset o
/// fires when the shown count reaches t + o from above (also when a read skips past it), and glows for N seconds or until the
/// count is N bars past. A count that goes back up (a wipe and a new pull, a new phase) re-arms it. Pure.</summary>
public static class BarAlert
{
    /// <summary>Reads missing this long clear the count (the bar is gone: a cutscene, the fight over).</summary>
    public static readonly TimeSpan Lost = TimeSpan.FromSeconds(3);

    /// <summary>A count rising this many bars or fewer is a shield added to the bar (종막 2-2: +10 at 305), not a new phase.</summary>
    public const int ShieldRise = 15;

    /// <summary>The counts that fire: each alert plus <paramref name="offset"/>, high to low, none below 1.</summary>
    public static IReadOnlyList<int> Thresholds(IReadOnlyList<int> alerts, int offset) =>
        [.. alerts.Select(a => a + offset).Where(a => a >= 1).Distinct().OrderDescending()];

    /// <summary>The state after a read at <paramref name="now"/> (null: not read). A new value shows only once two reads in a
    /// row agree, so a one-frame misread (a cast effect over the digits) neither shows nor fires.</summary>
    public static BarCountState Update(BarCountState s, int? read, TimeSpan now, IReadOnlyList<int> thresholds)
    {
        if (read is not { } r) return now - s.SeenAt > Lost ? s with { Shown = null, Pending = null, FiredAt = null } : s;   // a wipe: the re-pull re-arms
        if (r == s.Shown) return s with { Pending = null, SeenAt = now };
        if (s.Pending != r) return s with { Pending = r, SeenAt = now };
        var next = s with { Shown = r, Pending = null, SeenAt = now };
        if (s.Shown is not { } was) return next with { Low = r };   // the first value: joined mid-fight, nothing crossed
        if (r > was)   // a shield (a few bars: 종막 2-2 +10) keeps what fired; a bigger jump (a new phase) re-arms
            return r - was <= ShieldRise ? next : (s.FiredAt is { } f && r > f ? next with { FiredAt = null } : next) with { Low = r };
        int low = Math.Min(was, s.Low ?? was);   // an alert fires once per pull: only under the lowest count yet
        var crossed = thresholds.Where(a => low > a && a >= r).ToList();
        next = next with { Low = Math.Min(low, r) };
        return crossed.Count > 0 ? next with { FiredAt = crossed.Min(), FiredTime = now } : next;
    }

    /// <summary>Whether the window glows at <paramref name="now"/>: an alert fired and is within its seconds, or the count is
    /// not yet <paramref name="bars"/> past it.</summary>
    public static bool Glowing(BarCountState s, GlowUntil until, int seconds, int bars, TimeSpan now) =>
        s.FiredAt is { } at && (until == GlowUntil.Seconds
            ? now - s.FiredTime <= TimeSpan.FromSeconds(seconds)
            : s.Shown is { } shown && shown > at - bars && shown <= at);   // a shield lifting it back over the alert: no glow
}
