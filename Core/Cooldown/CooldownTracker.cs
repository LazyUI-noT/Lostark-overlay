using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Cooldown;

/// <summary>The per-slot decision tree of PLAN §3 "Per-slot cooldown logic": one reading in, the next state out.
/// Pure and deterministic. The game rounds up, so a reading of "N s" at t means EndsAt is in (t+N−1, t+N].</summary>
public static class CooldownTracker
{
    /// <summary>While the fraction of a second is unknown (after a cast or a reduction), EndsAt is kept just past
    /// the lower bound, so the predicted edge stays inside the scheduler's per-frame window until the screen shows it.</summary>
    public static readonly TimeSpan ChaseLead = TimeSpan.FromMilliseconds(50);

    /// <summary>A digit change seen this close before the predicted edge is that edge (locked to the frame);
    /// further off, the timing is unknown and is found again at the next edge.</summary>
    public static readonly TimeSpan EdgeTolerance = TimeSpan.FromMilliseconds(40);

    /// <summary>A Ready read straight out of a cooldown keeps that cooldown's EndsAt and Total this long. PLAN §3 wants
    /// 2 no-digit frames before Ready, but a reset must show on its own frame (333.mp4), so Ready is set at once and
    /// undone instead: digits matching the old prediction (±1 s) within this window mean the no-digit frame was a
    /// flash or misread, and the cooldown resumes without a new cast.</summary>
    public static readonly TimeSpan ResumeWindow = TimeSpan.FromMilliseconds(150);

    /// <summary>How long a stack skill's recharge value must hold after its re-use lockout before it is believed
    /// (a flash misread lasted 50 ms).</summary>
    public static readonly TimeSpan LockoutConfirm = TimeSpan.FromMilliseconds(100);

    /// <summary>After a use flash, Ready reads are the chain's next part or the moment before the number shows
    /// (chainSkill.mp4: up to 1.4 s from the first flash): ignored this long after the last flash.</summary>
    public static readonly TimeSpan UseHold = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    /// <summary>Next state of the slot after reading <paramref name="r"/> (taken at <c>r.At</c>). The gold frame is
    /// what this frame shows; a flash hides it (kept), a slot that can't be read shows none. A stack count marks the slot
    /// as a stack skill until it can't be read (a loading screen, perhaps before another character).</summary>
    public static SlotState Update(SlotState prev, Reading r) =>
        (Used(prev, r) ?? Next(prev, r)) with
        {
            Gold = r.Kind switch { ReadKind.CooldownIllegible => prev.Gold, ReadKind.Unreadable => false, _ when r.Flash => prev.Gold, _ => r.Gold },
            StackSkill = r.Kind != ReadKind.Unreadable && (prev.StackSkill || r.Stacked),
        };

    /// <summary>A use flash on a ready slot (not one just out of a cooldown, whose end may flash) starts a cooldown without
    /// a number; an unreadable read carries no flash (a bright scene behind a hidden HUD). until the number shows, further flashes extend it and anything else within <see cref="UseHold"/> is ignored.
    /// Null when the reading goes through <see cref="Next"/>.</summary>
    private static SlotState? Used(SlotState prev, Reading r)
    {
        if (r.Kind is ReadKind.Seconds or ReadKind.Minutes) return null;
        if (r.Flash && prev is { Phase: SlotPhase.Ready, EndsAt: null })
            return new SlotState(prev.Slot, SlotPhase.Cooldown, null, null, false, r.At, UsedAt: r.At);
        if (prev.UsedAt is not { } used) return null;
        if (r.Flash) return prev with { UsedAt = r.At, Estimated = false };
        // Unread: estimated like any cooldown, so Advance ends it UseHold after the last flash.
        return r.Kind == ReadKind.Unreadable || r.At - used >= UseHold ? null : prev with { Estimated = false };
    }

