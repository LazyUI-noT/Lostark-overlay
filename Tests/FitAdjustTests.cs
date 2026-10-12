using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>수동 조정: the debug window's live edit of a fit (the skill bar moved and scaled, the buff, debuff and boss rows shifted),
/// and the settings that keep an applied one per game-area size.</summary>
public class FitAdjustTests
{
    static FitAdjust Session(double hud = 100) => FitAdjust.Begin(Fhd, 120, new Fit(hud));

    [Fact]
    public void Moving_the_bar_moves_every_slot_label_buff_and_debuff_box_by_the_same_pixels()
    {
        var a = Session();
        var b = a.Move(AdjustPart.스킬창, 12, -7);
        foreach (var slot in SlotInfo.All)
        {
            var (r0, r1) = (Geometry.Slot(slot, Fhd, a.Current), Geometry.Slot(slot, Fhd, b.Current));
            Assert.InRange(r1.X - r0.X, 11, 13);
            Assert.InRange(r1.Y - r0.Y, -8, -6);
        }
        var (l0, l1) = (Geometry.ToScreen(Geometry.RefAwakeningLabel, Fhd, a.Current), Geometry.ToScreen(Geometry.RefAwakeningLabel, Fhd, b.Current));
        Assert.InRange(l1.X - l0.X, 11, 13);
        Assert.InRange(Geometry.BuffBoxes(Fhd, b.Current, 120)[0].Icon.X - Geometry.BuffBoxes(Fhd, a.Current, 120)[0].Icon.X, 11, 13);
        Assert.InRange(Geometry.DebuffBoxes(Fhd, b.Current, 120)[0].Icon.X - Geometry.DebuffBoxes(Fhd, a.Current, 120)[0].Icon.X, 11, 13);
    }

    [Fact]
    public void Moving_the_bar_leaves_the_boss_row()
    {
        var a = Session();
        Assert.Equal(Geometry.BossRow(Fhd, a.Current), Geometry.BossRow(Fhd, a.Move(AdjustPart.스킬창, 30, 20).Current));
    }

    [Theory]
    [InlineData(AdjustPart.버프)]
    [InlineData(AdjustPart.디버프)]
    [InlineData(AdjustPart.보스)]
    public void Moving_a_row_moves_only_that_row(AdjustPart part)
    {
        var a = Session();
        var b = a.Move(part, 5, 3);
        Assert.Equal(Geometry.BarRect(Fhd, a.Current), Geometry.BarRect(Fhd, b.Current));
        foreach (var other in Enum.GetValues<AdjustPart>().Where(p => p != part && p != AdjustPart.스킬창))
            Assert.Equal(a.RectOf(other), b.RectOf(other));
        var (r0, r1) = (a.RectOf(part), b.RectOf(part));
        Assert.Equal((r0.X + 5, r0.Y + 3, r0.Width, r0.Height), (r1.X, r1.Y, r1.Width, r1.Height));
        Assert.Equal((5.0, 3.0), b.Offset(part));
    }

    /// <summary>The bar's x/y (screen px from where the game puts it) never change with its size: it scales about the HUD's own
    /// anchor, the bottom centre of the game area.</summary>
    [Theory]
    [InlineData(80)]
    [InlineData(130)]
    public void Scaling_the_bar_keeps_its_x_y_pixels(double hud)
    {
        var a = Session(100).Move(AdjustPart.스킬창, 12, -7);
        var b = a.ScaleTo(hud);
        Assert.Equal(hud, b.Current.Hud, 6);
        var (x, y) = b.Offset(AdjustPart.스킬창);
        Assert.Equal(12, x, 6);
        Assert.Equal(-7, y, 6);
    }

    [Fact]
    public void Dragging_the_left_corner_puts_the_bar_edge_under_the_pointer_and_keeps_x_y()
    {
        var a = Session(100).Move(AdjustPart.스킬창, 10, -5);
        var bar = a.RectOf(AdjustPart.스킬창);
        var b = a.DragLeftTo(bar.X - 40);
        Assert.InRange(b.RectOf(AdjustPart.스킬창).X - (bar.X - 40), -1, 1);
        Assert.True(b.Current.Hud > a.Current.Hud);
        var (x, y) = b.Offset(AdjustPart.스킬창);
        Assert.Equal((10.0, -5.0), (Math.Round(x, 6), Math.Round(y, 6)));
    }

    [Fact]
    public void One_pixel_wider_grows_the_bar_one_pixel_and_keeps_x_y()
    {
        var a = Session(100).Move(AdjustPart.스킬창, 3, 0);
        int w = a.RectOf(AdjustPart.스킬창).Width;
        var b = a.WidenBy(1);
        Assert.InRange(b.RectOf(AdjustPart.스킬창).Width - w, 0, 2);
        Assert.Equal(3, b.Offset(AdjustPart.스킬창).X, 6);
    }

