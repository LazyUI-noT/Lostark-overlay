using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The game's icon counts per row (boss-row-count.plan.md, phase 1): the size table, the 표시 numbers and the 감지 개수 migration.</summary>
public class IconCountTests
{
    [Theory]
    // (buff size, HUD, most): the user's HUD × buff table, 2026-11-19
    [InlineData(120, 110, 12)]
    [InlineData(120, 105, 12)]
    [InlineData(120, 100, 14)]
    [InlineData(120, 90, 15)]
    [InlineData(120, 80, 17)]
    [InlineData(110, 105, 13)]
    [InlineData(100, 105, 14)]
    [InlineData(90, 105, 16)]
    [InlineData(80, 105, 17)]
    [InlineData(100, 100, 15)]      // HUD 100 otherwise follows the buff table
    [InlineData(110, 100, 13)]
    [InlineData(90, 90, 17)]
    [InlineData(110, 90, 16)]
    [InlineData(90, 110, 15)]       // HUD 110: 17 / 15 / 14 / 13 / 12
    [InlineData(80, 110, 17)]
    [InlineData(100, 90, 17)]       // HUD 90: 100 and below are all 17
    [InlineData(60, 90, 17)]        // under 80: the most
    [InlineData(80, 80, 17)]        // HUD 80: 120 and below are all 17
    [InlineData(120, 110.05, 12)]   // a HUD read off the bar within the 0.5 tolerance: still 110
    [InlineData(100, 102, 14)]      // between presets: the next one up (HUD 105's row)
    [InlineData(85, 90, 17)]        // buff between presets: the next one up (90)
    [InlineData(95, 100, 15)]
    [InlineData(150, 80, 17)]       // over 120: the least of the row
    [InlineData(120, 100.3, 14)]    // a measured HUD within half a percent of a preset is that preset (not 105's 12)
    [InlineData(120, 89.8, 15)]     // and below it too (90, not 80's 17)
    [InlineData(120, 100.5, 14)]    // the tolerance edge: still 100
    [InlineData(120, 100.51, 12)]   // just past it: 105's row
    [InlineData(120, 75, 17)]       // HUD under 80: HUD 80's row
    [InlineData(120, 115, 12)]      // HUD over 110.5: HUD 110's row
    public void The_size_and_HUD_fix_the_most_icons_a_row_shows(int size, double hud, int most) =>
        Assert.Equal(most, Geometry.MaxIcons(size, hud));

    [Theory]
    [InlineData(120, null, 12)]   // unset: the size's maximum
    [InlineData(120, 17, 12)]     // a stored number over the maximum is capped, not lost
    [InlineData(120, 10, 10)]
    [InlineData(80, 13, 13)]
    [InlineData(80, null, 17)]
    public void Shown_is_the_chosen_count_capped_by_the_size(int size, int? chosen, int expected) =>
        Assert.Equal(expected, Geometry.Shown(size, chosen, 110));

