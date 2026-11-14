using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>발키리아덴버그.mp4 (해방자) at 10 fps, the engine's gauge rate: a shine sweeps the orb at 1.0 s and 5.0 s and
/// reads it full for two reads (0.2 s), with a dip on either side; the orb really fills in steps (0.22 → 0.38 at 3.9 s,
/// → 0.52 at 5.8 s, → 0.73 at 6.6 s, full at 8.0 s).</summary>
public class GaugeFilterTests
{
    static double[] Replay(string clip = "gauge_valk_shine")
    {
        var def = GaugeLoader.Parse(File.ReadAllText(Path.Combine(Helpers.RepoRoot(), "Profiles", "aden", "발키리_해방자", GaugeLoader.JsonFile)),
            Fixture.Load("gauge_valk_support_mask").Frames[0]);
        var filter = new GaugeFilter();
        return [.. Fixture.Load(clip).Frames.Select(f => filter.Add(def.Read(f)))];
    }

    /// <summary>발키리아덴버그2.mp4: at 0.22 a shine at 3.3 s and 7.3 s first darkens the fill for three reads (0.11), then
    /// reads full for two; full from 9.3 s, where the orb shines on its own (over 80%) and a shine at 11.5 s drops
    /// them to 0.09–0.87.</summary>
    [Fact]
    public void A_shine_neither_dips_a_low_gauge_nor_makes_a_full_one_flicker()
    {
        var v = Replay("gauge_valk_shine2");
        for (int i = 0; i < 93; i++) Assert.True(v[i] == 0.22, $"{i / 10.0:0.0} s: {v[i]:0.00}");
        for (int i = 98; i < v.Length; i++) Assert.True(v[i] == 1, $"{i / 10.0:0.0} s: {v[i]:0.00}");
    }

    [Fact]
    public void A_shine_over_the_orb_does_not_fill_the_gauge()
    {
        var v = Replay();
        for (int i = 5; i < 20; i++) Assert.InRange(v[i], 0.15, 0.25);   // shine at 1.0 s
        for (int i = 45; i < 58; i++) Assert.InRange(v[i], 0.30, 0.55);  // shine at 5.0 s, fill 0.38 → 0.52
    }

    [Fact]
    public void A_real_fill_shows_within_0_4_s()
    {
        var v = Replay();
        Assert.Equal(0.38, v[43], 0.02);   // filled at 3.9 s
        Assert.Equal(0.73, v[70], 0.02);   // filled at 6.6 s
        Assert.Equal(1, v[84]);            // full at 8.0 s
    }

    /// <summary>From 80% the orb carries a shine of its own, which makes a full orb read 0.80 for up to 4 reads (9.0 s,
    /// 13.0 s, 17.0 s): shown full throughout.</summary>
    [Fact]
    public void A_full_orb_stays_full_under_its_shine()
    {
        var v = Replay();
        for (int i = 84; i < v.Length; i++) Assert.True(v[i] == 1, $"{i / 10.0:0.0} s: {v[i]:0.00}");
    }

    /// <summary>절실한구원gameplay.mp4 from 15.4 s at 10 fps: 0.78, then Z or X at 18.1 s spends the 3 full crystals and 0.06
    /// is left; the skill's lightning reads full at 18.3, 18.6–18.7 and 19.6–19.7 s. Told of the use, what it leaves is
    /// shown at once, then what is read (0.06) once a run agrees; no lightning read shows.</summary>
    [Fact]
    public void A_use_shows_the_spent_gauge_at_once()
    {
        var shown = ReplayBard(spentAt: 27, remainder: 0.03);
        for (int i = 0; i < 31; i++) Assert.InRange(shown[i], 0.72, 0.79);
        for (int i = 31; i < shown.Count; i++) Assert.Equal(0.06, shown[i], 0.02);
    }

    /// <summary>The key and the gauge must both show the use: a press with no drop after it changes nothing.</summary>
    [Fact]
    public void A_press_that_spends_nothing_leaves_the_gauge()
    {
        var shown = ReplayBard(spentAt: 5, remainder: 0.24);
        for (int i = 0; i < 27; i++) Assert.InRange(shown[i], 0.72, 0.79);
    }

    /// <summary>One stray low read after a press (a flash over the crystals) is not taken for the use.</summary>
    [Fact]
    public void One_low_read_after_a_press_is_not_a_spend()
    {
        var f = new GaugeFilter();
        for (int i = 0; i < 5; i++) f.Add(0.25, T(i));
        f.Arm(0, 0.125, T(5) + GaugeFilter.ArmFor);
        Assert.Equal(0.25, f.Add(0.02, T(5)));
        for (int i = 6; i < 16; i++) Assert.Equal(0.25, f.Add(0.25, T(i)));
    }

