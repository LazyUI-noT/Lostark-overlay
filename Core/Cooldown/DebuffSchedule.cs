using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Cooldown;

/// <summary>What the debuff row read last: how many boxes from the left hold a debuff, what each is (kept between
/// identifications), each timed box's seconds (null: none or unread), and when each pass last ran. Immutable.</summary>
public sealed record DebuffSlots(int Filled, IReadOnlyList<DebuffKind?> Kinds, IReadOnlyList<double?> Seconds,
    TimeSpan FramesAt, TimeSpan ClassifiedAt, TimeSpan TimersAt)
{
    public static readonly DebuffSlots None = new(0, [], [], TimeSpan.MinValue, TimeSpan.MinValue, TimeSpan.MinValue);
}

/// <summary>Which read the engine does this step.</summary>
public enum DebuffPass { None, Frames, Timers }

/// <summary>Debuff cadence v2 (the user's spec, 2026-10-30). Frame pass 5/s: the filled boxes and the first empty one are
/// checked (DebuffReader.Filled, cheap), the icons identified only every <see cref="ClassifyEvery"/> or at once when the
/// filled count changed (the row shifted) or a timed box's timer wasn't read, and the timed boxes' timers read. Timer pass
/// 10/s in between: only the timers of timed boxes under <see cref="FastUnder"/>, each captured alone. Pure.</summary>
public static class DebuffSchedule
{
    public static readonly TimeSpan FrameEvery = TimeSpan.FromMilliseconds(200), ClassifyEvery = TimeSpan.FromMilliseconds(500),
        FastEvery = TimeSpan.FromMilliseconds(100);

    /// <summary>A timer below this many seconds is read 10 times a second.</summary>
    public const double FastUnder = 2;

    /// <summary>At least <paramref name="every"/> since <paramref name="last"/> (MinValue: never, so due).</summary>
    static bool Elapsed(TimeSpan now, TimeSpan last, TimeSpan every) => last == TimeSpan.MinValue || now - last >= every;

    public static DebuffPass Due(DebuffSlots s, TimeSpan now) =>
        Elapsed(now, s.FramesAt, FrameEvery) ? DebuffPass.Frames
        : FastTimers(s).Count > 0 && Elapsed(now, s.TimersAt, FastEvery) ? DebuffPass.Timers
        : DebuffPass.None;

    /// <summary>The boxes the frame pass captures: the filled ones and the next.</summary>
    public static int Boxes(DebuffSlots s) => Math.Min(Geometry.DebuffBoxCount, s.Filled + 1);

    public static bool Timed(DebuffKind? kind) => kind is DebuffKind.번개 or DebuffKind.교체 or DebuffKind.결투자;

    /// <summary>The boxes whose timer the timer pass reads: timed, read last under <see cref="FastUnder"/>; and the 결투자 always, its
    /// 4 → 0 dated from the tick it sees (DebuffTracker.DuelTimer: at 5/s up to 0.2 s late, review 2026-11-01).</summary>
    public static IReadOnlyList<int> FastTimers(DebuffSlots s) =>
        [.. Enumerable.Range(0, s.Filled).Where(i => s.Kinds[i] == DebuffKind.결투자 || Timed(s.Kinds[i]) && s.Seconds[i] is < FastUnder)];

    /// <summary>Whether the frame pass that found <paramref name="filled"/> boxes identifies their icons again.</summary>
    public static bool Classify(DebuffSlots s, int filled, TimeSpan now) =>
        filled != s.Filled || Elapsed(now, s.ClassifiedAt, ClassifyEvery)
        || Enumerable.Range(0, s.Filled).Any(i => Timed(s.Kinds[i]) && s.Seconds[i] is null);

    /// <summary>After a frame pass: <paramref name="kinds"/> the boxes identified now, or null when they weren't (kept); the
    /// seconds read on the timed boxes.</summary>
    public static DebuffSlots Frames(DebuffSlots s, int filled, IReadOnlyList<DebuffKind?>? kinds, IReadOnlyList<double?> seconds, TimeSpan now) =>
        new(filled, kinds ?? s.Kinds, seconds, now, kinds is null ? s.ClassifiedAt : now, now);

    /// <summary>After a timer pass: the seconds read per box (null: unread, which asks for an identification next frame pass).</summary>
    public static DebuffSlots Timers(DebuffSlots s, IReadOnlyDictionary<int, double?> seconds, TimeSpan now) =>
        s with { Seconds = [.. s.Seconds.Select((v, i) => seconds.TryGetValue(i, out var read) ? read : v)], TimersAt = now };
}
