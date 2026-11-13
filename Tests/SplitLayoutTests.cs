using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>합쳐서 / 따로, the model and the one-time migration (class-profiles.plan.md "합쳐서 / 따로", phase 7).</summary>
public class SplitLayoutTests
{
    const string Lone = "워로드 · 고독한기사", Combat = "워로드 · 전투태세", Bard = "바드 · 각성", NoGauge = "도화가 · 회귀";
    static readonly IReadOnlySet<string> Gauged = new HashSet<string> { Lone, Combat, Bard };
    const double Gap = FreeLayout.Gap;

    static double Shift(double thickness, double height) => SplitLayout.GaugeHeight(thickness, SplitLayout.BoxSize(height)) + Gap;

    static Settings Fresh()
    {
        var s = new Settings { SplitMigrated = false };
        foreach (var k in new[] { Lone, Combat, Bard, NoGauge })
        {
            ClassProfiles.Entry(s, k).Own = true;
            ClassProfiles.Entry(s, k).Mine.Gauge = true;
        }
        return s;
    }

    static ProfileEntry Entry(double top, bool detached, double height = 1, double thickness = 6, bool side = false) => new()
    {
        Overlay = new() { Left = 100, Top = top, Height = height }, GaugeThickness = thickness, BuffsDetached = detached, BuffsSide = side,
    };

    [Fact]
    public void Gauge_height_follows_thickness_and_box_height()
    {
        Assert.Equal(6, SplitLayout.GaugeHeight(6, 34));
        Assert.Equal(12, SplitLayout.GaugeHeight(6, 68));
        Assert.Equal(2, SplitLayout.GaugeHeight(1, 10));   // never thinner than 2
    }

    [Fact]
    public void Gauge_size_swaps_when_upright_and_scales_with_the_window_sizes()
    {
        var g = new GaugeStyle { Width = 1, Height = 1, Thickness = 6 };
        var flat = SplitLayout.GaugeSize(g);
        g.Upright = true;
        Assert.Equal((flat.H, flat.W), SplitLayout.GaugeSize(g));
        g.Upright = false;
        g.Width = 2;
        Assert.Equal(flat.W * 2, SplitLayout.GaugeSize(g).W);
    }

    [Fact]
    public void Upright_buffs_need_both_side_and_apart()
    {
        var b = new BuffSettings { Side = true };
        Assert.True(b.Upright(true));
        Assert.False(b.Upright(false));
        b.Side = false;
        Assert.False(b.Upright(true));
    }

    [Fact]
    public void Gauge_window_has_two_sizes_swapped_when_upright()
    {
        var s = new Settings();
        Assert.Equal(SizeMode.Two, WindowSizing.ModeOf(s, SizedWindow.Gauge));
        Assert.Equal((1.5, 1.0), WindowSizing.Resize(s, SizedWindow.Gauge, SizeHandle.Right, (1, 1), (100, 50), 50, 0));
        s.Gauge.Upright = true;
        // upright: the screen's width is the thickness (Height), its height the length (Width)
        Assert.Equal((1.0, 1.5), WindowSizing.Resize(s, SizedWindow.Gauge, SizeHandle.Right, (1, 1), (100, 50), 50, 0));
    }

    [Fact]
    public void Migrate_shifts_each_class_entry_by_its_gauge()
    {
        var s = Fresh();
        var p = s.Profiles[0];
        p.Classes = new() { [Lone] = Entry(500, true, height: 1, thickness: 6), [Combat] = Entry(500, true, height: 2, thickness: 10) };

        Assert.True(SplitLayout.Migrate(s, Gauged));

        Assert.Equal(500 + Shift(6, 1), p.Classes[Lone].Overlay.Top);
        Assert.Equal(500 + Shift(10, 2), p.Classes[Combat].Overlay.Top);
        Assert.NotEqual(p.Classes[Lone].Overlay.Top, p.Classes[Combat].Overlay.Top);
        Assert.Equal(p.Classes[Combat].Overlay.Top, p.SubclassPlaces[Combat].Top);   // the 0.22 mirror
        Assert.Equal(100, p.Classes[Lone].Overlay.Left);
    }

