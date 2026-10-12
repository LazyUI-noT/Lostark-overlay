using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using static GaugeOverlay.Tests.Helpers;

namespace GaugeOverlay.Tests;

/// <summary>모르둠's 결투자 debuff: the icon read in the debuff row, and its own clock (a 4 → 0 countdown, then the icon,
/// all within 30 s of the first sighting, whatever is read after that).</summary>
public class DuelistTests
{
    // reference/결투자.png (fixture "duelist", 143×81, buff about 110%): the blue sigil at the left, 결투자 at the right.
    static readonly PixelRect SigilIcon = new(38, 30, 29, 29), DuelIcon = new(71, 30, 29, 29);

    static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);
    static TimeSpan At(double s) => T0 + TimeSpan.FromSeconds(s);
    static readonly DebuffReading Seen = new(DebuffKind.결투자, null);

    static DebuffStatus Update(DebuffStatus prev, double s, bool seen = false, bool hud = true) =>
        DebuffTracker.Update(prev, seen ? [Seen] : [], null, null, At(s), hud);

    static DuelPhase Phase(DebuffStatus s, double at) => DuelPhase.At(s.Duelist, At(at));

    // ── Reading ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_right_icon_of_the_reference_shot_reads_as_결투자_and_the_left_does_not()
    {
        var shot = Fixture.Load("duelist").Frames[0];
        Assert.Equal(DebuffKind.결투자, DebuffReader.Classify(shot, DuelIcon));
        Assert.Equal(DebuffKind.파랑, DebuffReader.Classify(shot, SigilIcon));
    }

    [Fact]
    public void A_결투자_icon_under_a_glare_reads_as_nothing_and_not_as_the_red_sigil()
    {
        // The red sigil is its closest look-alike (0.64 against the 빨강 template, hue 0°): a dark band over the icon's top
        // 12 rows drops 결투자 to 0.75, under its bar of 0.85, with 빨강 at 0.68. Neither may be reported.
        var shot = Fixture.Load("duelist").Frames[0];
        var hidden = new Image(shot.Width, shot.Height, (byte[])shot.Bgra.Clone());
        for (int y = DuelIcon.Y; y < DuelIcon.Y + 12; y++)
            for (int x = DuelIcon.X; x < DuelIcon.Right; x++)
                hidden.Bgra.AsSpan(y * hidden.Stride + x * 4, 3).Fill(40);
        Assert.Null(DebuffReader.Classify(hidden, DuelIcon));
    }

    // The png's icon is buff 110%, the red sigil box (debuffrow_3) buff 120%, both at 1080p; the buff size and the screen scale
    // (2 at 4K) enlarge or shrink each.
    [Theory]
    [InlineData(80, 1.0)]
    [InlineData(100, 1.0)]
    [InlineData(120, 1.0)]
    [InlineData(120, 2.0)]   // 4K
    public void 결투자_reads_at_every_buff_size_and_the_red_sigil_stays_the_red_sigil(int buff, double screen)
    {
        PixelRect Scaled(PixelRect r, double f) => new((int)Math.Round(r.X * f), (int)Math.Round(r.Y * f), (int)Math.Round(r.Width * f), (int)Math.Round(r.Height * f));
        var shot = Fixture.Load("duelist").Frames[0];
        double fd = screen * buff / 110;
        var duel = BuffReader.Resample(shot, new PixelRect(0, 0, shot.Width, shot.Height), fd);
        Assert.Equal(DebuffKind.결투자, DebuffReader.Classify(duel, Scaled(DuelIcon, fd)));

        var row = Fixture.Load("debuffrow_3").Frames[0];
        double fr = screen * buff / 120;
        var red = BuffReader.Resample(row, new PixelRect(0, 0, row.Width, row.Height), fr);
        var box = Geometry.DebuffBoxes(Fhd, new Fit(110), 120)[2].Icon;
        Assert.Equal(DebuffKind.빨강, DebuffReader.Classify(red, Scaled(box with { X = box.X - 1060, Y = box.Y - 880 }, fr)));
    }

    // ── The debug window's reading ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_debug_row_names_a_near_miss_of_결투자_and_the_score_of_the_reading_is_the_templates()
    {
        var shot = Fixture.Load("duelist").Frames[0];
        Assert.Equal(DebuffKind.결투자, DebuffReader.Classify(shot, DuelIcon, out double own));
        Assert.InRange(own, 0.95, 1);

        var hidden = new Image(shot.Width, shot.Height, (byte[])shot.Bgra.Clone());
        for (int y = DuelIcon.Y; y < DuelIcon.Y + 12; y++)
            for (int x = DuelIcon.X; x < DuelIcon.Right; x++)
                hidden.Bgra.AsSpan(y * hidden.Stride + x * 4, 3).Fill(40);
        Assert.Null(DebuffReader.Classify(hidden, DuelIcon, out double near));
        Assert.InRange(near, 0.5, 0.85);
        Assert.Equal($"없음 (결투자 {near:0.00})", DebuffReader.DebugText(null, near));
    }

    [Theory]
    [InlineData(null, 0.78, "없음 (결투자 0.78)")]
    [InlineData(null, 0.5, "없음")]
    [InlineData(null, 0.2, "없음")]
    [InlineData(DebuffKind.빨강, 0.68, "빨강 (결투자 0.68)")]
    [InlineData(DebuffKind.빨강, 0.4, "빨강")]
    [InlineData(DebuffKind.결투자, 0.98, "결투자")]
    [InlineData(DebuffKind.번개, 0.7, "번개")]
    [InlineData(DebuffKind.파랑, 0.7, "파랑")]
    public void The_debug_text_adds_the_결투자_score_only_to_nothing_or_the_red_sigil_above_half(DebuffKind? kind, double score, string text)
    {
        Assert.Equal(text, DebuffReader.DebugText(kind, score));
    }

    [Fact]
    public void The_결투자_icon_is_a_25_px_red_burst()
    {
        var icon = DebuffReader.Icon(DebuffKind.결투자);
        Assert.Equal((25, 25), (icon.Width, icon.Height));
        double b = Enumerable.Range(0, 625).Average(p => icon.Bgra[p * 4]), r = Enumerable.Range(0, 625).Average(p => icon.Bgra[p * 4 + 2]);
        Assert.True(r > b * 1.5, $"B {b:0} R {r:0}");
    }

    // ── The countdown and the hold ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void No_sighting_shows_nothing()
    {
        var s = Update(DebuffStatus.None, 0);
        Assert.Null(s.Duelist);
        Assert.Equal(DuelStage.None, DuelPhase.At(null, T0).Stage);
        Assert.Equal(DuelStage.None, Phase(s, 3).Stage);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(0.99, 4)]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    [InlineData(3.5, 1)]
    [InlineData(4, 0)]
    [InlineData(4.99, 0)]
    public void The_countdown_runs_4_to_0_a_second_each(double after, int number)
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        Assert.Equal(new DuelPhase(DuelStage.Countdown, number), Phase(s, after));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(5.01)]
    [InlineData(17)]
    [InlineData(29.99)]
    public void After_the_0_the_icon_alone_stays_until_30_seconds(double after)
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        Assert.Equal(DuelStage.Icon, Phase(s, after).Stage);
    }

    [Fact]
    public void The_box_clears_exactly_30_seconds_after_the_first_sighting()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        Assert.Equal(DuelStage.None, Phase(s, 30).Stage);
        Assert.Equal(DuelStage.None, Phase(s, 31).Stage);
    }

    [Fact]
    public void A_clock_before_the_first_sighting_shows_nothing()
    {
        Assert.Equal(DuelStage.None, DuelPhase.At(new DuelMark(At(5)), At(4)).Stage);
    }

    // ── The 30 s are the app's own, whatever is read ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_sighting_keeps_the_same_mark_while_it_is_read_again()
    {
        var first = Update(DebuffStatus.None, 0, seen: true);
        var again = Update(first, 0.1, seen: true);
        Assert.Equal(first, again);   // no change to publish
    }

    [Fact]
    public void The_icon_stays_after_it_is_gone_from_the_row_until_30_seconds()
    {
        // A cutscene: read at 0, never again.
        var s = Update(DebuffStatus.None, 0, seen: true);
        foreach (double t in new[] { 0.1, 0.7, 6, 20, 29.9 })
        {
            s = Update(s, t);
            Assert.Equal(DuelStage.Icon, Phase(s, Math.Max(t, 5)).Stage);
        }
        s = Update(s, 30);
        Assert.Null(s.Duelist);
    }

    [Fact]
    public void Reappearing_inside_the_30_seconds_does_not_restart_the_countdown()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        s = Update(s, 10);                      // gone (cutscene)
        s = Update(s, 12, seen: true);          // and back
        Assert.Equal(T0, s.Duelist!.FirstSeenAt);
        Assert.Equal(DuelStage.Icon, Phase(s, 12).Stage);
        Assert.Equal(DuelStage.None, Phase(s, 30).Stage);   // not 12 + 30
    }

    [Fact]
    public void Reappearing_during_the_countdown_does_not_restart_it_either()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        s = Update(s, 1.5);
        s = Update(s, 2.2, seen: true);
        Assert.Equal(new DuelPhase(DuelStage.Countdown, 2), Phase(s, 2.2));
    }

    [Fact]
    public void A_hidden_hud_neither_pauses_nor_shifts_the_clock()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        s = Update(s, 5, hud: false);           // a loading screen or cutscene
        s = Update(s, 20, hud: false);
        Assert.Equal(T0, s.Duelist!.FirstSeenAt);
        s = Update(s, 25);                      // the HUD is back, the icon is not read yet
        Assert.Equal(DuelStage.Icon, Phase(s, 25).Stage);
        Assert.Equal(DuelStage.None, Phase(s, 30).Stage);
    }

    [Fact]
    public void A_hud_hidden_past_the_30_seconds_ends_the_cycle()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        s = Update(s, 31, hud: false);
        Assert.Null(s.Duelist);
    }

    [Fact]
    public void A_sighting_after_the_icon_was_absent_past_the_30_seconds_starts_a_new_cycle()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        s = Update(s, 31);                       // a read without it
        Assert.Null(s.Duelist);
        s = Update(s, 31.1, seen: true);
        Assert.Equal(At(31.1), s.Duelist!.FirstSeenAt);
        Assert.Equal(new DuelPhase(DuelStage.Countdown, 4), Phase(s, 31.1));
    }

    [Fact]
    public void The_30_second_boundary_is_exact()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        s = Update(s, 29.99, seen: true);
        Assert.Equal(DuelStage.Icon, Phase(s, 29.99).Stage);
        s = Update(s, 30, seen: true);
        Assert.Equal(DuelStage.None, Phase(s, 30).Stage);
    }

    // ── One appearance is one cycle: the 30 s is a hard cap ───────────────────────────────────────────────────────

    [Fact]
    public void Sightings_in_a_continuous_run_through_the_30_seconds_never_restart_the_cycle()
    {
        // The game's own timer runs 38 s or more: the icon is still read after our 30 s are over.
        var s = DebuffStatus.None;
        for (double t = 0; t <= 45; t += 0.5)
        {
            s = Update(s, t, seen: true);
            if (t >= 30) Assert.Equal(DuelStage.None, Phase(s, t).Stage);
            Assert.Equal(T0, s.Duelist!.FirstSeenAt);
        }
    }

    [Fact]
    public void The_status_changes_once_when_the_30_seconds_end_so_a_republish_clears_the_box()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        s = Update(s, 29.9, seen: true);
        var after = Update(s, 30, seen: true);
        Assert.NotEqual(s, after);
        Assert.Equal(after, Update(after, 30.1, seen: true));   // and then it stays
    }

    [Fact]
    public void One_read_without_the_icon_after_the_30_seconds_lets_the_next_sighting_start_a_new_cycle()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        for (double t = 0.5; t <= 33; t += 0.5) s = Update(s, t, seen: true);
        s = Update(s, 33.5);                     // one read without it
        s = Update(s, 36, seen: true);
        Assert.Equal(At(36), s.Duelist!.FirstSeenAt);
        Assert.Equal(new DuelPhase(DuelStage.Countdown, 4), Phase(s, 36));
    }

    [Theory]
    [InlineData(false)]   // reads came every second
    [InlineData(true)]    // no read between the last miss and the sighting
    public void Absent_from_20_s_and_seen_again_at_35_s_starts_a_new_cycle(bool readsBetween)
    {
        // Ambiguous by nature: after our 30 s are over this cannot be told from a new debuff, so it is one.
        var s = Update(DebuffStatus.None, 0, seen: true);
        s = Update(s, 20);
        if (readsBetween) foreach (double t in new[] { 25.0, 31, 34 }) s = Update(s, t);
        s = Update(s, 35, seen: true);
        Assert.Equal(At(35), s.Duelist!.FirstSeenAt);
        Assert.Equal(new DuelPhase(DuelStage.Countdown, 4), Phase(s, 35));
    }

    [Fact]
    public void A_sighting_at_exactly_30_s_after_a_miss_inside_the_30_seconds_starts_a_new_cycle()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        s = Update(s, 29.9);
        s = Update(s, 30, seen: true);
        Assert.Equal(At(30), s.Duelist!.FirstSeenAt);
    }

    [Fact]
    public void The_first_read_after_a_hidden_hud_past_the_30_seconds_is_a_gap_too()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        for (double t = 1; t <= 29; t++) s = Update(s, t, seen: true);
        s = Update(s, 32, hud: false);
        s = Update(s, 33, seen: true);
        Assert.Equal(At(33), s.Duelist!.FirstSeenAt);
    }

    // ── The other debuffs are untouched ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Reading_결투자_marks_no_other_debuff_and_the_other_debuffs_no_결투자()
    {
        var s = Update(DebuffStatus.None, 0, seen: true);
        Assert.Equal((null, null, null, null), (s.Blue, s.Red, s.Lightning, s.Swap));
        s = DebuffTracker.Update(DebuffStatus.None, [new(DebuffKind.번개, 6), new(DebuffKind.교체, 3), new(DebuffKind.빨강, null)], 6, 2, T0);
        Assert.Null(s.Duelist);
    }

    [Fact]
    public void 결투자_and_the_lightning_are_both_kept_so_the_lightning_can_be_drawn()
    {
        var s = DebuffTracker.Update(DebuffStatus.None, [Seen, new(DebuffKind.번개, 6)], null, null, T0);
        Assert.NotNull(s.Duelist);
        Assert.Equal(6, s.Lightning!.Seconds(T0));
    }

    [Fact]
    public void The_other_debuffs_still_hold_half_a_second_beside_a_running_cycle()
    {
        var s = DebuffTracker.Update(DebuffStatus.None, [Seen, new(DebuffKind.번개, 6)], null, null, T0);
        s = DebuffTracker.Update(s, [], null, null, At(0.6));
        Assert.Null(s.Lightning);
        Assert.NotNull(s.Duelist);
    }
}
