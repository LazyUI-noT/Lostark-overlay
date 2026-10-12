using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Cooldown;

/// <summary>Decides which slots to read on each captured frame (PLAN §3 "Edge-watch schedule").
/// Has no clock of its own: every time is the frame's capture time, passed in.</summary>
public sealed class ReadScheduler
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(250);
    // A ready slot is read this often so its use flash (about 117 ms) is seen at least twice. With no input read, the
    // flash is the only sign of a use whose number shows later (a chain skill). 85 µs a read.
    private static readonly TimeSpan ReadyInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LongInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MidInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan EdgeWindow = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan KeyInterval = TimeSpan.FromSeconds(1.0 / 30);

    private readonly TimeSpan?[] lastRead = new TimeSpan?[SlotInfo.All.Length];
    private readonly TimeSpan?[] watchUntil = new TimeSpan?[SlotInfo.All.Length];
    private TimeSpan? keyPendingFrom;
    private TimeSpan? lastKeyServe;

    /// <summary>Slots to read on the frame captured at <paramref name="now"/>.</summary>
    public IReadOnlyList<SlotId> Due(TimeSpan now, IReadOnlyList<SlotState> states)
    {
        bool key = keyPendingFrom is { } pressed && now >= pressed
                   && (lastKeyServe is not { } served || now - served >= KeyInterval);
        if (key) (keyPendingFrom, lastKeyServe) = (null, now);

        var due = new List<SlotId>();
        foreach (var s in states)
            if (key || Watched(s.Slot, now) || IsDue(s, now))
                due.Add(s.Slot);
        return due;
    }

    /// <summary>A key was pressed: read every slot on the next frame captured at or after <paramref name="now"/>
    /// (cooldown slots to check for resets/spam, ready slots so a cast is seen without waiting for the 4 Hz check).
    /// Presses before that frame merge into one read; at most 30 such frames per second.</summary>
    public void RequestCooldownReads(TimeSpan now) => keyPendingFrom = now;

    /// <summary>Reads <paramref name="target"/> on every frame captured up to and including <paramref name="until"/>, whatever its
    /// phase (코어 빌드: a cast is known to change it). A later call extends the watch; an earlier time never shortens it.</summary>
    public void Watch(SlotId target, TimeSpan until) =>
        watchUntil[(int)target] = watchUntil[(int)target] is { } current && current > until ? current : until;

    private bool Watched(SlotId slot, TimeSpan now) => watchUntil[(int)slot] is { } until && now <= until;

    public void MarkRead(SlotId slot, TimeSpan at) => lastRead[(int)slot] = at;

    private bool IsDue(SlotState s, TimeSpan now)
    {
        var since = lastRead[(int)s.Slot] is { } last ? now - last : TimeSpan.MaxValue;
        switch (s.Phase)
        {
            case SlotPhase.Ready when s.EndsAt is not null && now - s.ChangedAt <= CooldownTracker.ResumeWindow:
                return true;                                               // just left a cooldown: real, or a flash?
            case SlotPhase.Ready:
                return since >= ReadyInterval;
            case SlotPhase.LongCooldown:
                return since >= LongInterval;
            case SlotPhase.Cooldown:
                var remaining = (s.EndsAt ?? now) - now;
                if (remaining <= TimeSpan.FromSeconds(1)) return true;     // last second: every frame
                double frac = remaining.TotalSeconds % 1;                  // whole-second edges at frac = 0
                return Math.Min(frac, 1 - frac) <= EdgeWindow.TotalSeconds || since >= MidInterval;
            default:
                return since >= IdleInterval;
        }
    }
}