    [Fact]
    public void HUD_is_the_live_fit_then_the_typed_then_the_game_areas_fit_then_100()
    {
        var s = new Settings();
        s.Fits["1920x1080"] = new Fit(90);
        Assert.Equal(90, s.HudFor(null, "1920x1080"));
        Assert.Equal(100, s.HudFor(null, "2560x1440"));   // a key without a fit
        Assert.Equal(100, s.HudFor(null, null));
        s.ManualHud = 105;
        Assert.Equal(105, s.HudFor(null, "1920x1080"));   // the typed HUD wins over the area's fit
        Assert.Equal(110, s.HudFor(110, "1920x1080"));    // the engine's live fit wins over all
    }

    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(105)]
    [InlineData(110)]
    public void Daenantu_keeps_fourteen_slots_at_every_HUD(int hud) =>
        Assert.Equal(14, Geometry.DaenantuSlotsIn(new Viewport(0, 0, 1920, 1080), new Fit(hud)).Most);

    [Fact]
    public void Buff_counts_follow_the_HUD()
    {
        var b = new BuffSettings { Size = 120, Count = 17 };
        var at = b.RowAt();
        Assert.Equal((14, 12), (b.ReadCount(100), b.ReadCount(110)));
        Assert.Equal((14, 12), (Geometry.Shown(b.Size, b.Shown, 100), Geometry.Shown(b.Size, b.Shown, 110)));
        Assert.Equal((14, 14), at(100));
        Assert.Equal((12, 12), at(110));
        b.Count = 5;   // RowAt keeps the copy it was made from
        Assert.Equal((14, 14), at(100));
        Assert.Equal((5, 14), b.RowAt()(100));
    }
    [Fact]
    public void Slots_read_the_shown_count_and_identify_the_smaller_of_it_and_the_detection_count()
    {
        var vp = new Viewport(0, 0, 1920, 1080);
        var all = Geometry.BossSlotsIn(vp, new Fit(110), 110);
        Assert.Equal(13, all.Most);
        Assert.Equal(13, all.Reads);
        var three = Geometry.BossSlotsIn(vp, new Fit(110), 110, null, 3);
        Assert.Equal((13, 3, 3), (three.Most, three.Identify, three.Reads));
        var shown = Geometry.BossSlotsIn(vp, new Fit(110), 110, 11, 17);
        Assert.Equal((11, 11), (shown.Most, shown.Reads));
    }

    [Fact]
    public void The_read_counts_follow_the_size_and_the_chosen_shown_count()
    {
        var d = new DebuffSettings { BossIconSize = 120, BossCount = 17 };
        Assert.Equal(12, d.ReadCount(110));
        d.BossShown = 10;
        Assert.Equal(10, d.ReadCount(110));
        d.BossCount = 4;
        Assert.Equal(4, d.ReadCount(110));
        var b = new BuffSettings { Size = 80, Count = 7 };
        Assert.Equal(7, b.ReadCount(110));
        b.Shown = 12;
        b.Count = 17;
        Assert.Equal(12, b.ReadCount(110));
    }

    [Fact]
    public void A_saved_fourteen_becomes_seventeen_once_and_other_numbers_stay()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<Settings>("""{"Debuffs":{"BossCount":14}}""")!;
        Assert.Equal(17, SettingsStore.Normalize(old).Debuffs.BossCount);
        Assert.True(old.ShownMigrated);
        old.Debuffs.BossCount = 14;   // chosen on purpose after the migration
        Assert.Equal(14, SettingsStore.Normalize(old).Debuffs.BossCount);
        var other = System.Text.Json.JsonSerializer.Deserialize<Settings>("""{"Debuffs":{"BossCount":9}}""")!;
        Assert.Equal(9, SettingsStore.Normalize(other).Debuffs.BossCount);
    }

    [Fact]
    public void A_new_install_is_not_migrated() => Assert.Equal(17, new Settings().Debuffs.BossCount);

    [Fact]
    public void Counts_outside_the_game_range_are_repaired()
    {
        var s = new Settings { ShownMigrated = true };
        (s.Debuffs.BossCount, s.Buffs.Count, s.Debuffs.BossShown, s.Buffs.Shown) = (40, 40, 9, 18);
        SettingsStore.Normalize(s);
        Assert.Equal((17, 17, null, null), (s.Debuffs.BossCount, s.Buffs.Count, s.Debuffs.BossShown, s.Buffs.Shown));
        (s.Debuffs.BossShown, s.Buffs.Shown) = (10, 17);
        SettingsStore.Normalize(s);
        Assert.Equal((10, 17), (s.Debuffs.BossShown, s.Buffs.Shown));
    }

    public static TheoryData<int, int, int, int> Matrix()
    {
        var data = new TheoryData<int, int, int, int>();
        foreach (var size in new[] { 80, 90, 100, 110, 120 })
            foreach (var hud in new[] { 80, 110 })
                for (int v = 0; v < Viewports.Length; v++) data.Add(size, hud, v, 0);
        return data;
    }

    static readonly Viewport[] Viewports =
    [
        new(0, 0, 1920, 1080), new(0, 0, 3840, 2160), new(0, 0, 2560, 1080), new(0, 135, 1920, 810), new(100, 50, 1600, 900),
    ];

    [Theory]
    [MemberData(nameof(Matrix))]
    public void The_boss_strip_stays_in_the_viewport_and_right_of_the_slash(int size, int hud, int vpIndex, int _)
    {
        var vp = Viewports[vpIndex];
        var fit = new Fit(hud);
        var strip = Geometry.BossDebuffStrip(vp, fit, size);
        Assert.True(strip.X >= vp.X && strip.Right <= vp.X + vp.Width, $"strip {strip} outside {vp}");
        Assert.True(Geometry.FirstBossDebuffX(vp, fit) > Geometry.BossSlashX(vp, fit));
        var last = Geometry.BossDebuffSlots(vp, fit, size)[^1].Icon;
        Assert.True(last.Right <= strip.Right);
        Assert.Equal(Geometry.MaxIcons(size, hud), Geometry.BossDebuffSlots(vp, fit, size).Count);
    }
}