    [Fact]
    public void Shared_entry_materialises_gauge_classes()
    {
        var s = Fresh();
        var p = s.Profiles[0];
        p.Overlay = Entry(300, true).Overlay;
        (p.BuffsDetached, p.GaugeThickness) = (true, 6);
        p.Classes = [];

        SplitLayout.Migrate(s, Gauged);

        Assert.Equal(300, p.Overlay.Top);   // the shared one serves classes without a gauge: not moved
        Assert.All(Gauged, k => Assert.Equal(300 + Shift(6, 1), p.Classes![k].Overlay.Top));
        Assert.DoesNotContain(NoGauge, p.Classes!.Keys);
    }

    [Fact]
    public void A_class_with_its_gauge_off_or_without_one_stays_put()
    {
        var s = Fresh();
        ClassProfiles.Edit(s, Lone).Mine.Gauge = false;
        var p = s.Profiles[0];
        p.Classes = new() { [Lone] = Entry(500, true), [NoGauge] = Entry(500, true) };

        SplitLayout.Migrate(s, Gauged);

        Assert.Equal(500, p.Classes[Lone].Overlay.Top);
        Assert.Equal(500, p.Classes[NoGauge].Overlay.Top);
    }

    [Fact]
    public void Flat_buffs_in_the_plate_open_합쳐서_and_nothing_moves()
    {
        var s = Fresh();
        var p = s.Profiles[0];
        p.Overlay.Top = 300;
        p.Classes = new() { [Lone] = Entry(500, false) };

        SplitLayout.Migrate(s, Gauged);

        Assert.False(p.BuffsDetached);
        Assert.False(p.Classes[Lone].BuffsDetached);
        Assert.Equal(500, p.Classes[Lone].Overlay.Top);
        Assert.Single(p.Classes);   // no entry made
    }

    [Fact]
    public void Upright_bars_beside_the_plate_open_따로()
    {
        var s = Fresh();
        var p = s.Profiles[0];
        p.Classes = new() { [Lone] = Entry(500, false, side: true) };
        p.Classes[Lone].Buffs.Left = 40;

        SplitLayout.Migrate(s, Gauged);

        Assert.True(p.Classes[Lone].BuffsDetached);
        Assert.True(p.Classes[Lone].BuffsSide);
        Assert.Equal(40, p.Classes[Lone].Buffs.Left);   // the buff window stays where it was
        Assert.Equal(500 + Shift(6, 1), p.Classes[Lone].Overlay.Top);
    }

    [Fact]
    public void An_unplaced_spot_stays_default()
    {
        var s = Fresh();
        var e = Entry(double.NaN, true);
        s.Profiles[0].Classes = new() { [Lone] = e };

        SplitLayout.Migrate(s, Gauged);

        Assert.True(double.IsNaN(e.Overlay.Top));
    }

    [Fact]
    public void Live_settings_move_with_their_class()
    {
        var s = Fresh();
        s.PlaceKey = Combat;
        (s.Overlay.Top, s.Buffs.Detached) = (400, true);

        SplitLayout.Migrate(s, Gauged);

        Assert.Equal(400 + Shift(6, 1), s.Overlay.Top);
    }

    [Fact]
    public void Free_layout_moves_by_the_gap_the_gauge_leaves()
    {
        var s = Fresh();
        var cells = FreeLayout.Default(6);
        ClassProfiles.Edit(s, Lone).Mine.Layout = cells;
        s.Profiles[0].Classes = new() { [Lone] = Entry(500, true) };

        SplitLayout.Migrate(s, Gauged);

        double top = Math.Min(cells[Part.변화].Y, cells[Part.초각스].Y);   // the boxes' top without the gauge
        Assert.Equal(500 + Math.Round(top), s.Profiles[0].Classes![Lone].Overlay.Top);
    }

    [Fact]
    public void Free_layout_with_the_gauge_below_the_boxes_moves_nothing()
    {
        var s = Fresh();
        var cells = FreeLayout.Default(6);
        cells[Part.게이지] = cells[Part.게이지] with { Y = 500 };
        ClassProfiles.Edit(s, Lone).Mine.Layout = cells;
        s.Profiles[0].Classes = new() { [Lone] = Entry(500, true) };

        SplitLayout.Migrate(s, Gauged);

        Assert.Equal(500, s.Profiles[0].Classes![Lone].Overlay.Top);
    }

    [Fact]
    public void Migrate_runs_once()
    {
        var s = Fresh();
        s.Profiles[0].Classes = new() { [Lone] = Entry(500, true) };

        Assert.True(SplitLayout.Migrate(s, Gauged));
        double moved = s.Profiles[0].Classes![Lone].Overlay.Top;
        Assert.False(SplitLayout.Migrate(s, Gauged));

        Assert.Equal(moved, s.Profiles[0].Classes![Lone].Overlay.Top);
        Assert.True(s.SplitMigrated);
    }