    private static SlotState Next(SlotState prev, Reading r) => r.Kind switch
    {
        // Only a read sets Ready while readable; this also covers resets (333.mp4).
        ReadKind.Ready => prev.Phase == SlotPhase.Ready ? prev with { Estimated = false }
            : prev.Phase == SlotPhase.Cooldown ? Enter(prev, SlotPhase.Ready, r.At) with { EndsAt = prev.EndsAt, Total = prev.Total }
            : Enter(prev, SlotPhase.Ready, r.At),
        ReadKind.Minutes => prev.Phase == SlotPhase.LongCooldown && r.Value <= prev.Minutes
            ? prev with { Minutes = r.Value, Estimated = false }
            : Enter(prev, SlotPhase.LongCooldown, r.At) with { Minutes = r.Value, CastSeen = prev.Phase != SlotPhase.Unknown },
        ReadKind.Seconds => Seconds(prev, r) with { Stacked = r.Stacked },
        // Digits present but illegible: keep everything. In the last second this is the 99% hold.
        ReadKind.CooldownIllegible => prev,
        ReadKind.Unavailable => prev.Phase == SlotPhase.Unavailable ? prev with { Estimated = false } : Enter(prev, SlotPhase.Unavailable, r.At),
        _ => prev.Phase == SlotPhase.Unknown ? prev : prev with { Estimated = true },
    };

    /// <summary>Counts an Estimated cooldown down on the recorded time: past EndsAt it becomes Ready (still Estimated).
    /// A stack skill's lockout (no EndsAt) lasts at most its "1s", and a use is left after it. Readable slots never
    /// become Ready from the timer.</summary>
    public static SlotState Advance(SlotState s, TimeSpan now) =>
        s is { Estimated: true, Phase: SlotPhase.Cooldown } && (s.EndsAt ?? (s.UsedAt is { } used ? used + UseHold : s.ChangedAt + Second)) is var end && end <= now
            ? new SlotState(s.Slot, SlotPhase.Ready, null, null, true, end, StackSkill: s.StackSkill)
            : s;

