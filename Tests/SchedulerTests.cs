using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class SchedulerTests
{
    private const SlotId Q = SlotId.일반1;
    private const double Frame = 1 / 60.0;

    private static SlotState State(SlotPhase phase, double? endsAt = null) =>
        new(Q, phase, endsAt is { } e ? S(e) : null, endsAt is { } e2 ? S(e2) : null, false, TimeSpan.Zero);

    /// <summary>Runs the scheduler at 60 fps, marking every due read; returns the read times.</summary>
    private static List<double> Reads(ReadScheduler scheduler, SlotState s, double from, double to, Action<double>? beforeFrame = null)
    {
        var reads = new List<double>();
        for (int k = (int)Math.Round(from / Frame); k * Frame < to; k++)
        {
            double t = k * Frame;
            beforeFrame?.Invoke(t);
            if (!scheduler.Due(S(t), [s]).Contains(Q)) continue;
            scheduler.MarkRead(Q, S(t));
            reads.Add(t);
        }
        return reads;
    }

    [Fact]
    public void Ready_is_read_at_20_hz_for_its_use_flash_and_long_cooldown_at_1_hz()
    {
        Assert.Equal(20, Reads(new ReadScheduler(), State(SlotPhase.Ready), 0, 1).Count);
        Assert.Equal(4, Reads(new ReadScheduler(), State(SlotPhase.Unknown), 0, 1).Count);
        Assert.Equal(2, Reads(new ReadScheduler(), State(SlotPhase.LongCooldown), 0, 2).Count);
    }

    [Fact]
    public void Cooldown_reads_edge_windows_one_mid_read_and_every_frame_in_the_last_second()
    {
        const double endsAt = 5.0;
        var reads = Reads(new ReadScheduler(), State(SlotPhase.Cooldown, endsAt), 0, endsAt);

        // Every frame within ±60 ms of each whole-second edge and in the last second.
        foreach (double edge in new[] { 1.0, 2.0, 3.0 })
            Assert.All(Enumerable.Range(0, 400).Select(k => k * Frame).Where(t => Math.Abs(t - edge) <= 0.055),
                t => Assert.Contains(t, reads));
        Assert.All(Enumerable.Range(0, 400).Select(k => k * Frame).Where(t => t >= 4.0 && t < endsAt),
            t => Assert.Contains(t, reads));

        // Between two edges (outside the windows): about one confirming read.
        Assert.InRange(reads.Count(t => t > 2.07 && t < 2.93), 1, 2);
    }

    [Fact]
    public void Key_spam_is_coalesced_into_one_read_on_the_next_frame()
    {
        var scheduler = new ReadScheduler();
        var s = State(SlotPhase.Cooldown, 10.5);   // 2.0 s: mid-second, outside edge windows
        scheduler.MarkRead(Q, S(1.99));

        for (int i = 0; i < 20; i++) scheduler.RequestCooldownReads(S(2.0 + i * 0.0005));
        Assert.DoesNotContain(Q, scheduler.Due(S(1.995), [s]));   // frame captured before the presses
        Assert.Contains(Q, scheduler.Due(S(2.0 + 1 * Frame), [s]));
        scheduler.MarkRead(Q, S(2.0 + Frame));
        Assert.DoesNotContain(Q, scheduler.Due(S(2.0 + 2 * Frame), [s]));
        Assert.DoesNotContain(Q, scheduler.Due(S(2.0 + 3 * Frame), [s]));
    }

    [Fact]
    public void Key_triggered_reads_are_capped_at_30_per_second()
    {
        var scheduler = new ReadScheduler();
        // Long cooldown is read once a second on its own; a press before every frame adds at most 30 more.
        var reads = Reads(scheduler, State(SlotPhase.LongCooldown), 0, 1, t => scheduler.RequestCooldownReads(S(t)));
        Assert.InRange(reads.Count, 20, 31);
    }

    // --- Watch (코어 빌드: a slot a cast is known to change is read on every frame for a while) ---

    private const SlotId Other = SlotId.일반2;

    private static SlotState On(SlotId slot, double endsAt) =>
        new(slot, SlotPhase.Cooldown, S(endsAt), S(endsAt), false, TimeSpan.Zero);

    /// <summary>Q and 일반2 on a cooldown that ends at 10.5, whole-second edges at .5: at the times used below (not within 60 ms
    /// of one) neither is due on its own while it was read 10 ms before.</summary>
    private static (ReadScheduler, SlotState[]) Idle(params double[] justRead)
    {
        var scheduler = new ReadScheduler();
        foreach (var t in justRead) { scheduler.MarkRead(Q, S(t)); scheduler.MarkRead(Other, S(t)); }
        return (scheduler, [On(Q, 10.5), On(Other, 10.5)]);
    }

    [Fact]
    public void A_watched_slot_is_due_on_every_frame_until_the_time_and_then_follows_its_normal_pace()
    {
        var (scheduler, states) = Idle(1.99);
        scheduler.Watch(Q, S(2.3));

        for (int k = 0; k <= 18; k++)   // 2.0 .. 2.3 at 60 fps, each frame read just before the next
        {
            double t = 2.0 + k * Frame;
            Assert.Contains(Q, scheduler.Due(S(t), states));
            scheduler.MarkRead(Q, S(t));
        }
        Assert.DoesNotContain(Q, scheduler.Due(S(2.0 + 19 * Frame), states));   // past 2.3: 17 ms after its last read, normal pace
    }

    [Fact]
    public void A_watch_ends_exactly_at_its_time()
    {
        var (scheduler, states) = Idle(1.99);
        scheduler.Watch(Q, S(2.0));
        Assert.Contains(Q, scheduler.Due(S(2.0), states));
        Assert.DoesNotContain(Q, scheduler.Due(S(2.0 + 1e-6), states));
    }

    [Fact]
    public void A_watch_reaches_no_other_slot()
    {
        var (scheduler, states) = Idle(1.99);
        scheduler.Watch(Q, S(3));
        Assert.Equal([Q], scheduler.Due(S(2.0), states));
    }

    /// <summary>Every phase, not only a cooldown: a ready slot (normally 20 Hz), a long cooldown (1 Hz) and an unread one (4 Hz).</summary>
    [Theory]
    [InlineData(SlotPhase.Ready)]
    [InlineData(SlotPhase.LongCooldown)]
    [InlineData(SlotPhase.Unknown)]
    public void A_watched_slot_is_due_on_every_frame_in_any_phase_and_only_until_the_time(SlotPhase phase)
    {
        var scheduler = new ReadScheduler();
        var states = new[] { State(phase) };
        scheduler.MarkRead(Q, S(1.99));
        scheduler.Watch(Q, S(2.3));

        for (int k = 0; k <= 18; k++)
        {
            double t = 2.0 + k * Frame;
            Assert.Contains(Q, scheduler.Due(S(t), states));
            scheduler.MarkRead(Q, S(t));
        }
        Assert.DoesNotContain(Q, scheduler.Due(S(2.0 + 19 * Frame), states));
    }

    [Fact]
    public void Watching_again_extends_a_watch_and_an_earlier_time_never_shortens_it()
    {
        var (scheduler, states) = Idle(3.39, 3.59);
        scheduler.Watch(Q, S(2.5));
        scheduler.Watch(Q, S(3.5));
        scheduler.Watch(Q, S(1.0));
        Assert.Contains(Q, scheduler.Due(S(3.4), states));
        Assert.DoesNotContain(Q, scheduler.Due(S(3.6), states));
    }

    [Fact]
    public void Several_slots_can_be_watched_at_once_each_for_its_own_time()
    {
        var (scheduler, states) = Idle(2.09, 2.59);
        scheduler.Watch(Q, S(2.2));
        scheduler.Watch(Other, S(3));
        Assert.Equal([Q, Other], scheduler.Due(S(2.1), states));
        Assert.Equal([Other], scheduler.Due(S(2.6), states));
    }

    [Fact]
    public void A_watched_slot_not_in_the_states_is_not_invented()
    {
        var (scheduler, states) = Idle(1.99);
        scheduler.Watch(SlotId.아덴2, S(3));
        Assert.DoesNotContain(SlotId.아덴2, scheduler.Due(S(2.0), states));
    }

    [Fact]
    public void Key_press_also_reads_ready_slots_so_a_cast_is_seen_on_the_next_frame()
    {
        var scheduler = new ReadScheduler();
        var s = State(SlotPhase.Ready);
        scheduler.MarkRead(Q, S(1));
        scheduler.RequestCooldownReads(S(1.05));
        Assert.Contains(Q, scheduler.Due(S(1.06), [s]));
    }
}
