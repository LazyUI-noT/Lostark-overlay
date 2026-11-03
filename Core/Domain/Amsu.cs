using System.Collections.Immutable;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>One bomb on the boss: when it ends, when its icon was last seen, and a read off the countdown waiting for more that
/// agree (<paramref name="Agreeing"/> so far: a re-throw, a late start, or a misread to ignore).</summary>
public sealed record BombTimer(TimeSpan EndsAt, TimeSpan LastSeen, double? Pending = null, TimeSpan PendingAt = default, int Agreeing = 0,
    int? Slot = null);

/// <summary>A bomb that ended: when, and the slot its icon was in (its fading icon may read as the other kind there).</summary>
public sealed record EndedBomb(TimeSpan At, int? Slot);

/// <summary>What happened to a bomb between two states: it appeared, it was thrown again (its end jumped later), or it ended.</summary>
public enum AmsuChange { 시작, 재시작, 끝 }

/// <summary>A bomb's change and its seconds left after it (0 when it ended).</summary>
public sealed record AmsuEdge(Bomb Kind, AmsuChange Change, double Left);

/// <summary>The bombs on the boss and the boss they're on (its max HP: another boss clears them).</summary>
public sealed record AmsuState(ImmutableDictionary<Bomb, BombTimer> Up, long? BossMaxHp, ImmutableDictionary<Bomb, EndedBomb>? Ended = null,
    long? OtherBoss = null)
{
    public static readonly AmsuState None = new(ImmutableDictionary<Bomb, BombTimer>.Empty, null);
}

/// <summary>The 암수 countdown (amsu.design.md): reads of the boss row into the bomb shown. A bomb's time only counts down, or jumps
/// back near its full length when thrown again; misreads repeat the same way (a lost tens digit reads low every time, replay
/// 2026-11-01), so: a new bomb starts at its full time unless read <see cref="Fresh"/> s from it; a read higher than the countdown
/// counts only as a fresh throw, read twice; a lower one only after <see cref="LowerReads"/> agreeing reads (seen mid-bomb); one
/// over the bomb's length never. A bomb out of view (pushed behind "+N", the bar hidden) counts on to 0. Pure.</summary>
public static class Amsu
{
    const double Off = 2, SeenFor = 1.0, Fresh = 3, JustThrown = 1.5;
    const int UpperReads = 2, LowerReads = 4;
    static readonly TimeSpan Linger = TimeSpan.FromSeconds(2);

    /// <summary>A new bomb whose seconds are the other kind's countdown: that icon, read as the other kind for a moment.</summary>
    static bool OtherKindsTime(ImmutableDictionary<Bomb, BombTimer> up, BombRead read, TimeSpan now) =>
        read.Seconds is { } sec && up.Any(b => b.Key != read.Kind
            && Tens(sec).Any(c => Math.Abs((b.Value.EndsAt - now).TotalSeconds - c) <= Off));   // its tens may be lost too

    // As read, never restored: right after an end the fading icon's "0.4초" reads "4" (its "0." lost), not a re-throw's 24.
    static bool IsFresh(BombRead r) => r.Seconds is { } sec && sec >= Duration(r.Kind) - Fresh && sec <= Duration(r.Kind) + Off;

    /// <summary>A first read's seconds: its lost tens put back only when that lands within <see cref="JustThrown"/> s of the full
    /// time (icons are read every 0.5 s, so a throw is first read at 25 or 24); else as read, so a bomb first seen late keeps its
    /// own low time (a "3" is not 23) and a misread digit doesn't become a throw (a "7" for "1" is not 17).</summary>
    static double? FirstSeconds(BombRead r) =>
        r.Seconds is not { } shown ? null
        : Restored(shown, Duration(r.Kind), r.Kind) is var sec && sec >= Duration(r.Kind) - JustThrown ? sec : shown;

    public static double Duration(Bomb b) => b == Bomb.빛암 ? 25 : 20;

    /// <summary>Whether the bombs' "N초" is read at this 보스 디버프 크기 (<see cref="AmsuReader.ReadableFrom"/>); under it the 암수 창 shows
    /// only that a bomb is up, while its icon is seen.</summary>
    public static bool Timed(int bossSize) => bossSize >= AmsuReader.ReadableFrom;

    /// <summary>Whether bombs are read at all: the boss debuff icons at least <see cref="MinIconPx"/> on screen (user, 2026-11-04:
    /// 80% at 1080p is not supported). Smaller, a 빛암's 1 px frame blurs and the walk loses it behind a red-art icon (빛암 80_90).</summary>
    public static bool Readable(Viewport vp, Fit fit, int bossSize) =>
        DebuffReader.BossIconSize * Geometry.BossScale(vp, fit, bossSize) >= MinIconPx;

    /// <summary>Icon side in screen px: 80% at 1080p HUD 110 is 20 px (fails), 90% 23 px (reads).</summary>
    public const double MinIconPx = 21.5;

