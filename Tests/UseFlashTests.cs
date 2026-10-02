using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>A skill's use flashes white around the icon's edge (chainSkill.mp4). A chain skill then looks ready, its next
/// part pressable, until its number shows up to a second later; from the flash on it is a cooldown without a number. A
/// cooldown whose prediction ran out restarts only on a flash or on two readings that agree (sBug.mp4: "1s" read 11).</summary>
public class UseFlashTests
{
    const SlotId W = SlotId.일반2;
    const double Frame = 1 / 60.0;

    static Reading Ready(double t, bool flash = false) => new(W, ReadKind.Ready, 0, S(t), Flash: flash);
    static Reading Sec(int v, double t) => new(W, ReadKind.Seconds, v, S(t));
    static SlotState ReadyAt(double t) => new(W, SlotPhase.Ready, null, null, false, S(t));
    static SlotState Feed(SlotState s, params Reading[] rs) => rs.Aggregate(s, CooldownTracker.Update);

    [Fact]
    public void A_flash_on_a_ready_skill_is_a_cooldown_without_a_number()
    {
        var s = Feed(ReadyAt(0), Ready(1.0, flash: true));
        Assert.Equal(SlotPhase.Cooldown, s.Phase);
        Assert.Null(s.Remaining(S(1.0)));
        Assert.Null(s.Progress(S(1.0)));
    }

    [Fact]
    public void Ready_reads_after_a_flash_are_ignored_for_3_s_from_the_last_flash()
    {
        var s = Feed(ReadyAt(0), Ready(1.0, flash: true), Ready(1.2), Ready(1.5, flash: true), Ready(4.2));
        Assert.Equal(SlotPhase.Cooldown, s.Phase);   // 2.7 s after the second flash
        Assert.Equal(SlotPhase.Ready, Feed(s, Ready(4.6)).Phase);
    }

    [Fact]
    public void The_number_after_a_flash_starts_the_cooldown_at_the_flash()
    {
        var s = Feed(ReadyAt(0), Ready(1.0, flash: true), Ready(1.8), Sec(4, 2.5));
        Assert.Equal(SlotPhase.Cooldown, s.Phase);
        Assert.Equal(S(1.0), s.ChangedAt);
        Assert.InRange(s.EndsAt!.Value.TotalSeconds, 5.5, 5.6);   // shown at once, not held like a stack skill's lockout
        Assert.True(s.Progress(S(2.5)) > 0);
    }

    [Fact]
    public void Unread_after_a_flash_it_is_ready_again_3_s_later()
    {
        var s = Feed(ReadyAt(0), Ready(1.0, flash: true), new Reading(W, ReadKind.Unreadable, 0, S(1.2)));
        Assert.Equal(SlotPhase.Cooldown, CooldownTracker.Advance(s, S(3.9)).Phase);
        Assert.Equal(SlotPhase.Ready, CooldownTracker.Advance(s, S(4.1)).Phase);
    }

    [Fact]
    public void A_flash_just_after_a_cooldown_ends_is_not_a_use()
    {
        // Ready straight out of a cooldown keeps its EndsAt for a moment; the game may light the edge then.
        var s = ReadyAt(0) with { EndsAt = S(0), Total = S(10) };
        Assert.Equal(SlotPhase.Ready, Feed(s, Ready(0.05, flash: true)).Phase);
    }

    [Fact]
    public void A_flash_on_a_slot_unread_a_moment_ago_is_a_use()
    {
        // The flash's first frames can wash out the key label (chain_Q): the slot is estimated, the flash still counts.
        var s = ReadyAt(0) with { Estimated = true };
        Assert.Equal(SlotPhase.Cooldown, Feed(s, Ready(1.0, flash: true)).Phase);
    }

    [Fact]
    public void An_expired_cooldown_doesnt_restart_on_one_higher_reading()
    {
        var s = ReadyAt(-1);
        for (double t = 0; t < 1.0; t += Frame) s = Feed(s, Sec(t < 0.5 ? 2 : 1, t + 0.5));   // ends at 1.5
        s = Feed(s, Sec(11, 1.6), Sec(11, 1.7), Ready(1.8));
        Assert.Equal(SlotPhase.Ready, s.Phase);
    }

    [Fact]
    public void An_expired_cooldown_restarts_when_the_next_number_agrees()
    {
        var s = ReadyAt(-1);
        for (double t = 0; t < 1.0; t += Frame) s = Feed(s, Sec(t < 0.5 ? 2 : 1, t + 0.5));
        s = Feed(s, Sec(12, 1.6), Sec(11, 2.6));
        Assert.Equal(SlotPhase.Cooldown, s.Phase);
        Assert.InRange(s.EndsAt!.Value.TotalSeconds, 12.6, 12.7);
    }
}

/// <summary>The recordings through the real reader and tracker.</summary>
public class UseFlashReplayTests
{
    static List<SlotState> Replay(string name, int calibrateFrom)
    {
        var f = Fixture.Load(name);
        var slot = Enum.Parse<SlotId>(f.Slot);
        var digits = new DigitReader(Geometry.Scale(Fhd, new Fit(f.Hud)));
        var cal = new SlotCalibration(f.Frames[calibrateFrom < 0 ? f.Frames.Count - 1 : calibrateFrom]);
        var s = SlotState.Unknown(slot);
        return [.. f.Frames.Select((img, i) => s = CooldownTracker.Update(s, SlotClassifier.Read(slot, img, f.Pts[i], cal, digits)))];
    }

    /// <summary>W used twice (flashes from frames 16 and 45), "4s" from frame 102; Q once (27), "9s" from 73. Q's first two
    /// flash frames wash out its key label (unreadable, no flash read), so the use is taken on the third.</summary>
    [Theory]
    [InlineData("chain_W_1", 16)]
    [InlineData("chain_W_2", 11)]
    [InlineData("chain_Q", 29)]
    public void A_chain_skill_is_on_cooldown_from_its_first_flash(string name, int flash)
    {
        var states = Replay(name, 0);
        Assert.All(states.Skip(1).Take(flash - 1), s => Assert.Equal(SlotPhase.Ready, s.Phase));
        Assert.All(states.Skip(flash), s => Assert.Equal(SlotPhase.Cooldown, s.Phase));
    }

    /// <summary>sBug.mp4: S counts 2, 1, then is ready at frame 114, never a fresh 10 or 11.</summary>
    [Fact]
    public void A_misread_at_the_end_of_a_cooldown_doesnt_restart_it()
    {
        var states = Replay("sbug_일반6", -1);
        Assert.All(states.Take(114), s => Assert.True(s.Phase == SlotPhase.Cooldown && s.EndsAt < S(2.2), $"{s.Phase} {s.EndsAt}"));
        Assert.Equal(SlotPhase.Ready, states[114].Phase);
    }
}
