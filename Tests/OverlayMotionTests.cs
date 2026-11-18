using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>overlay-motion.plan.md: which change of a read-driven window fades, buff bar arrivals, the meter's eased bars.</summary>
public sealed class OverlayMotionTests
{
    static readonly ReadShown Off = new(Gate: true, InUse: false), On = new(Gate: true, InUse: true);

    [Fact]
    public void A_read_starting_the_window_fades_it_in()
    {
        Assert.Equal(WindowChange.FadeIn, OverlayMotion.Change(Off, On, lingerEnded: false));
    }

    [Fact]
    public void A_linger_ending_fades_it_out()
    {
        Assert.Equal(WindowChange.FadeOut, OverlayMotion.Change(On, Off, lingerEnded: true));
    }

    [Fact]
    public void A_source_gone_snaps_it_away()
    {
        Assert.Equal(WindowChange.Snap, OverlayMotion.Change(On, Off, lingerEnded: false));
    }

    [Theory]
    [InlineData(false, true)]   // Alt+Tab back, a hotkey, 오버레이 표시: the gate opens with the read already on
    [InlineData(true, false)]   // the gate closes
    public void The_gate_changing_never_animates(bool gateBefore, bool gateAfter)
    {
        var (was, now) = (new ReadShown(gateBefore, InUse: true), new ReadShown(gateAfter, InUse: true));
        Assert.Equal(WindowChange.Snap, OverlayMotion.Change(was, now, lingerEnded: true));
    }

    [Fact]
    public void A_read_starting_while_the_gate_opens_snaps()
    {
        Assert.Equal(WindowChange.Snap, OverlayMotion.Change(new ReadShown(false, false), On, lingerEnded: false));
    }

    [Fact]
    public void Edit_mode_placing_the_window_never_animates()
    {
        var placing = new ReadShown(Gate: true, InUse: false, Placing: true);
        Assert.Equal(WindowChange.Snap, OverlayMotion.Change(placing, Off, lingerEnded: true));   // edit mode ends, nothing read
        Assert.Equal(WindowChange.Snap, OverlayMotion.Change(Off, placing, lingerEnded: false));  // edit mode starts
    }

    [Fact]
    public void Nothing_changes_while_the_shown_state_holds()
    {
        Assert.Equal(WindowChange.None, OverlayMotion.Change(On, On, lingerEnded: false));
        Assert.Equal(WindowChange.None, OverlayMotion.Change(new ReadShown(true, true, true), new ReadShown(true, false, true), lingerEnded: true));
        Assert.Equal(WindowChange.None, OverlayMotion.Change(Off, Off, lingerEnded: true));
    }

    [Fact]
    public void A_gate_change_while_hidden_snaps_so_a_fade_out_under_way_is_cut()
    {
        // The linger just ended (fading out), then Alt+Tab or a hotkey closes the gate: gone at once.
        Assert.Equal(WindowChange.Snap, OverlayMotion.Change(Off, new ReadShown(Gate: false, InUse: false), lingerEnded: true));
    }

    [Fact]
    public void The_meters_linger_ended_only_when_it_timed_out_not_on_leaving_the_dungeon()
    {
        var read = TimeSpan.FromSeconds(100);
        Assert.True(WindowLinger.LingerEnded(read, RaidPresence.None));
        Assert.False(WindowLinger.LingerEnded(null, RaidPresence.None));   // the meter went off: gone
        var outside = RaidPresence.None with { OutSeen = read + TimeSpan.FromSeconds(1) };
        Assert.False(WindowLinger.LingerEnded(read, outside));   // the channel box's gold arrow: out at once
    }

    [Fact]
    public void A_buff_coming_on_arrives_from_half_and_is_whole_after_its_time()
    {
        var a = Arrival.Unseen.Next(on: false, ms: 0, motion: true).Next(on: true, ms: 1000, motion: true);
        Assert.Equal(OverlayMotion.FadeFrom, a.Opacity(1000, 120), 6);
        Assert.True(a.Moving(1060, 120));
        Assert.InRange(a.Opacity(1060, 120), 0.9, 1.0);   // Out: most of the way by half
        Assert.Equal(1, a.Opacity(1120, 120), 6);
        Assert.False(a.Moving(1120, 120));
    }

    [Fact]
    public void A_buff_first_seen_on_doesnt_fade()
    {
        // A window shown again (Alt+Tab) or a new layout starts unseen: what is on is drawn whole.
        var a = Arrival.Unseen.Next(on: true, ms: 1000, motion: true);
        Assert.Equal(1, a.Opacity(1000, 120), 6);
        Assert.False(a.Moving(1000, 120));
    }

    [Fact]
    public void A_buff_refreshed_while_on_doesnt_fade_again()
    {
        var a = Arrival.Unseen.Next(false, 0, true).Next(true, 1000, true).Next(true, 1500, true);
        Assert.Equal(1, a.Opacity(1500, 120), 6);
    }

    [Fact]
    public void A_buff_arriving_with_motion_off_is_whole_at_once()
    {
        var a = Arrival.Unseen.Next(false, 0, true).Next(true, 1000, motion: false);
        Assert.Equal(1, a.Opacity(1000, 120), 6);
        Assert.False(a.Moving(1000, 120));
    }

    [Fact]
    public void A_glide_starts_where_it_is_and_ends_on_the_target()
    {
        var g = Glide.At(0.2).Toward(0.6, ms: 1000, durationMs: 240, motion: true);
        Assert.Equal(0.2, g.Value(1000, 240), 6);
        Assert.True(g.Moving(1100, 240));
        Assert.Equal(0.6, g.Value(1240, 240), 6);
        Assert.False(g.Moving(1240, 240));
    }

    [Fact]
    public void A_glide_retargeted_midway_starts_from_its_current_value()
    {
        var g = Glide.At(0).Toward(1, 1000, 240, true);
        double mid = g.Value(1120, 240);
        var again = g.Toward(0.5, 1120, 240, true);
        Assert.Equal(mid, again.Value(1120, 240), 6);
        Assert.Equal(0.5, again.Value(1360, 240), 6);
    }

    [Fact]
    public void A_glide_to_the_same_target_keeps_going()
    {
        var g = Glide.At(0).Toward(1, 1000, 240, true);
        Assert.Equal(g, g.Toward(1, 1100, 240, true));
    }

    [Fact]
    public void A_glide_with_motion_off_is_on_the_target_at_once()
    {
        var g = Glide.At(0.2).Toward(0.7, 1000, 240, motion: false);
        Assert.Equal(0.7, g.Value(1000, 240));
        Assert.False(g.Moving(1000, 240));
    }

    [Fact]
    public void A_glide_at_rest_is_exactly_its_value()
    {
        // The meter's bars at rest must draw the same pixels as before the refactor: no rounding through the curve.
        double v = 0.123456789;
        Assert.Equal(v, Glide.At(v).Value(5000, 240));
        Assert.Equal(v, Glide.At(0.9).Toward(v, 1000, 240, true).Value(1240, 240));
    }

    [Fact]
    public void Every_new_motion_stays_under_a_quarter_second()
    {
        Assert.All([OverlayMotion.InMs, OverlayMotion.OutMs, OverlayMotion.BarInMs, OverlayMotion.MeterBarMs], ms => Assert.InRange(ms, 1, 249));
    }
}