    [Fact]
    public void A_new_install_and_a_saved_file_need_no_migration()
    {
        Assert.True(new Settings().SplitMigrated);
        Assert.True(SettingsStore.Normalize(Reload(new Settings())).SplitMigrated);
    }

    [Fact]
    public void A_022_file_without_the_marker_is_unmigrated()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(new Settings(), SettingsStore.Json))!.AsObject();
        json.Remove(nameof(Settings.SplitMigrated));

        Assert.False(JsonSerializer.Deserialize<Settings>(json.ToJsonString(), SettingsStore.Json)!.SplitMigrated);
    }

    /// <summary>0.22 skips members it doesn't know, so the new ones are only extra; a file without them fills from live.</summary>
    [Fact]
    public void Downgrade_022_reads_the_new_file_and_an_old_file_fills_the_new_fields()
    {
        var s = new Settings();
        s.Profiles[0].GaugePlace = new() { Left = 5, Top = 6, Width = 1.5, Height = 0.5 };
        s.Profiles[0].GaugeUpright = true;
        s.Gauge.Upright = true;

        var back = Reload(s);
        Assert.Equal(1.5, back.Profiles[0].GaugePlace!.Width);
        Assert.True(back.Profiles[0].GaugeUpright);
        Assert.True(back.Gauge.Upright);

        Assert.Equal(JsonUnmappedMemberHandling.Skip, SettingsStore.Json.UnmappedMemberHandling);
        var node = JsonNode.Parse(JsonSerializer.Serialize(s, SettingsStore.Json))!.AsObject();
        foreach (var p in node["Profiles"]!.AsArray().Select(x => x!.AsObject())) { p.Remove("GaugePlace"); p.Remove("GaugeUpright"); }
        foreach (var k in new[] { "Left", "Top", "Width", "Height", "Upright" }) node["Gauge"]!.AsObject().Remove(k);
        node.Remove("SplitMigrated");
        var old = SettingsStore.Normalize(JsonSerializer.Deserialize<Settings>(node.ToJsonString(), SettingsStore.Json)!);

        Assert.NotNull(old.Profiles[0].GaugePlace);
        Assert.False(old.Profiles[0].GaugeUpright);
        Assert.False(old.SplitMigrated);
    }

    [Fact]
    public void Profile_places_carry_the_gauge_window()
    {
        var s = new Settings();
        s.Gauge.Left = 77;
        s.Gauge.Upright = true;
        var e = new ProfileEntry();
        e.CaptureFrom(s);
        var other = new Settings();
        e.ApplyTo(other);

        Assert.Equal(77, other.Gauge.Left);
        Assert.True(other.Gauge.Upright);
        Assert.Contains(nameof(ProfileEntry.GaugePlace), ProfileEntry.Places);
        Assert.Contains(nameof(ProfileEntry.GaugeUpright), ProfileEntry.Places);
    }

    [Fact]
    public void Home_scales_give_the_window_the_drawn_gauges_size()
    {
        var g = new GaugeStyle { Thickness = 6 };
        var (w, h) = SplitLayout.HomeScales(SplitLayout.GaugeLengthAtOne, 34);
        (g.Width, g.Height) = (w, h);

        Assert.Equal((SplitLayout.GaugeLength(1), SplitLayout.GaugeHeight(6, 34)), SplitLayout.GaugeSize(g));
        Assert.Equal((1.5, 2.0), SplitLayout.HomeScales(1.5 * SplitLayout.GaugeLengthAtOne, 2 * FreeLayout.Box));
        Assert.Equal(ClassProfiles.MaxScale, SplitLayout.HomeScales(1e6, 34).W);   // clamped to the settings' range
    }

    [Fact]
    public void Apart_is_the_buff_windows_detached_switch()
    {
        var s = new Settings();
        s.Apart = true;
        Assert.True(s.Buffs.Detached);
        s.Buffs.Detached = false;
        Assert.False(s.Apart);
        Assert.DoesNotContain("Apart", JsonSerializer.Serialize(s, SettingsStore.Json));
    }

    static Settings Reload(Settings s) => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s, SettingsStore.Json), SettingsStore.Json)!;
}