    // ── Sizes: the game's 버프 크기 (buff and debuff rows) and the boss row's % ────────────────────────────────────────

    [Fact]
    public void The_buff_size_moves_both_rows_like_the_game_setting_and_reverts()
    {
        var a = Session();
        var b = a.WithBuffSize(100);
        Assert.Equal(100, b.BuffSize);
        Assert.Equal(Geometry.BuffArea(Fhd, a.Current, 100), b.RectOf(AdjustPart.버프));
        Assert.Equal(Geometry.DebuffArea(Fhd, a.Current, 100), b.RectOf(AdjustPart.디버프));
        Assert.True(b.Changed);
        var back = b.Reverted();
        Assert.Equal((120, false), (back.BuffSize, back.Changed));
    }

    [Theory]
    [InlineData(113, 113)]   // ± between the game's choices
    [InlineData(9999, FitAdjust.MaxBuffSize)]
    [InlineData(1, FitAdjust.MinBuffSize)]
    public void The_buff_size_takes_any_whole_percent_in_range(int asked, int got) =>
        Assert.Equal(got, Session().WithBuffSize(asked).BuffSize);

    [Fact]
    public void The_boss_size_grows_about_the_slash_and_its_reads_follow()
    {
        var a = Session();
        var b = a.BossTo(130);
        Assert.Equal((100, 130), (a.BossPercent, b.BossPercent));
        var (old, now) = (a.RectOf(AdjustPart.보스), b.RectOf(AdjustPart.보스));
        double s0 = Geometry.BossScale(Fhd, a.Current), s1 = Geometry.BossScale(Fhd, b.Current);
        Assert.Equal(s0 * 1.3, s1, 6);
        double axis0 = old.X + Geometry.BossAxis * s0, axis1 = now.X + Geometry.BossAxis * s1;
        Assert.InRange(axis1 - axis0, -1, 1);                               // the slash between buffs and debuffs stays
        Assert.InRange(now.Y + DebuffReader.BossIconTop * s1 - (old.Y + DebuffReader.BossIconTop * s0), -1, 1);   // the icons' top too
        Assert.Equal(a.Start, b.Reverted().Current);
    }

    /// <summary>The boss row's box is wide (540 reference px for 32 px icons): its size stops where the box still fits on both sides
    /// of the slash, so a big size never pushes the row off its place (a shift the clamp would keep after shrinking again).</summary>
    [Fact]
    public void A_big_boss_size_stops_where_the_row_still_fits_and_never_moves_it()
    {
        var a = Session();
        var b = a.BossTo(9999);
        Assert.Equal((0.0, 0.0), b.Offset(AdjustPart.보스));
        var r = b.RectOf(AdjustPart.보스);
        Assert.True(r.X >= Fhd.X && r.Right <= Fhd.X + Fhd.Width, r.ToString());
        Assert.True(b.BossPercent > 100);
        Assert.Equal((0.0, 0.0), b.BossTo(60).Offset(AdjustPart.보스));
        Assert.Equal(FitAdjust.MinBoss, a.BossTo(1).BossPercent);
    }

    [Fact]
    public void Clamp_keeps_bar_and_rows_inside_the_game_area()
    {
        var a = Session().Move(AdjustPart.스킬창, 5000, 5000).Move(AdjustPart.버프, -9000, 0).Move(AdjustPart.보스, 0, -9000);
        foreach (var part in Enum.GetValues<AdjustPart>())
        {
            var r = a.RectOf(part);
            Assert.True(r.X >= Fhd.X && r.Y >= Fhd.Y && r.Right <= Fhd.X + Fhd.Width && r.Bottom <= Fhd.Y + Fhd.Height, $"{part} {r}");
        }
    }

    [Fact]
    public void Clamp_limits_the_scale_to_50_250()
    {
        var a = Session();
        Assert.Equal(FitAdjust.MaxHud, a.ScaleTo(99999).Current.Hud);
        Assert.Equal(FitAdjust.MinHud, a.ScaleTo(1).Current.Hud);
    }

