using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>수라의길's 기력·충격 cue: a 기력 skill spends the yellow (기력) half, a 충격 skill the green (충격) half; rises (each half
/// refilling on its own, Z filling both) say nothing. The cue shows the other side of the last skill, each 일반 skill's side is
/// learned from the drop after its cast, and Z lights the cue until the next skill.</summary>
public class RingCueTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Only_sura_gets_the_cue()
    {
        Assert.True(RingCue.For("브레이커 · 수라의길"));
        Assert.False(RingCue.For("브레이커 · 권왕파천무"));
        Assert.False(RingCue.For(null));
    }

    [Fact]
    public void A_green_drop_is_a_충격_skill_and_a_yellow_drop_a_기력_one()
    {
        var r = new RingTrack();
        r.Read(0.6, 0.6, S(0));
        Assert.True(r.Read(0.7, 0.3, S(1)));
        Assert.Equal(RingSide.기력, r.Cue!.Show);
        Assert.True(r.Read(0.3, 0.8, S(2)));
        Assert.Equal(RingSide.충격, r.Cue!.Show);
    }

    [Fact]
    public void Slow_refills_after_a_충격_skill_move_nothing()
    {
        // From a raid log (2026-11-18): 파천섬광 spends green, then both halves creep up.
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(756));
        r.Cast(SlotId.일반8, false, S(756.34));
        r.Read(0.55, 0.20, S(756.59));
        Assert.False(r.Read(0.60, 0.24, S(757.29)));
        Assert.False(r.Read(0.70, 0.35, S(757.6)));
        Assert.False(r.Read(0.80, 0.50, S(758.4)));   // past the step from where it was: still a rise
        Assert.Equal(RingSide.충격, r.Cue!.Last);
        Assert.Equal(RingSide.충격, r.Learned(SlotId.일반8));
    }

    [Fact]
    public void Noise_under_the_step_moves_nothing()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        Assert.False(r.Read(0.48, 0.52, S(1)));
        Assert.False(r.Read(0.52, 0.48, S(2)));
        Assert.Null(r.Cue);
    }

    [Fact]
    public void Yellow_teaches_a_기력_skill_while_green_is_full()
    {
        var r = new RingTrack();
        r.Read(0.8, 1.0, S(0));
        r.Cast(SlotId.일반1, false, S(1));
        Assert.True(r.Read(0.4, 1.0, S(1.3)));
        Assert.Equal(RingSide.기력, r.Learned(SlotId.일반1));
        Assert.Equal(RingSide.기력, r.Cue!.Last);
    }

    [Fact]
    public void A_learned_skill_moves_the_cue_on_its_cast()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Cast(SlotId.일반3, false, S(1));
        r.Read(0.6, 0.3, S(1.5));
        r.Cast(SlotId.일반1, false, S(4));
        r.Read(0.2, 0.6, S(4.5));
        Assert.Equal(RingSide.충격, r.Learned(SlotId.일반3));
        Assert.Equal(RingSide.기력, r.Learned(SlotId.일반1));
        Assert.True(r.Cast(SlotId.일반3, false, S(7)));
        Assert.Equal(RingSide.충격, r.Cue!.Last);
        Assert.True(r.Cast(SlotId.일반1, false, S(8)));   // before any drop is read
        Assert.Equal(RingSide.기력, r.Cue!.Last);
    }

    [Fact]
    public void A_late_drop_teaches_nothing_and_the_latest_drop_relearns()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Cast(SlotId.일반2, false, S(1));
        r.Read(0.2, 0.6, S(4));             // past CastWindow
        Assert.Null(r.Learned(SlotId.일반2));

        r.Cast(SlotId.일반2, false, S(5));
        r.Read(0.3, 0.2, S(5.5));
        Assert.Equal(RingSide.충격, r.Learned(SlotId.일반2));
        r.Cast(SlotId.일반2, false, S(7));   // the skill was moved
        r.Read(0.0, 0.3, S(7.5));
        Assert.Equal(RingSide.기력, r.Learned(SlotId.일반2));
    }

    [Fact]
    public void 초각스_learns_its_side_as_a_일반_skill()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Cast(SlotId.초각스, false, S(1));
        r.Read(0.1, 0.9, S(1.5));           // 천왕지무: 기력
        Assert.Equal(RingSide.기력, r.Learned(SlotId.초각스));
        r.Read(0.3, 0.4, S(3));             // a 충격 skill
        Assert.True(r.Cast(SlotId.초각스, false, S(4)));   // the learned side moves the cue before a drop does
        Assert.Equal(RingSide.기력, r.Cue!.Last);
    }

    [Fact]
    public void 각성기_learns_nothing()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Cast(SlotId.각성기, false, S(1));
        r.Read(0.6, 0.2, S(1.5));
        Assert.Null(r.Learned(SlotId.각성기));
    }

    [Fact]
    public void A_later_cast_takes_over_the_drop()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Cast(SlotId.일반3, false, S(1));
        r.Cast(SlotId.각성기, false, S(1.3));
        r.Read(0.6, 0.2, S(1.8));           // 각성기's drop, not 일반3's
        Assert.Null(r.Learned(SlotId.일반3));
    }

    [Fact]
    public void A_일반_cast_after_초각스_takes_over_its_drop()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Cast(SlotId.초각스, false, S(1));
        r.Cast(SlotId.일반2, false, S(1.5));
        r.Read(0.6, 0.2, S(2));             // 일반2's drop, not 초각스's
        Assert.Equal(RingSide.충격, r.Learned(SlotId.일반2));
        Assert.Null(r.Learned(SlotId.초각스));
    }

    [Fact]
    public void Both_halves_dropping_at_once_is_the_ring_covered()
    {
        // From a raid log and 수라 gauge bug.mp4 (2026-11-18, the first Z): Z's flash reads both halves 0, then full again,
        // and Z's cast is seen a second later.
        var r = new RingTrack();
        r.Read(0.5, 0.17, S(764.64));
        r.Cast(SlotId.일반3, false, S(765.09));
        r.Read(0.2, 0.64, S(765.54));       // 일반3 is 기력
        Assert.False(r.Read(0.0, 0.0, S(766.34)));
        Assert.False(r.Read(1.0, 1.0, S(766.74)));
        r.Cast(SlotId.아덴1, true, S(767.44));
        Assert.Equal(new RingCue(RingSide.기력, AfterZ: true), r.Cue);
        Assert.Equal(RingSide.기력, r.Learned(SlotId.일반3));
    }

    [Fact]
    public void Zs_flash_read_one_half_at_a_time_is_undone_by_its_cast()
    {
        var r = new RingTrack();
        r.Read(1.0, 1.0, S(0));
        r.Cast(SlotId.일반3, false, S(1));
        r.Read(0.6, 1.0, S(1.3));           // 일반3 is 기력
        r.Read(0.0, 1.0, S(2.6));           // the flash covers yellow a read before green
        r.Read(0.0, 0.0, S(2.7));           // green: 충격 until Z's cast undoes the run
        r.Read(1.0, 1.0, S(3.0));           // Z fills both
        r.Cast(SlotId.아덴1, true, S(3.5));
        Assert.Equal(new RingCue(RingSide.기력, AfterZ: true), r.Cue);
        Assert.Equal(RingSide.기력, r.Learned(SlotId.일반3));
    }

    [Fact]
    public void A_drop_against_a_known_side_right_before_Z_is_Zs()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Cast(SlotId.일반6, false, S(1));
        r.Read(0.2, 0.8, S(1.5));           // 일반6 learned 기력
        r.Cast(SlotId.일반6, false, S(2.5));
        r.Read(0.3, 0.0, S(2.7));           // Z's flash on green, not 일반6
        r.Read(1.0, 1.0, S(3.1));
        r.Cast(SlotId.아덴1, true, S(3.5));
        Assert.Equal(RingSide.기력, r.Learned(SlotId.일반6));
        Assert.Equal(RingSide.기력, r.Cue!.Last);
    }

    [Fact]
    public void A_skills_own_drop_right_before_Z_stays()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Cast(SlotId.일반3, false, S(1));
        r.Read(0.6, 0.2, S(1.2));           // 일반3 is 충격
        r.Read(1.0, 1.0, S(1.4));           // Z fills both
        r.Cast(SlotId.아덴1, true, S(1.5));
        Assert.Equal(RingSide.충격, r.Cue!.Last);
        Assert.Equal(RingSide.충격, r.Learned(SlotId.일반3));
    }

    [Fact]
    public void A_drop_Z_never_refilled_stays()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Read(0.6, 0.2, S(1));             // a 충격 skill whose cast wasn't seen
        r.Cast(SlotId.아덴1, true, S(1.5));
        Assert.Equal(RingSide.충격, r.Cue!.Last);
    }

    [Fact]
    public void A_slow_refill_of_one_half_is_not_Zs()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(9));
        r.Read(0.6, 0.2, S(10));            // a 충격 skill whose cast wasn't seen
        r.Read(0.6, 0.25, S(10.8));         // green refilling on its own
        r.Cast(SlotId.아덴1, true, S(11.7));
        Assert.Equal(RingSide.충격, r.Cue!.Last);
    }

    [Fact]
    public void Zs_cast_seen_between_its_flash_and_its_refill_undoes_the_flash()
    {
        var r = new RingTrack();
        r.Read(1.0, 1.0, S(0));
        r.Cast(SlotId.일반3, false, S(1));
        r.Read(0.6, 1.0, S(1.3));           // 일반3 is 기력
        r.Read(0.0, 1.0, S(2.6));
        r.Read(0.0, 0.0, S(2.8));           // the flash, read as 충격
        r.Cast(SlotId.아덴1, true, S(3.0));
        Assert.True(r.Read(1.0, 1.0, S(3.3)));   // Z fills both, in Z's phase
        Assert.Equal(new RingCue(RingSide.기력, AfterZ: true), r.Cue);
        Assert.Equal(RingSide.기력, r.Learned(SlotId.일반3));
    }

    [Fact]
    public void A_drop_long_before_Z_stays()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Read(0.6, 0.2, S(1));
        r.Read(1.0, 1.0, S(4.6));
        r.Cast(SlotId.아덴1, true, S(5));
        Assert.Equal(RingSide.충격, r.Cue!.Last);
    }

    [Fact]
    public void A_cast_between_the_drop_and_Z_keeps_the_drop()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Read(0.6, 0.2, S(1));
        r.Cast(SlotId.각성기, false, S(1.5));
        r.Read(1.0, 1.0, S(1.8));
        r.Cast(SlotId.아덴1, true, S(2));
        Assert.Equal(RingSide.충격, r.Cue!.Last);
    }

    [Fact]
    public void Z_lights_the_cue_even_before_any_skill_until_the_next_cast()
    {
        var r = new RingTrack();
        Assert.True(r.Cast(SlotId.아덴1, true, S(0)));
        Assert.True(r.Cue!.AfterZ);
        Assert.Null(r.Cue.Show);            // the whole bar
        Assert.True(r.Cast(SlotId.초각스, false, S(8)));
        Assert.False(r.Cue!.AfterZ);
    }

    [Fact]
    public void Drops_in_Zs_phase_keep_the_glow_and_one_after_it_ends_it()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Read(0.6, 0.2, S(1));
        r.Cast(SlotId.아덴1, true, S(2));
        Assert.False(r.Read(0.1, 0.6, S(4)));   // Z's hits
        Assert.True(r.Cue!.AfterZ);
        Assert.Equal(RingSide.충격, r.Cue.Last);
        Assert.True(r.Read(0.2, 0.3, S(9)));    // the next skill
        Assert.False(r.Cue!.AfterZ);
        Assert.Equal(RingSide.충격, r.Cue.Last);
    }

    [Fact]
    public void Z_seen_twice_keeps_the_first_phase()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Cast(SlotId.아덴1, true, S(1));
        r.Cast(SlotId.아덴1, true, S(3));
        Assert.True(r.Read(0.6, 0.2, S(7.5)));  // past 1 + ZPhase, not 3 + ZPhase
        Assert.False(r.Cue!.AfterZ);
    }

    [Fact]
    public void A_learned_skill_after_Z_ends_the_glow_on_its_side()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Cast(SlotId.일반5, false, S(1));
        r.Read(0.6, 0.2, S(1.5));
        r.Cast(SlotId.아덴1, true, S(3));
        r.Cast(SlotId.일반5, false, S(4));
        Assert.Equal(new RingCue(RingSide.충격), r.Cue);
    }

    [Fact]
    public void Clear_drops_the_cue_and_keeps_what_was_learned()
    {
        var r = new RingTrack();
        r.Read(0.5, 0.5, S(0));
        r.Cast(SlotId.일반4, false, S(1));
        r.Read(0.2, 0.8, S(1.2));
        r.Clear();
        Assert.Null(r.Cue);
        Assert.Equal(RingSide.기력, r.Learned(SlotId.일반4));
        Assert.False(r.Read(0.1, 0.1, S(5)));   // a new baseline first
    }

    [Theory]
    [InlineData(RingGlow.항상, false, true)]
    [InlineData(RingGlow.항상, true, true)]
    [InlineData(RingGlow.Z후, false, false)]
    [InlineData(RingGlow.Z후, true, true)]
    [InlineData(RingGlow.끄기, true, false)]
    public void Glow_picks_when_the_outline_shows(RingGlow glow, bool afterZ, bool drawn) =>
        Assert.Equal(drawn, new RingCue(RingSide.기력, afterZ).Drawn(glow));

    [Fact]
    public void Ring_자동_hides_only_outside_a_raid()
    {
        var r = new RingSettings();
        Assert.False(r.OutOfRaid(false));
        r.RaidOnly = true;
        Assert.True(r.OutOfRaid(false));
        Assert.False(r.OutOfRaid(true));
    }
}