    /// <summary>The same on screen (user, 2026-11-01): the "N초" as large as 100% at 1080 high, so a game area shorter than 1080
    /// (forced 21:9 at 1920×1080: 810) needs more, a taller one less. Replay 14 misread the digits at 1920×810 and 100%.</summary>
    public static bool Timed(Viewport vp, int bossSize) => Geometry.BuffScale(vp, bossSize) >= AmsuReader.ReadableFrom / 100.0 - 0.005;

    // Untimed (iconsOnly): a bomb lasts while its icon is seen, and this long after (a popup or a damage number hides it a moment).
    static readonly TimeSpan IconGrace = TimeSpan.FromSeconds(1.5);

    /// <summary>The state after a step: <paramref name="row"/> null when the row wasn't read this step, <paramref name="bossMaxHp"/>
    /// null when the boss bar's HP didn't read (the countdown runs on).</summary>
    public static AmsuState Step(AmsuState s, AmsuRow? row, long? bossMaxHp, TimeSpan now, bool iconsOnly = false)
    {
        // Another boss only when its max HP reads twice in a row: a popup over the bar misreads it once (replay 2026-11-01).
        long? other = null;
        if (bossMaxHp is { } boss && s.BossMaxHp is { } had && boss != had)
        {
            if (s.OtherBoss == boss) s = AmsuState.None;
            else (other, bossMaxHp) = (boss, had);
        }
        var ended = (s.Ended ?? ImmutableDictionary<Bomb, EndedBomb>.Empty).SetItems(
            s.Up.Where(b => b.Value.EndsAt <= now).Select(b => KeyValuePair.Create(b.Key, new EndedBomb(b.Value.EndsAt, b.Value.Slot))));
        var up = s.Up.Where(b => b.Value.EndsAt > now).ToImmutableDictionary();
        var reads = row?.Bombs ?? [];
        foreach (var read in reads)
        {
            // One icon read as the other kind (they share the art: 90% in the game, a fading icon at its end): a bomb keeps its
            // slot, so a read in a running bomb's slot is that bomb. Not when the read's own kind is running and read only here:
            // that bomb moved into the slot of one that left (review 2026-11-01); nor when the owner is read in another slot.
            var kind = read.Kind;
            if (read.Slot is { } slot && up.Where(b => b.Key != kind && b.Value.Slot == slot).Select(b => (Bomb?)b.Key).FirstOrDefault() is { } owner
                && !reads.Any(r => r.Kind == owner && r.Slot != slot) && (!up.ContainsKey(kind) || reads.Count(r => r.Kind == kind) > 1))
                kind = owner;
            // Just after a bomb ended, its fading icon may read as the other kind: timed, a new one then needs a fresh "N초" (the
            // tail "0.8" a slot over started a 25 s 빛암, engine-loop sweep, 21:9 clip); untimed, another slot.
            else if (!up.ContainsKey(kind) && ended.Any(e => e.Key != kind && now - e.Value.At < Linger
                         && (iconsOnly ? read.Slot is { } at && e.Value.Slot == at : !IsFresh(read))))
                continue;
            if (up.TryGetValue(kind, out var t))
                up = up.SetItem(kind, (iconsOnly ? t with { LastSeen = now, EndsAt = now + IconGrace } : Seen(t, kind, read.Seconds, now))
                    with { Slot = read.Slot ?? t.Slot });
            // Just ended: the icon lingers a moment past our 0, so only a fresh throw starts it again (not a phantom full time).
            // Untimed, the icon seen is the evidence: no wait for a fresh "N초" that is never read (review 2026-11-01).
            // A bomb first seen in its last second ("0.1초") is about to end: nothing to start (it stood in at the full time).
            else if (!(!iconsOnly && ended.TryGetValue(kind, out var end) && now - end.At < Linger && !IsFresh(read)) && (read.Seconds is not null || iconsOnly)
                     && !(read.Seconds is < 1)
                     && !OtherKindsTime(up, read, now))
                up = up.SetItem(kind, (iconsOnly ? new(now + IconGrace, now) : New(read, now)) with { Slot = read.Slot });   // timed: needs its seconds
        }
        // Timed, a bomb missing from a row read with room in it wasn't pushed behind "+N": its icon gone a moment is its end
        // (user, 2026-11-01: an expired bomb counted on, faded). Untimed bombs end on their own grace (IconGrace).
        if (row is { Full: false, Seen: true } && !iconsOnly)
            foreach (var kind in up.Where(b => now - b.Value.LastSeen > IconGrace).Select(b => b.Key).ToList())
                (up, ended) = (up.Remove(kind), ended.SetItem(kind, new EndedBomb(now, up[kind].Slot)));
        return new(up, bossMaxHp ?? s.BossMaxHp, ended, other);
    }