    /// <summary>Once confirmed, reads above what is left (the skill's effects over the gauge) are left out for 0.5 s only.</summary>
    [Fact]
    public void After_a_use_higher_reads_are_left_out_for_half_a_second()
    {
        var f = new GaugeFilter();
        for (int i = 0; i < 5; i++) f.Add(0.75, T(i));
        f.Arm(0.25, 0.125, T(5) + GaugeFilter.ArmFor);
        f.Add(0.25, T(5));
        Assert.Equal(0.25, f.Add(0.25, T(6)));                            // two in a row: shown
        for (int i = 7; i <= 10; i++) Assert.Equal(0.25, f.Add(1, T(i)));  // lightning, within 0.5 s
        for (int i = 11; i < 16; i++) f.Add(0.75, T(i));
        Assert.Equal(0.75, f.Add(0.75, T(16)));                           // after it, a run rules again
    }

    /// <summary>A drop too late after the press (past <see cref="GaugeFilter.ArmFor"/>) is taken the ordinary way.</summary>
    [Fact]
    public void A_press_waits_one_second_for_its_drop()
    {
        var f = new GaugeFilter();
        for (int i = 0; i < 5; i++) f.Add(0.75, T(i));
        f.Arm(0.25, 0.125, T(5) + GaugeFilter.ArmFor);
        for (int i = 5; i < 16; i++) f.Add(0.75, T(i));
        f.Add(0.25, T(16));
        Assert.Equal(0.75, f.Add(0.25, T(17)));
    }

    /// <summary>A skill that always spends (도화가): told of the use, the gauge drops at once, without waiting for reads to
    /// confirm; the old gauge still read before the game draws the drop, and the skill's effects, are left out.</summary>
    [Fact]
    public void A_spend_shows_at_once_and_the_read_follows_afterwards()
    {
        var f = new GaugeFilter();
        for (int i = 0; i < 5; i++) f.Add(0.75, T(i));
        Assert.Equal(0.25, f.Spend(0.25, T(5)));
        for (int i = 5; i < 9; i++) Assert.Equal(0.25, f.Add(0.75, T(i)));   // the drop is not drawn yet
        for (int i = 9; i < 16; i++) Assert.Equal(0.25, f.Add(0.25, T(i)));
    }

    /// <summary>Told of a use that never happened, the gauge goes back to what is read once the hold is over.</summary>
    [Fact]
    public void A_spend_that_did_not_happen_is_corrected_by_the_read()
    {
        var f = new GaugeFilter();
        for (int i = 0; i < 5; i++) f.Add(0.75, T(i));
        f.Spend(0.25, T(5));
        for (int i = 5; i < 19; i++) f.Add(0.75, T(i));   // held to 1.5 s, then a run of 5 reads
        Assert.Equal(0.75, f.Add(0.75, T(19)));
    }

    static TimeSpan T(int read) => TimeSpan.FromSeconds(read / 10.0);

    static List<double> ReplayBard(int spentAt, double remainder)
    {
        var def = GaugeLoader.Parse(File.ReadAllText(Path.Combine(Helpers.RepoRoot(), "Profiles", "aden", "바드_절실한구원", GaugeLoader.JsonFile)),
            Fixture.Load("gauge_bard_support_mask").Frames[0]);
        var filter = new GaugeFilter();
        var frames = Fixture.Load("gauge_bard_use").Frames;
        var shown = new List<double>();
        for (int i = 0; i < frames.Count; i++)
        {
            if (i == spentAt) filter.Arm(remainder, 0.125, T(i) + GaugeFilter.ArmFor);
            shown.Add(filter.Add(def.Read(frames[i]), T(i)));
        }
        return shown;
    }

    static double[] Replay(string clip, GaugeReadMode mode)
    {
        var def = GaugeLoader.Parse(File.ReadAllText(Path.Combine(Helpers.RepoRoot(), "Profiles", "aden", "발키리_해방자", GaugeLoader.JsonFile)),
            Fixture.Load("gauge_valk_support_mask").Frames[0]);
        var filter = new GaugeFilter { Mode = mode };
        var frames = Fixture.Load(clip).Frames;
        return [.. frames.Select((f, i) => filter.Add(def.Read(f), T(i)))];
    }

    static GaugeFilter At(double shown, GaugeReadMode mode)
    {
        var f = new GaugeFilter { Mode = mode };
        for (int i = 0; i < 5; i++) f.Add(shown, T(i));
        return f;
    }

    [Fact]
    public void A_steady_small_rise_shows_after_two_reads()
    {
        var f = At(0.3, GaugeReadMode.SteadyRise);
        f.Add(0.38, T(5));
        Assert.Equal(0.38, f.Add(0.38, T(6)));
    }