    /// <summary>Opening the debug window never changes a fit the game itself gives: every HUD size, screen and buff size the game
    /// offers keeps its bar and rows inside the game area as they are.</summary>
    [Theory]
    [InlineData(0, 0, 1920, 1080)]
    [InlineData(0, 135, 1920, 810)]     // forced 21:9 on 1080p
    [InlineData(0, 0, 3840, 2160)]
    [InlineData(0, 270, 3840, 1620)]    // forced 21:9 on 4K
    [InlineData(1080, 162, 2560, 1440)] // a game away from the monitor's origin
    public void A_fit_the_game_gives_is_unchanged(int x, int y, int w, int h)
    {
        var vp = new Viewport(x, y, w, h);
        foreach (var hud in Settings.HudSizes)
            foreach (var buff in new[] { 80, 100, 120 })
            {
                var fit = new Fit(hud, -2, 1.5);
                Assert.False(FitAdjust.Begin(vp, buff, fit).Changed, $"HUD {hud} buff {buff}");
            }
    }

    [Fact]
    public void PartAt_prefers_the_corner_then_the_smallest_box()
    {
        var a = Session();
        var bar = a.RectOf(AdjustPart.스킬창);
        Assert.Equal((AdjustPart.스킬창, true), a.PartAt(bar.X + 2, bar.Bottom - 2, 10));   // the bottom-left corner scales
        var buff = a.RectOf(AdjustPart.버프);   // inside the bar's box too: the smaller one wins
        Assert.Equal((AdjustPart.버프, false), a.PartAt(buff.X + buff.Width / 2, buff.Y + buff.Height / 2, 10));
        Assert.Equal((AdjustPart.스킬창, false), a.PartAt(bar.X + 30, bar.Bottom - 20, 10));
        Assert.Null(a.PartAt(5, 5, 10));
    }

    [Fact]
    public void Edits_never_change_Start_and_Reverted_gives_Start()
    {
        var a = Session();
        var b = a.Move(AdjustPart.스킬창, 9, 9).Move(AdjustPart.디버프, 4, 0).ScaleTo(120).BossTo(120);
        Assert.Equal(a.Start, b.Start);
        Assert.True(b.Changed);
        Assert.Equal(a.Start, b.Reverted().Current);
        Assert.False(b.Reverted().Changed);
    }

    // ── Settings: the three modes and the applied adjusts ─────────────────────────────────────────────────────────

    [Fact]
    public void Mode_follows_the_flags_and_SetFitMode_sets_them()
    {
        var s = new Settings();
        Assert.Equal(FitMode.자동, s.FitMode);
        s.SetFitMode(FitMode.직접입력);
        Assert.Equal(FitMode.직접입력, s.FitMode);
        Assert.NotNull(s.ManualHud);
        var typed = s.ManualHud;
        s.SetFitMode(FitMode.수동조정);
        Assert.Equal(FitMode.수동조정, s.FitMode);
        Assert.Equal(typed, s.ManualHud);   // 직접 입력's values stay: the game area the adjust was made on
        var fresh = new Settings();
        fresh.SetFitMode(FitMode.수동조정);
        Assert.Null(fresh.ManualHud);   // from 자동: the area is measured
        s.SetFitMode(FitMode.자동);
        Assert.Equal((FitMode.자동, (double?)null, false), (s.FitMode, s.ManualHud, s.UseAdjusts));
    }

    [Fact]
    public void ApplyAdjust_stores_per_size_and_turns_수동_조정_on()
    {
        var s = new Settings();
        var fit = new Fit(95, 1, 2, new Shift(3, 4));
        s.ApplyAdjust("1920×1080", fit);
        Assert.Equal(FitMode.수동조정, s.FitMode);
        Assert.Equal(fit, s.Adjusts["1920×1080"]);
        Assert.True(s.AdjustActive("1920×1080"));
        Assert.False(s.AdjustActive("2560×1440"));
        Assert.False(s.AdjustActive(null));
    }

    [Fact]
    public void Adjusts_reach_the_engine_only_in_수동_조정()
    {
        var s = new Settings();
        s.ApplyAdjust("1920×1080", new Fit(95));
        Assert.Equal(new Fit(95), s.ActiveAdjusts()!["1920×1080"]);
        s.SetFitMode(FitMode.자동);
        Assert.Null(s.ActiveAdjusts());
        Assert.True(s.Adjusts.ContainsKey("1920×1080"));   // kept for when 수동 조정 is picked again
        s.SetFitMode(FitMode.직접입력);
        Assert.Null(s.ActiveAdjusts());
    }

    [Fact]
    public void DiscardAdjust_drops_that_size_and_returns_to_자동()
    {
        var s = new Settings();
        s.ApplyAdjust("1920×1080", new Fit(95));
        s.ApplyAdjust("1920×810", new Fit(90));
        s.DiscardAdjust("1920×1080");
        Assert.Equal(FitMode.자동, s.FitMode);
        Assert.False(s.Adjusts.ContainsKey("1920×1080"));
        Assert.True(s.Adjusts.ContainsKey("1920×810"));
    }
}