    /// <summary>A bomb first seen: thrown just now (a fresh read, or none read) at its read or full time; else at its full time,
    /// the read waiting for more that agree.</summary>
    static BombTimer New(BombRead read, TimeSpan now)
    {
        double full = Duration(read.Kind);
        if (FirstSeconds(read) is not { } sec || sec > full + Off) return new(now + TimeSpan.FromSeconds(full), now);
        return sec >= full - Fresh ? new(now + TimeSpan.FromSeconds(sec), now)   // the bottom of its second: the next reads pull it up, the first step down dates it
            : new(now + TimeSpan.FromSeconds(full), now, Pending: sec, PendingAt: now, Agreeing: 1);
    }

    static BombTimer Seen(BombTimer t, Bomb kind, double? seconds, TimeSpan now)
    {
        t = t with { LastSeen = now };
        if (seconds is not { } shown || shown > Duration(kind) + Off) return t;   // none, or more than a bomb lasts
        double left = (t.EndsAt - now).TotalSeconds;
        // A lost tens is put back near the read waiting for agreement, if any (the countdown may still be a stand-in full time).
        double sec = Restored(shown, t.Pending is { } q ? q - (now - t.PendingAt).TotalSeconds : left, kind);
        // Within a second of its window: the end pulled into it, as buffs are (BuffTracker.Window), so a step down dates it.
        if (left >= sec - 1 && left <= Top(sec) + 1)
            return t with { EndsAt = now + TimeSpan.FromSeconds(Math.Clamp(left, sec, Top(sec))), Pending = null, Agreeing = 0 };
        if (Math.Abs(sec - left) <= Off) return t with { Pending = null, Agreeing = 0 };
        bool higher = sec > left;
        // Up, but not to a just-thrown time: a misread ("11" as "17", replay 8). A re-throw reads 20/19 (25/24) at its first read.
        if (higher && sec < Duration(kind) - JustThrown) return t;
        if (t.Pending is { } p && Math.Abs(sec - (p - (now - t.PendingAt).TotalSeconds)) <= Off && (p > left) == higher)
        {
            int agreeing = t.Agreeing + 1;
            return agreeing >= (higher ? UpperReads : LowerReads)
                ? t with { EndsAt = t.PendingAt + TimeSpan.FromSeconds(p), Pending = null, Agreeing = 0 }
                : t with { Agreeing = agreeing };
        }
        return t with { Pending = sec, PendingAt = now, Agreeing = 1 };
    }

    /// <summary>A whole-second read under 10 may have lost its tens (BuffReader.MinTens: at 90–100% the "2" of "24" scores under it,
    /// replay 4): the candidate up to the bomb's length nearest <paramref name="near"/>, as BuffTracker.RestoreTens does.</summary>
    static double Restored(double shown, double near, Bomb kind) =>
        Tens(shown).Where(c => c <= Duration(kind) + Off).MinBy(c => Math.Abs(c - near));

    /// <summary>The top of a read's window: the game rounds whole seconds down ("16초" is 16.0–16.9 s left) and shows tenths
    /// under 1 s as they are (user, 2026-11-01; BuffTracker.Window).</summary>
    static double Top(double shown) => shown >= 1 && shown == Math.Floor(shown) ? shown + 1 : shown;

    /// <summary>What a read may have been: a whole second under 10 with its tens lost, or itself.</summary>
    static double[] Tens(double shown) => shown >= 10 || shown != Math.Floor(shown) ? [shown] : [shown, shown + 10, shown + 20];

    /// <summary>The changes from <paramref name="was"/> to <paramref name="now"/> at <paramref name="at"/>, bomb by bomb (the log, the
    /// fight record's 암수 lane).</summary>
    public static IReadOnlyList<AmsuEdge> Edges(AmsuState was, AmsuState now, TimeSpan at)
    {
        var edges = new List<AmsuEdge>();
        foreach (var kind in Enum.GetValues<Bomb>())
        {
            bool had = was.Up.TryGetValue(kind, out var a), has = now.Up.TryGetValue(kind, out var b);
            if (has && !had) edges.Add(new(kind, AmsuChange.시작, (b!.EndsAt - at).TotalSeconds));
            else if (has && (b!.EndsAt - a!.EndsAt).TotalSeconds > Off) edges.Add(new(kind, AmsuChange.재시작, (b.EndsAt - at).TotalSeconds));
            else if (had && !has) edges.Add(new(kind, AmsuChange.끝, 0));
        }
        return edges;
    }

    /// <summary>What the 암수 창 shows at <paramref name="now"/>: the bomb with the most time left (none: hidden), its seconds left,
    /// and whether its icon was seen in the last second (else drawn faded).</summary>
    public static (Bomb? Shown, double Left, bool Seen) View(AmsuState s, TimeSpan now)
    {
        var live = s.Up.Where(b => b.Value.EndsAt > now).ToList();
        if (live.Count == 0) return (null, 0, false);
        var (kind, t) = live.MaxBy(b => b.Value.EndsAt);
        return (kind, (t.EndsAt - now).TotalSeconds, (now - t.LastSeen).TotalSeconds <= SeenFor);
    }
}