    /// <summary>The shine's veil reads the orb full for two reads: a jump that big still waits for five.</summary>
    [Fact]
    public void A_big_jump_still_waits_with_steady_rises()
    {
        var f = At(0.3, GaugeReadMode.SteadyRise);
        f.Add(1, T(5));
        Assert.Equal(0.3, f.Add(1, T(6)));
    }

    [Fact]
    public void Steady_rises_keep_the_shine_out_and_show_a_fill_sooner()
    {
        var low = Replay("gauge_valk_shine2", GaugeReadMode.SteadyRise);
        for (int i = 0; i < 93; i++) Assert.True(low[i] == 0.22, $"{i / 10.0:0.0} s: {low[i]:0.00}");
        var steady = Replay("gauge_valk_shine", GaugeReadMode.SteadyRise);
        for (int i = 5; i < 20; i++) Assert.InRange(steady[i], 0.15, 0.25);
        for (int i = 45; i < 58; i++) Assert.InRange(steady[i], 0.30, 0.55);
        int First(double[] v) => Array.FindIndex(v, x => x >= 0.36);
        Assert.True(First(steady) < First(Replay("gauge_valk_shine", GaugeReadMode.Agreed)), $"{First(steady)}");
    }

    [Fact]
    public void A_predicted_rise_shows_after_two_reads()
    {
        var f = At(0.2, GaugeReadMode.Predicted);
        f.Expect(0.2, T(5) + TimeSpan.FromSeconds(1));
        f.Add(0.4, T(5));
        Assert.Equal(0.4, f.Add(0.4, T(6)));
    }

    [Fact]
    public void With_no_prediction_a_rise_waits_for_five_reads()
    {
        var f = At(0.2, GaugeReadMode.Predicted);
        f.Add(0.4, T(5));
        Assert.Equal(0.2, f.Add(0.4, T(6)));
    }

    [Fact]
    public void A_rise_bigger_than_predicted_waits()
    {
        var f = At(0.2, GaugeReadMode.Predicted);
        f.Expect(0.1, T(5) + TimeSpan.FromSeconds(1));
        f.Add(0.4, T(5));
        Assert.Equal(0.2, f.Add(0.4, T(6)));
    }

    [Fact]
    public void A_prediction_is_used_up_by_the_rise_it_let_through()
    {
        var f = At(0.2, GaugeReadMode.Predicted);
        f.Expect(0.2, T(5) + TimeSpan.FromSeconds(1));
        f.Add(0.3, T(5));
        Assert.Equal(0.3, f.Add(0.3, T(6)));
        f.Add(0.5, T(7));
        Assert.Equal(0.3, f.Add(0.5, T(8)));
    }

    [Fact]
    public void A_prediction_runs_out()
    {
        var f = At(0.2, GaugeReadMode.Predicted);
        f.Expect(0.2, T(6));
        f.Add(0.4, T(10));
        Assert.Equal(0.2, f.Add(0.4, T(11)));
    }

    [Fact]
    public void Reset_forgets_the_last_reads()
    {
        var f = new GaugeFilter();
        for (int i = 0; i < 5; i++) f.Add(1);
        f.Reset();
        Assert.Equal(0.2, f.Add(0.2));
    }

    // 수라act.mp4 (브레이커 수라의길) at 10 fps from 6.0 s: Z at 6.3 s (its effects flicker the read), then the orb drains about
    // 0.03 a read to 0 at 11.0 s (BreakerSweep, su_act.csv).
    static readonly double[] SuraDrain = [1.00, 1.00, 1.00, 0.00, 0.12, 1.00, 0.31, 0.27, 1.00, 1.00, 1.00, 0.89, 1.00, 0.84, 0.84,
        0.79, 0.77, 0.74, 0.74, 0.70, 0.70, 0.65, 0.65, 0.60, 0.55, 0.55, 0.53, 0.51, 0.48, 0.46, 0.43, 0.41, 0.39, 0.36, 0.34,
        0.31, 0.31, 0.27, 0.24, 0.22, 0.20, 0.17, 0.17, 0.12, 0.12, 0.08, 0.05, 0.03, 0.03, 0.01, 0.00, 0.00, 0.00, 0.00];

    static (double[] Shown, int[] Drained) Drain(double[] reads, bool follows)
    {
        var f = new GaugeFilter { FollowsDrain = follows };
        for (int i = 0; i < 6; i++) f.Add(1);   // full before
        var drained = new List<int>();
        var shown = reads.Select((r, i) => { double v = f.Add(r); if (f.TakeDrainedFromFull()) drained.Add(i); return v; }).ToArray();
        return (shown, [.. drained]);
    }

    /// <summary>Without drain follow a steady fall never gives 5 agreeing reads: the gauge stays full until the orb is empty.</summary>
    [Fact]
    public void A_drain_shows_only_at_its_end_by_default()
    {
        var (shown, drained) = Drain(SuraDrain, follows: false);
        Assert.All(shown[..45], v => Assert.Equal(1.0, v));
        Assert.Empty(drained);
    }

