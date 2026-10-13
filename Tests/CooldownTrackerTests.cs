using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class CooldownTrackerTests
{
    private const SlotId D = SlotId.일반7;
    private const double Frame = 1 / 60.0;

    private static Reading R(ReadKind kind, double t, int value = 0) => new(D, kind, value, S(t));
    private static Reading Sec(int value, double t) => R(ReadKind.Seconds, t, value);

    /// <summary>A new cooldown started: the sweep restarts. The first seconds after "Nm" is the same cooldown, and so is
    /// one resumed after a flash (its ChangedAt goes back to the old cast).</summary>
    private static bool IsNewCast(SlotState prev, SlotState next) =>
        next.Phase is SlotPhase.Cooldown or SlotPhase.LongCooldown
        && (next.Phase != prev.Phase || next.ChangedAt != prev.ChangedAt)
        && next.ChangedAt >= prev.ChangedAt
        && !(prev.Phase == SlotPhase.LongCooldown && next.Phase == SlotPhase.Cooldown);

    /// <summary>What the game shows at t for a cooldown ending at <paramref name="endsAt"/> (round-up).</summary>
    private static Reading Game(double t, double endsAt) =>
        t >= endsAt ? R(ReadKind.Ready, t) : Sec((int)Math.Ceiling(endsAt - t - 1e-9), t);

    private static SlotState ReadyAt(double t) => new(D, SlotPhase.Ready, null, null, false, S(t));

    /// <summary>A cooldown cast at 0 ending at <paramref name="endsAt"/>, read every frame up to <paramref name="until"/>.</summary>
    private static SlotState Locked(double endsAt, double until)
    {
        var s = ReadyAt(-1);
        for (int k = 0; k * Frame <= until; k++) s = CooldownTracker.Update(s, Game(k * Frame, endsAt));
        return s;
    }

    private static void Near(double expected, TimeSpan? actual, double tolerance = Frame) =>
        Assert.InRange(actual!.Value.TotalSeconds, expected - tolerance, expected + tolerance);

    // live_test2 일반8: first read as the taskbar slides off, already at "1s". The cast wasn't seen, so the sweep has no
    // length to run over; chasing the unknown fraction of a second made it fill and fall back every frame.
    [Fact]
    public void A_cooldown_first_seen_midway_has_no_sweep_until_its_next_cast()
    {
        var s = SlotState.Unknown(D);
        for (int k = 0; k <= 40; k++)   // up to 1.55 s
        {
            double t = 0.883 + k * Frame;
            s = CooldownTracker.Update(s, Game(t, 1.6));
            Assert.Equal(SlotPhase.Cooldown, s.Phase);
            Assert.Null(s.Progress(S(t)));
        }
        Near(1.6, s.EndsAt, 0.1);   // the timer itself is still tracked

        s = CooldownTracker.Update(s, R(ReadKind.Ready, 1.7));
        var cast = CooldownTracker.Update(s, Sec(9, 3.0));
        Assert.True(IsNewCast(s, cast));
        Assert.Equal(0, cast.Progress(S(3.0)));
    }

    [Fact]
    public void A_stack_count_seen_with_the_timer_keeps_the_cooldown_and_marks_it_stacked()
    {
        var s = CooldownTracker.Update(ReadyAt(-1), Sec(8, 0) with { Stacked = true });
        Assert.Equal((SlotPhase.Cooldown, true), (s.Phase, s.Stacked));
        Near(7 + CooldownTracker.ChaseLead.TotalSeconds, s.EndsAt);

        s = CooldownTracker.Update(s, R(ReadKind.CooldownIllegible, 0.5));   // count can't be judged: keep it
        Assert.True(s.Stacked);
        s = CooldownTracker.Update(s, Sec(7, 1.2));                           // last use spent: count gone
        Assert.False(s.Stacked);
        s = CooldownTracker.Update(s, Sec(6, 2.2) with { Stacked = true });
        Assert.False(CooldownTracker.Update(s, R(ReadKind.Ready, 8)).Stacked);
    }

    /// <summary>The count vanishes at 0 uses, so a stack skill with none left looks like any cooldown: the slot is
    /// remembered as a stack skill from its first count until it can't be read (a loading screen, maybe another character).</summary>
    [Fact]
    public void A_slot_is_remembered_as_a_stack_skill_until_it_cant_be_read()
    {
        var s = CooldownTracker.Update(ReadyAt(-1), Sec(8, 0));
        Assert.False(s.StackSkill);
        s = CooldownTracker.Update(s, Sec(7, 1.2) with { Stacked = true });
        Assert.True(s.StackSkill);
        s = CooldownTracker.Update(s, Sec(6, 2.2));                       // last use spent
        Assert.Equal((false, true), (s.Stacked, s.StackSkill));
        s = CooldownTracker.Update(s, R(ReadKind.Ready, 9));
        Assert.True(s.StackSkill);
        Assert.True(CooldownTracker.Advance(CooldownTracker.Update(s, Sec(5, 10)) with { Estimated = true }, S(20)).StackSkill);
        Assert.False(CooldownTracker.Update(s, R(ReadKind.Unreadable, 10)).StackSkill);
    }

    /// <summary>The gold frame is what the screen shows now: set and cleared by each reading, kept through a flash
    /// (the frame can't be judged) and dropped once the slot can't be read.</summary>
    [Fact]
    public void The_gold_frame_follows_the_screen()
    {
        var s = CooldownTracker.Update(ReadyAt(-1), R(ReadKind.Ready, 0) with { Gold = true });
        Assert.True(s.Gold);
        s = CooldownTracker.Update(s, Sec(8, 0.5) with { Gold = true });
        Assert.Equal((SlotPhase.Cooldown, true), (s.Phase, s.Gold));
        Assert.True(CooldownTracker.Update(s, R(ReadKind.CooldownIllegible, 0.6)).Gold);
        Assert.False(CooldownTracker.Update(s, Sec(7, 1.5)).Gold);
        Assert.False(CooldownTracker.Update(s, R(ReadKind.Unreadable, 1.5)).Gold);
    }

    [Fact]
    public void A_stack_skills_reuse_lockout_before_its_recharge_timer_is_one_cast()
    {
        // live_test2 E / live test Q: a cast shows "1s" and the count (re-use lockout) for ~0.6 s, then the recharge
        // timer "9s". The lockout is not a 1 s cooldown: no sweep races to full and restarts, and the cast is counted once.
        var s = ReadyAt(-1);
        int casts = 0;
        double last = 0;
        for (int k = 0; k * Frame <= 2.5; k++)
        {
            double t = k * Frame;
            var next = CooldownTracker.Update(s, (t < 0.6 ? Sec(1, t) : Game(t, 9.3)) with { Stacked = true });
            if (IsNewCast(s, next)) casts++;
            s = next;
            if (t < 0.6) Assert.Null(s.Progress(S(t)));
            else if (s.Progress(S(t)) is { } p)
            {
                Assert.True(p >= last - 1e-9, $"sweep went back at {t:0.000}");
                last = p;
            }
        }
        Assert.Equal(1, casts);
        Assert.Equal(TimeSpan.Zero, s.ChangedAt);
        Near(9.3, s.EndsAt);
    }

    [Fact]
    public void A_known_stack_skills_lockout_is_one_cast_even_before_its_count_shows()
    {
        // live_test2 E 4.08 s, live test Q 3.05 s: the count beside "1s" comes ~150 ms after the cast. Without it the "1s"
        // was taken for a 1 s cooldown whose end was chased 50 ms at a time: the sweep jumped between 80 and 97 % for half a
        // second, then restarted at the recharge timer (user, 2026-10-13). A slot known to stack waits for the timer.
        var s = ReadyAt(-1) with { StackSkill = true };
        int casts = 0;
        double last = 0;
        for (int k = 0; k * Frame <= 2.5; k++)
        {
            double t = k * Frame;
            var r = t < 0.6 ? Sec(1, t) with { Stacked = t >= 0.15 } : Game(t, 9.3) with { Stacked = true };
            var next = CooldownTracker.Update(s, r);
            if (IsNewCast(s, next)) casts++;
            s = next;
            if (t < 0.6) Assert.Null(s.Progress(S(t)));
            else if (s.Progress(S(t)) is { } p)
            {
                Assert.True(p >= last - 1e-9, $"sweep went back at {t:0.000}");
                last = p;
            }
        }
        Assert.Equal(1, casts);
        Assert.Equal(TimeSpan.Zero, s.ChangedAt);
        Near(9.3, s.EndsAt);
    }

    [Fact]
    public void An_unknown_slots_1s_without_a_count_is_still_an_ordinary_cooldown()
    {
        var s = CooldownTracker.Update(ReadyAt(-1), Sec(1, 0));
        Assert.NotNull(s.EndsAt);   // a real last second of a skill not known to stack
    }

    [Fact]
    public void A_lockout_that_turns_unreadable_ends_on_the_timer()
    {
        var s = CooldownTracker.Update(ReadyAt(-1), Sec(1, 0) with { Stacked = true });
        s = CooldownTracker.Update(s, R(ReadKind.Unreadable, 0.3));   // HUD hidden right after the cast
        Assert.Equal(SlotPhase.Cooldown, CooldownTracker.Advance(s, S(0.9)).Phase);
        s = CooldownTracker.Advance(s, S(1.1));
        Assert.Equal((SlotPhase.Ready, true), (s.Phase, s.Estimated));
    }

    [Fact]
    public void A_flash_misread_during_the_lockout_does_not_set_the_recharge()
    {
        // live test Q, 14.43 s: the cast flash streaks left of "1s" and reads "11" for 3 frames; the recharge is "6s".
        var s = ReadyAt(-1);
        for (int k = 0; k * Frame <= 2.5; k++)
        {
            double t = k * Frame;
            var r = k is >= 1 and <= 3 ? Sec(11, t) : t < 0.7 ? Sec(1, t) : Game(t, 6.4);
            s = CooldownTracker.Update(s, r with { Stacked = true });
            Assert.True(s.EndsAt is null || s.EndsAt < S(7.5), $"EndsAt {s.EndsAt} at {t:0.000}");
        }
        Near(6.4, s.EndsAt);
        Assert.Equal(TimeSpan.Zero, s.ChangedAt);
    }

    [Fact]
    public void Measured_gameplay_D_edge_2s_to_1s_predicts_ready_one_second_later()
    {
        // gameplay.mp4, D slot: "2s" until frame 38, "1s" from frame 39, ready at frame 99.
        var s = ReadyAt(-1);
        for (int f = 0; f <= 99; f++)
        {
            var reading = f < 39 ? Sec(2, f * Frame) : f < 99 ? Sec(1, f * Frame) : R(ReadKind.Ready, f * Frame);
            s = CooldownTracker.Update(s, reading);
            if (f is >= 39 and < 99) Near(99 * Frame, s.EndsAt);
        }
        Assert.Equal(SlotPhase.Ready, s.Phase);
        Assert.Equal(S(99 * Frame), s.ChangedAt);
    }

    [Fact]
    public void Edge_relock_stays_within_one_frame_at_60fps_with_jitter()
    {
        const double castAt = 0.1234, length = 6.35, endsAt = castAt + length;
        var rng = new Random(7);
        var scheduler = new ReadScheduler();
        var s = ReadyAt(-1);
        bool pressed = false, readyChecked = false;

        for (int k = 0; k < 480; k++)
        {
            double t = k * Frame + (rng.NextDouble() * 2 - 1) * 0.003;
            if (!pressed && t >= castAt - 0.002) { scheduler.RequestCooldownReads(S(castAt)); pressed = true; }
            if (!scheduler.Due(S(t), [s]).Contains(D)) continue;
            scheduler.MarkRead(D, S(t));

            var prev = s;
            s = CooldownTracker.Update(s, t < castAt ? R(ReadKind.Ready, t) : Game(t, endsAt));

            // After the first edge (7s → 6s, at castAt + 0.35) every read keeps EndsAt within one frame.
            if (s.Phase == SlotPhase.Cooldown && t > castAt + 0.35 + 2 * Frame)
            {
                Near(endsAt, s.EndsAt, Frame + 0.006);
                Near(length, s.Total, 2 * Frame + 0.006);
            }
            if (s.Phase == SlotPhase.Ready && prev.Phase == SlotPhase.Cooldown)
            {
                // Ready on the first frame at or after the true end, from the screen.
                Assert.InRange(s.ChangedAt.TotalSeconds, endsAt, endsAt + Frame + 0.006);
                readyChecked = true;
            }
        }
        Assert.True(readyChecked);
    }

    [Theory]
    [InlineData(1)]    // "11s" read as 1 for its whole second (live_test2, S and 초각스)
    [InlineData(18)]   // "8s" read as 18: a stack skill's clock hand taken for a leading 1 (live_test2, E)
    public void A_misread_held_for_a_whole_second_neither_restarts_nor_moves_the_cooldown(int misread)
    {
        const double endsAt = 40;
        var s = Locked(endsAt, until: 28.5);   // exact after several edges; "12s" until 29, "11s" until 30
        var castAt = s.ChangedAt;
        for (double t = 29; t < 30; t += Frame)
        {
            var prev = s;
            s = CooldownTracker.Update(s, Sec(misread, t));
            Assert.False(IsNewCast(prev, s));
            Near(endsAt, s.EndsAt);   // the sweep doesn't jump while the misread is on screen
        }
        for (double t = 30; t < 31; t += Frame) s = CooldownTracker.Update(s, Game(t, endsAt));   // "10s" again

        Assert.Equal(castAt, s.ChangedAt);
        Near(endsAt, s.EndsAt);
        Assert.Null(s.Pending);
    }

    [Fact]
    public void Reduction_mid_cooldown_relocks_and_is_exact_again_after_the_next_edge()
    {
        double endsAt = 10.4;
        var s = ReadyAt(-1);
        var castAt = TimeSpan.Zero;
        for (int k = 0; k * Frame < 7.5; k++)
        {
            double t = k * Frame;
            if (t >= 3.3) endsAt = 7.4;   // rune: −3 s at 3.3 s
            var prev = s;
            s = CooldownTracker.Update(s, Game(t, endsAt));
            if (k == 0) castAt = s.ChangedAt;
            if (t >= 7.4) break;

            Assert.False(IsNewCast(prev, s) && k > 0);
            Assert.Equal(castAt, s.ChangedAt);
            if (t >= 3.3 && t < 3.4)
            {
                Near(10.4, s.EndsAt);                                         // "5s" instead of "8s": held, one read could be a misread…
                Assert.Equal(5, s.Pending?.Value);
            }
            if (t >= 3.4 + Frame && t < 4.4)
                Assert.InRange(s.EndsAt!.Value.TotalSeconds, t + 3, 7.4 + Frame);   // …re-locked once "4s" agrees with it
            if (t > 3.3 + 1.1)
                Near(7.4, s.EndsAt);                                          // exact again after the next edge
        }
        Assert.Equal(SlotPhase.Ready, s.Phase);
        Near(7.4, s.ChangedAt);
    }

    [Fact]
    public void A_single_noisy_over_read_while_still_cooling_does_not_restart_the_cooldown()
    {
        var s = Locked(endsAt: 20, until: 9);   // 20 s cooldown, tracked consistently up to 11 s left
        var castAt = s.ChangedAt;

        // A misread frame claims far more time is left than predicted; the real EndsAt (20) is still in the
        // future, so this can't be a genuine new cast — a single noisy over-read must not restart the sweep.
        var noisy = CooldownTracker.Update(s, Sec(16, 9.02));
        Assert.False(IsNewCast(s, noisy));
        Assert.Equal(castAt, noisy.ChangedAt);
    }

    [Fact]
    public void Reset_reads_ready_immediately_while_the_timer_says_17s()
    {
        var s = Locked(endsAt: 25.3, until: 8);
        var ready = CooldownTracker.Update(s, R(ReadKind.Ready, 8.3));
        Assert.Equal((SlotPhase.Ready, false, S(8.3)), (ready.Phase, ready.Estimated, ready.ChangedAt));

        // Recast right after the reset: the full cooldown is far above the old prediction, so it is a new cast.
        var recast = CooldownTracker.Update(ready, Sec(30, 8.35));
        Assert.True(IsNewCast(ready, recast));
        Assert.Equal(S(8.35), recast.ChangedAt);

        // After the resume window even digits matching the old prediction are a new cast.
        var later = CooldownTracker.Update(ready, Sec(17, 8.3 + CooldownTracker.ResumeWindow.TotalSeconds + Frame));
        Assert.True(IsNewCast(ready, later));
    }

    [Fact]
    public void One_flash_frame_without_digits_mid_cooldown_resumes_it_without_a_new_cast()
    {
        var s = Locked(endsAt: 10.4, until: 3.5);
        var flash = CooldownTracker.Update(s, R(ReadKind.Ready, 3.5 + Frame));
        Assert.Equal(SlotPhase.Ready, flash.Phase);   // could be a reset: shown at once (333.mp4)

        // The scheduler reads it again on the very next frame, which shows the digits again.
        var scheduler = new ReadScheduler();
        scheduler.MarkRead(D, S(3.5 + Frame));
        Assert.Contains(D, scheduler.Due(S(3.5 + 2 * Frame), [flash]));
        var resumed = CooldownTracker.Update(flash, Game(3.5 + 2 * Frame, 10.4));

        Assert.False(IsNewCast(flash, resumed));   // the sweep keeps the old cast
        Assert.Equal((SlotPhase.Cooldown, s.ChangedAt, s.EndsAt, s.Total), (resumed.Phase, resumed.ChangedAt, resumed.EndsAt, resumed.Total));
    }

    [Fact]
    public void Unreadable_for_5s_counts_on_estimated_then_confirms_on_the_next_read()
    {
        var s = Locked(endsAt: 12.4, until: 2);
        var endsAt = s.EndsAt;
        for (double t = 2.1; t < 7; t += 0.1)
        {
            s = CooldownTracker.Advance(CooldownTracker.Update(s, R(ReadKind.Unreadable, t)), S(t));
            Assert.Equal((SlotPhase.Cooldown, true, endsAt), (s.Phase, s.Estimated, s.EndsAt));
        }
        s = CooldownTracker.Update(s, Game(7.05, 12.4));
        Assert.False(s.Estimated);
        Near(12.4, s.EndsAt);
    }

    [Fact]
    public void Unreadable_past_the_end_becomes_estimated_ready_then_a_read_confirms_it()
    {
        var s = Locked(endsAt: 5.4, until: 2);
        s = CooldownTracker.Update(s, R(ReadKind.Unreadable, 3));
        s = CooldownTracker.Advance(s, S(6));
        Assert.Equal((SlotPhase.Ready, true), (s.Phase, s.Estimated));
        Near(5.4, s.ChangedAt);

        var confirmed = CooldownTracker.Update(s, R(ReadKind.Ready, 6.5));
        Assert.Equal(s with { Estimated = false }, confirmed);

        Assert.Equal(SlotState.Unknown(D), CooldownTracker.Update(SlotState.Unknown(D), R(ReadKind.Unreadable, 1)));
    }

    [Fact]
    public void Minutes_show_no_sweep_until_the_first_seconds_reading()
    {
        var s = CooldownTracker.Update(ReadyAt(0), R(ReadKind.Minutes, 1, 3));
        Assert.True(IsNewCast(ReadyAt(0), s));
        Assert.Equal((SlotPhase.LongCooldown, 3, (double?)null), (s.Phase, s.Minutes, s.Progress(S(2))));

        s = CooldownTracker.Update(s, R(ReadKind.Minutes, 61, 2));
        s = CooldownTracker.Update(s, R(ReadKind.Minutes, 121, 1));
        Assert.Equal((SlotPhase.LongCooldown, 1, S(1)), (s.Phase, s.Minutes, s.ChangedAt));

        // The sweep covers the whole cooldown from the cast, minutes included, as the game's own sweep does; timing it
        // from the first seconds reading left it behind by the minutes phase (a ~70 s 초각스 about 10 s behind).
        var prev = s;
        s = CooldownTracker.Update(s, Sec(59, 150));
        Assert.Equal((SlotPhase.Cooldown, S(1)), (s.Phase, s.ChangedAt));
        Assert.InRange(s.Total!.Value.TotalSeconds, 207, 208);
        Assert.False(IsNewCast(prev, s));
    }

    [Fact]
    public void A_flash_on_a_cooldown_first_seen_midway_resumes_it()
    {
        // First seen at "3s" (no sweep length), a one-frame Ready, then the digits again: the same cooldown, not a cast.
        var s = CooldownTracker.Update(SlotState.Unknown(D), Sec(3, 0));
        s = CooldownTracker.Update(s, Sec(2, 0.05));
        var ready = CooldownTracker.Update(s, R(ReadKind.Ready, 0.9));
        var resumed = CooldownTracker.Update(ready, Sec(2, 0.95));
        Assert.Equal(SlotPhase.Cooldown, resumed.Phase);
        Assert.False(IsNewCast(ready, resumed));
        Assert.Null(resumed.Total);
        Assert.Equal(s.EndsAt, resumed.EndsAt);
    }

    [Fact]
    public void Minutes_first_seen_midway_give_no_sweep_after_them()
    {
        // App started during the minutes phase: the cast wasn't seen, so there is no length to sweep over.
        var s = CooldownTracker.Update(SlotState.Unknown(D), R(ReadKind.Minutes, 1, 2));
        s = CooldownTracker.Update(s, R(ReadKind.Minutes, 61, 1));
        s = CooldownTracker.Update(s, Sec(59, 90));
        Assert.Equal(SlotPhase.Cooldown, s.Phase);
        Assert.NotNull(s.EndsAt);
        Assert.Null(s.Progress(S(100)));
        s = CooldownTracker.Update(s, Sec(58, 91));   // still none as it counts down
        Assert.Null(s.Progress(S(100)));
    }

    [Fact]
    public void Repeated_readings_matching_the_prediction_change_nothing()
    {
        var s = Locked(endsAt: 10.4, until: 3.5);
        // Key spam triggers extra reads; mid-second they show the predicted digits and leave the state as is.
        for (double t = 3.52; t < 3.8; t += 0.01)
            Assert.Equal(s, CooldownTracker.Update(s, Game(t, 10.4)));
    }

    [Fact]
    public void Spam_at_the_moment_of_ready_is_a_new_cast_with_a_ready_shorter_than_100ms()
    {
        var s = Locked(endsAt: 6.35, until: 6.34);
        var ready = CooldownTracker.Update(s, R(ReadKind.Ready, 6.35 + 0.5 * Frame));
        var recast = CooldownTracker.Update(ready, Sec(7, 6.35 + 1.5 * Frame));
        Assert.True(IsNewCast(ready, recast));
        Assert.True(recast.ChangedAt - ready.ChangedAt < TimeSpan.FromMilliseconds(100));
        Near(6.35 + 1.5 * Frame + 6, recast.EndsAt, 0.06);

        // The ready frame skipped entirely: digits > prediction + 1 s under the use flash are still a new cast. Without
        // the flash they wait for the next digit to agree (sBug.mp4).
        Assert.False(IsNewCast(s, CooldownTracker.Update(s, Sec(7, 6.35 + 0.5 * Frame))));
        var skipped = CooldownTracker.Update(s, Sec(7, 6.35 + 0.5 * Frame) with { Flash = true });
        Assert.True(IsNewCast(s, skipped));
        Assert.Equal(S(6.35 + 0.5 * Frame), skipped.ChangedAt);
    }

    /// <summary>A case-2 아덴 skill: dark without enough resource, lit again when there is.</summary>
    [Fact]
    public void A_dark_skill_is_unavailable_until_it_lights_up()
    {
        var dark = CooldownTracker.Update(ReadyAt(0), R(ReadKind.Unavailable, 1));
        Assert.Equal((SlotPhase.Unavailable, S(1)), (dark.Phase, dark.ChangedAt));
        Assert.Equal(dark, CooldownTracker.Update(dark, R(ReadKind.Unavailable, 2)));
        Assert.Equal(dark, CooldownTracker.Advance(dark, S(60)));
        Assert.Equal(SlotPhase.Ready, CooldownTracker.Update(dark, R(ReadKind.Ready, 3)).Phase);
        Assert.Equal(SlotPhase.Cooldown, CooldownTracker.Update(dark, Sec(9, 3)).Phase);
    }

    /// <summary>추정.PNG: a frame that can't be read (a cast effect, the cursor) marks a dark skill estimated, and the
    /// next dark read confirms it; it used to stay 추정 until the skill lit up.</summary>
    [Fact]
    public void A_dark_read_confirms_a_dark_skill_left_estimated()
    {
        var dark = CooldownTracker.Update(ReadyAt(0), R(ReadKind.Unavailable, 1));
        var unsure = CooldownTracker.Update(dark, R(ReadKind.Unreadable, 2));
        Assert.True(unsure.Estimated);
        Assert.Equal(dark, CooldownTracker.Update(unsure, R(ReadKind.Unavailable, 3)));
    }

    [Fact]
    public void Illegible_last_second_holds_the_cooldown_and_never_turns_ready_on_its_own()
    {
        var s = Locked(endsAt: 5.4, until: 4.6);
        for (double t = 4.62; t < 6; t += Frame)
        {
            s = CooldownTracker.Advance(CooldownTracker.Update(s, R(ReadKind.CooldownIllegible, t)), S(t));
            Assert.Equal((SlotPhase.Cooldown, false), (s.Phase, s.Estimated));
        }
        Assert.Equal(TimeSpan.Zero, s.Remaining(S(6)));
        Assert.Equal(SlotPhase.Ready, CooldownTracker.Update(s, R(ReadKind.Ready, 6)).Phase);
    }
}