    private static SlotState Seconds(SlotState prev, Reading r)
    {
        var (value, t) = (r.Value, r.At);
        var hi = t + TimeSpan.FromSeconds(value);
        var lo = hi - Second;

        // A stack skill shows "1s" beside its count for a moment after each cast (the re-use lockout), then its
        // recharge timer (live_test2 E, live test Q). That "1s" isn't the cooldown: the cast is recorded with no
        // EndsAt (no sweep), and the recharge timer that follows continues it below.
        // The cast flash can streak "1s" into "11" for a few frames (live test Q), so the recharge value must hold for
        // LockoutConfirm first. The count comes ~150 ms after the "1s" (live_test2 E 4.08 s, live test Q 3.05 s), so a
        // slot already known to stack takes the "1s" as the lockout without it: otherwise it was a 1 s cooldown whose
        // end was chased for half a second, the sweep jumping near full, then restarting at the timer (user, 2026-10-13).
        bool lockout = prev is { Phase: SlotPhase.Cooldown, EndsAt: null };
        if (value == 1 && (r.Stacked || prev.StackSkill) && (prev.Phase != SlotPhase.Cooldown || lockout))
            return new SlotState(prev.Slot, SlotPhase.Cooldown, null, null, false, lockout ? prev.ChangedAt : t);
        if (lockout && prev.UsedAt is null && !(prev.Pending is { } seen && seen.Value == value && t - FirstSeen(seen) >= LockoutConfirm))
            return prev.Pending is { } same && same.Value == value ? prev : prev with { Pending = (value, lo + ChaseLead) };

        // It was a flash: resume. A cooldown first seen midway has no Total and keeps none; its cast time is unknown, but
        // it was before the flash (so it isn't a new cast).
        if (prev is { Phase: SlotPhase.Ready, EndsAt: { } old } && t - prev.ChangedAt <= ResumeWindow
            && Math.Abs(value - Math.Ceiling((old - t).TotalSeconds)) <= 1)
            prev = prev with { Phase = SlotPhase.Cooldown, ChangedAt = prev.Total is { } total ? old - total : prev.ChangedAt - ResumeWindow };

        // New cast (or first seconds after "Nm", or the number after a use flash): the shown value's lower bound is ≥ 1 s
        // above the prediction, AND that prediction has already elapsed, AND the icon flashes or the reading before
        // agreed with it (a recast whose flash wasn't seen). A still-future EndsAt can't legitimately jump later (a reset shows Ready on its own
        // frame before the recast, 333.mp4), and an elapsed one is still on screen as its last "1s", which a coloured
        // streak beside it can turn into 11 (sBug.mp4): neither restarts on a single noisy over-read.
        if (prev.Phase != SlotPhase.Cooldown || prev.EndsAt is not { } end || (end <= t && lo - end >= Second && (r.Flash || Agrees(prev, value, lo, hi))))
        {
            // After "Nm" the cast was at the start of the minutes phase: the sweep covers the whole cooldown, as the
            // game's does (timed from this reading instead, a ~70 s 초각스 swept ~10 s behind).
            var castAt = lockout || prev.Phase == SlotPhase.LongCooldown ? prev.ChangedAt : t;
            var cast = new SlotState(prev.Slot, SlotPhase.Cooldown, null, null, false, castAt);
            // Never read before (app start, taskbar sliding off), or minutes first seen midway: the cast wasn't seen,
            // so there is no length to sweep over. The timer is tracked; the sweep waits for the next cast (Lock keeps
            // Total unknown).
            bool unseen = prev.Phase == SlotPhase.Unknown || prev is { Phase: SlotPhase.LongCooldown, CastSeen: false };
            return unseen ? cast with { EndsAt = lo + ChaseLead } : Lock(cast, lo + ChaseLead);
        }

        // More than a second off a still-running prediction, either way: held until the next digit (another value)
        // agrees with it. A single misread ("11s" as 1, a stack skill's clock hand read as a leading 1) then neither
        // restarts nor stretches the sweep; a real reduction shows one digit later. Ready (resets) is not held.
        if (Off(end, lo, hi))
        {
            if (!Agrees(prev, value, lo, hi))
                return prev with { Pending = prev.Pending is { } same && same.Value == value ? same : (value, lo + ChaseLead), Estimated = false };
        }
        prev = prev with { Pending = null };

        if (end > hi)
            end = end - hi <= EdgeTolerance ? hi : lo + ChaseLead;   // expected edge : reduction (jumped down)
        else if (end <= lo)
            end = lo + ChaseLead;                                    // edge predicted but not on screen yet
        return Lock(prev, end);
    }

    private static bool Off(TimeSpan end, TimeSpan lo, TimeSpan hi) => lo - end >= Second || end - hi >= Second;

    /// <summary>The held reading is another value implying the same end as this one.</summary>
    private static bool Agrees(SlotState prev, int value, TimeSpan lo, TimeSpan hi) =>
        prev.Pending is { } p && p.Value != value && !Off(p.EndsAt, lo, hi);

    /// <summary>When a pending value was first read: it was stored as that reading's lo + ChaseLead.</summary>
    private static TimeSpan FirstSeen((int Value, TimeSpan EndsAt) p) => p.EndsAt - ChaseLead - TimeSpan.FromSeconds(p.Value - 1);

    private static SlotState Enter(SlotState prev, SlotPhase phase, TimeSpan t) => new(prev.Slot, phase, null, null, false, t);

    /// <summary>The sweep's 100% is the time from the cast to EndsAt, so it is refined whenever EndsAt is. A running
    /// cooldown without a Total was first seen midway (cast unseen) and stays without one.</summary>
    private static SlotState Lock(SlotState s, TimeSpan endsAt) =>
        s with { EndsAt = endsAt, Total = s is { EndsAt: not null, Total: null } ? null : endsAt - s.ChangedAt, Estimated = false };
}