    /// <summary>수라의길 (breaker-aden.plan.md Phase 2): with drain follow the bar is within 0.06 of the read from 7.6 s (two
    /// falls in four reads), and leaving full is reported once, as Z's use.</summary>
    [Fact]
    public void A_followed_drain_tracks_the_read_and_reports_leaving_full_once()
    {
        var (shown, drained) = Drain(SuraDrain, follows: true);
        for (int i = 16; i < SuraDrain.Length; i++) Assert.True(Math.Abs(shown[i] - SuraDrain[i]) <= 0.06, $"{6.0 + i / 10.0:0.0} s: {shown[i]} for {SuraDrain[i]}");
        Assert.All(shown[..13], v => Assert.Equal(1.0, v));   // Z's flicker moves nothing
        Assert.Equal([16], drained);
    }

    /// <summary>One low read, or a single step down, is no drain (and not a use).</summary>
    [Fact]
    public void A_single_dip_on_a_full_orb_is_not_a_drain()
    {
        var (shown, drained) = Drain([1, 1, 1, 0.89, 1, 1, 0.9, 0.9, 1, 1], follows: true);
        Assert.All(shown, v => Assert.Equal(1.0, v));
        Assert.Empty(drained);
    }

    /// <summary>A gauge that drops from full by any path is reported (the drain may be missed), once until it is full again.</summary>
    [Fact]
    public void Leaving_full_is_reported_once_per_fill()
    {
        var f = new GaugeFilter { FollowsDrain = true };
        int fired = 0;
        foreach (var r in new double[] { 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0.5, 0.5, 0.5, 0.5, 0.5, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 })
        {
            f.Add(r);
            if (f.TakeDrainedFromFull()) fired++;
        }
        Assert.Equal(2, fired);
    }

    /// <summary>수라act.mp4 at 10 reads a second (JeonbunSweep.Aden_in_a_video_when_requested): the drain's tail reads
    /// 0.05, 0.03, 0.03, 0.01, 0.00 then empty. The bar ends at 0, not back up at 3% from an older read in the run.</summary>
    [Fact]
    public void A_drain_ends_at_empty_not_at_the_runs_highest_read()
    {
        var (shown, _) = Drain([0.84, 0.79, 0.77, 0.74, 0.70, 0.65, 0.60, 0.55, 0.48, 0.41, 0.34, 0.27, 0.20, 0.12, 0.08, 0.05, 0.03, 0.03, 0.01, 0, 0, 0, 0, 0, 0, 0, 0], follows: true);
        Assert.Equal(0, shown[^1]);
    }

    /// <summary>A use not yet taken is forgotten with the gauge (another character, the HUD hidden).</summary>
    [Fact]
    public void Reset_forgets_a_use_not_yet_taken()
    {
        var f = new GaugeFilter { FollowsDrain = true };
        foreach (var r in new double[] { 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 }) f.Add(r);
        f.Reset();
        Assert.False(f.TakeDrainedFromFull());
    }

    /// <summary>권왕 20.59.43.mp4's second Z (JeonbunSweep.Aden_in_a_video_when_requested): the orb empties at once, Z's flash
    /// reads 0.05, 0.08, 0.03 over it, then empty. The run that takes the drop holds the 0.03; a later run all below it is
    /// shown even though the step is under Spread, so the bar ends at 0, not at 3% until the gauge refills.</summary>
    [Fact]
    public void A_drop_settles_at_empty_after_the_flash()
    {
        var (shown, _) = Drain([1, 1, 1, 1, 1, 0, 0.05, 0.08, 0.03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], follows: false);
        Assert.Equal(0, shown[^1]);
    }

    /// <summary>브레이커's ring (BreakerSweep.Sweep_the_ring_when_requested): 충격 steps about once a second over clean plateaus, with
    /// rare 1–2 read blips. Three agreeing reads take a step 0.2 s after it, and a two-read blip never shows.</summary>
    [Fact]
    public void A_shorter_run_takes_a_step_after_three_reads_and_skips_a_two_read_blip()
    {
        var f = new GaugeFilter { Run = 3 };
        double[] reads = [0.26, 0.26, 0.26, 0.82, 0.82, 0.82, 0.20, 0.20, 0.82, 0.82, 0.82];
        var shown = reads.Select((r, i) => f.Add(r, TimeSpan.FromMilliseconds(100 * i))).ToList();
        Assert.Equal(0.26, shown[4]);
        Assert.Equal(0.82, shown[5]);   // the third 0.82
        Assert.All(shown.Skip(5), s => Assert.Equal(0.82, s));   // the two 0.20s never show
    }
}
