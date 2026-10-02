using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>What the layout editor stands on: a class following 기본 or set on its own (개별), effects per class, the
/// five overlay profiles, the screenshot ↔ screen mapping, applying only what the editor owns, the box drag and the
/// ▶ 실행해 보기 sample run.</summary>
public class LayoutEditorDataTests
{
    const string Warlord = "워로드", Bard = "바드";
    static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);

    static Settings WithCharacter()
    {
        var s = new Settings { ActiveCharacter = "워로드 · 고독한기사" };
        s.Characters["워로드 · 고독한기사"] = new() { Class = Warlord, Subclass = "고독한기사" };
        return s;
    }

    static Settings RoundTrip(Settings s) =>
        JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s, SettingsStore.Json), SettingsStore.Json)!;

    // ── 기본 따르기 / 개별 ─────────────────────────────────────────────────────────────

    /// <summary>A class that follows 기본 draws with 기본's boxes.</summary>
    [Fact]
    public void A_following_class_reads_기본()
    {
        var s = WithCharacter();
        ClassProfiles.Edit(s, ClassProfiles.Template).Mine.Groups[SlotGroup.초각스] = false;
        ClassProfiles.Entry(s, Warlord);

        Assert.False(ClassProfiles.IsOwn(s, Warlord));
        Assert.False(ClassProfiles.Mine(s).Groups[SlotGroup.초각스]);
    }

    /// <summary>개별 설정 copies 기본 the first time; after that the class has its own and 기본 changes don't reach it.</summary>
    [Fact]
    public void 개별_starts_from_a_copy_of_기본()
    {
        var s = WithCharacter();
        ClassProfiles.Edit(s, ClassProfiles.Template).Mine.Groups[SlotGroup.초각스] = false;

        ClassProfiles.SetOwn(s, Warlord, true);
        Assert.False(ClassProfiles.Mine(s).Groups[SlotGroup.초각스]);
        ClassProfiles.Edit(s, ClassProfiles.Template).Mine.Groups[SlotGroup.초각스] = true;
        Assert.False(ClassProfiles.Mine(s).Groups[SlotGroup.초각스]);
    }

    /// <summary>Back to 기본 keeps the class's own settings, so 개별 again brings them back instead of a new copy.</summary>
    [Fact]
    public void Following_기본_again_keeps_the_class_settings_for_later()
    {
        var s = WithCharacter();
        ClassProfiles.SetOwn(s, Warlord, true);
        ClassProfiles.Edit(s, Warlord).Mine.Groups[SlotGroup.아덴스킬] = false;

        ClassProfiles.SetOwn(s, Warlord, false);
        Assert.True(ClassProfiles.Mine(s).Groups[SlotGroup.아덴스킬]);
        ClassProfiles.SetOwn(s, Warlord, true);
        Assert.False(ClassProfiles.Mine(s).Groups[SlotGroup.아덴스킬]);
    }

    /// <summary>Changing a class's settings makes it 개별 (as before), and a change on screen (편집 모드) to a following
    /// class goes to 기본.</summary>
    [Fact]
    public void Edit_makes_a_class_개별_and_EditShown_writes_기본_while_following()
    {
        var s = WithCharacter();
        ClassProfiles.EditShown(s, Warlord).Mine.Layout = FreeLayout.Default(6);
        Assert.NotNull(ClassProfiles.Of(s, ClassProfiles.Template).Mine.Layout);
        Assert.False(ClassProfiles.IsOwn(s, Warlord));

        ClassProfiles.Edit(s, Bard).Mine.Gauge = false;
        Assert.True(ClassProfiles.IsOwn(s, Bard));
        Assert.True(ClassProfiles.Of(s, ClassProfiles.Template).Mine.Gauge);
        ClassProfiles.EditShown(s, Bard).Mine.Gauge = true;
        Assert.True(ClassProfiles.Of(s, Bard).Mine.Gauge);
    }

    /// <summary>A party member's box dragged in the editor goes where that member is drawn from: 기본's party boxes
    /// while their class follows 기본 (every following class with it), the class's own once 개별; never your boxes.</summary>
    [Fact]
    public void A_member_box_drag_writes_the_layout_the_member_is_drawn_with()
    {
        var s = new Settings();
        var cells = FreeLayout.Default(6);
        ClassProfiles.BoxesShown(s, Bard, member: true).Layout = cells;
        Assert.Same(cells, ClassProfiles.Member(s, Warlord, Role.딜러, null).Layout);
        Assert.Null(ClassProfiles.Mine(s).Layout);

        ClassProfiles.SetOwn(s, Bard, true);
        var own = new Dictionary<Part, Cell>(cells) { [Part.일반1] = new(0, 0, 50, 50) };
        ClassProfiles.BoxesShown(s, Bard, member: true).Layout = own;
        Assert.Same(own, ClassProfiles.Member(s, Bard, Role.서포터, null).Layout);
        Assert.Same(cells, ClassProfiles.Member(s, Warlord, Role.딜러, null).Layout);
        Assert.Same(ClassProfiles.EditShown(s, Bard).Mine, ClassProfiles.BoxesShown(s, Bard, member: false));
    }

    /// <summary>Settings from before 개별 existed: a class is 개별 only where it differs from 기본 (learning made an
    /// entry for nearly every class, so having an entry says nothing).</summary>
    [Fact]
    public void Loading_old_settings_makes_개별_only_the_classes_that_differ_from_기본()
    {
        var s = WithCharacter();
        ClassProfiles.Edit(s, ClassProfiles.Template);
        ClassProfiles.Edit(s, Warlord);
        ClassProfiles.Edit(s, Bard).Mine.Gauge = false;
        s.Version = 1;
        var json = JsonSerializer.Serialize(s, SettingsStore.Json).Replace("\"Own\": true", "\"Own\": false");

        var back = LoadText(json);
        Assert.False(ClassProfiles.IsOwn(back, Warlord));
        Assert.True(ClassProfiles.IsOwn(back, Bard));
    }

    // ── effects per class ────────────────────────────────────────────────────────────

    /// <summary>준비되면 반짝임, 계속 빛남, 끝나기 직전 깜빡임, 아이콘 and 키 표시 are set per class now.</summary>
    [Fact]
    public void Effects_are_set_per_class()
    {
        var s = WithCharacter();
        ClassProfiles.Edit(s, Warlord).Mine.Pulse.Add(SlotGroup.각성기);
        Assert.True(ClassProfiles.Mine(s).Effects!.Pulses(SlotGroup.각성기));
        Assert.False(ClassProfiles.Of(s, Bard).Mine.Pulses(SlotGroup.각성기));
    }

    /// <summary>The effects were one set for every class: loading moves them into 기본 and into every class that already
    /// has settings of its own, so nothing looks different after the update.</summary>
    [Fact]
    public void Loading_old_settings_moves_the_effects_into_every_class()
    {
        var json = """
            {
              "Version": 1,
              "Overlay": { "ShowShine": false, "Pulse": [ "각성기" ], "PulseStyle": { "각성기": "Outline" },
                           "EndFlash": [ "일반스킬" ], "SlotEndFlash": { "일반1": false }, "KeyLetters": [ "일반2" ], "Icons": [ "초각스" ] },
              "Classes": { "": {}, "워로드": { "Mine": { "Gauge": false } } }
            }
            """;
        var s = LoadText(json);
        foreach (var cls in new[] { ClassProfiles.Template, Warlord })
        {
            var fx = ClassProfiles.Of(s, cls).Mine;
            Assert.All(Enum.GetValues<SlotGroup>(), g => Assert.False(fx.Shines(g)));
            Assert.True(fx.Pulses(SlotGroup.각성기));
            Assert.Equal(ReadyGlowStyle.Outline, fx.PulseStyleOf(SlotGroup.각성기));
            Assert.Equal((false, true), (fx.EndFlashes(SlotId.일반1, SlotGroup.일반스킬), fx.EndFlashes(SlotId.일반2, SlotGroup.일반스킬)));
            Assert.Contains(SlotId.일반2, fx.KeyLetters);
            Assert.True(fx.ShowsIcon(SlotGroup.초각스));
        }
        Assert.True(ClassProfiles.IsOwn(s, Warlord));
        Assert.Null(s.Overlay.Pulse);
        Assert.Null(s.Overlay.ShowShine);
    }

    /// <summary>The old 1.x "Custom" colours object still loads (the 개별 switch has its own name).</summary>
    [Fact]
    public void A_class_with_the_old_Custom_colours_still_loads()
    {
        var s = LoadText("""{ "Version": 1, "Classes": { "워로드": { "Custom": { "각성기": "#010203" } } } }""");
        Assert.Equal("#010203", ClassProfiles.Of(s, Warlord).Mine.Colors[SlotGroup.각성기]);
    }

    // ── overlay profiles ─────────────────────────────────────────────────────────────

    [Fact]
    public void There_are_five_profiles_after_saving_and_loading()
    {
        var s = new Settings();
        Assert.Equal(OverlayProfile.Count, s.Profiles.Count);
        Assert.Equal(OverlayProfile.Count, RoundTrip(s).Profiles.Count);
        Assert.Equal(OverlayProfile.Count, LoadText("""{ "Profiles": [ {}, {} ] }""").Profiles.Count);
    }

    /// <summary>Switching keeps where everything was in the profile left and puts back the one switched to; other
    /// profiles stay as they were.</summary>
    [Fact]
    public void Switching_profiles_swaps_places_sizes_style_and_backgrounds()
    {
        var s = new Settings();
        (s.Overlay.Left, s.Overlay.Top, s.Overlay.Width, s.Overlay.PlateColor) = (10, 20, 1.5, "#202020");
        (s.Buffs.Detached, s.Buffs.Left) = (true, 300);
        s.Buffs.Edit(BuffKind.공증).Thickness = 2;
        (s.Party.Together, s.Party.Left, s.Party.PlateColor) = (true, 50, "#303030");
        s.Party.ShowOthersBySlot[1].Top = 400;
        s.Gauge.Thickness = 9;

        s.SwitchProfile(1);
        Assert.Equal(1, s.ActiveProfile);
        Assert.True(double.IsNaN(s.Overlay.Left));
        Assert.Equal((1.0, false, 1.0, false, 6.0), (s.Overlay.Width, s.Buffs.Detached, s.Buffs.Of(BuffKind.공증).Thickness, s.Party.Together, s.Gauge.Thickness));
        s.Overlay.Left = 77;

        s.SwitchProfile(0);
        Assert.Equal((10.0, 20.0, 1.5, "#202020"), (s.Overlay.Left, s.Overlay.Top, s.Overlay.Width, s.Overlay.PlateColor));
        Assert.Equal((true, 300.0, 2.0), (s.Buffs.Detached, s.Buffs.Left, s.Buffs.Of(BuffKind.공증).Thickness));
        Assert.Equal((true, 50.0, "#303030", 400.0, 9.0), (s.Party.Together, s.Party.Left, s.Party.PlateColor, s.Party.ShowOthersBySlot[1].Top, s.Gauge.Thickness));
        Assert.Equal(77, s.Profiles[1].Overlay.Left);
        Assert.True(double.IsNaN(s.Profiles[2].Overlay.Left));
    }

    [Fact]
    public void Switching_to_the_active_or_a_missing_profile_changes_nothing()
    {
        var s = new Settings();
        s.Overlay.Left = 10;
        s.SwitchProfile(0);
        s.SwitchProfile(7);
        Assert.Equal((0, 10.0), (s.ActiveProfile, s.Overlay.Left));
    }

    // ── screenshot ↔ screen ──────────────────────────────────────────────────────────

    /// <summary>A screenshot is tied to screen places only when it has the screen's shape; it is stretched over the
    /// screen area it was taken of.</summary>
    [Fact]
    public void An_image_links_to_the_screen_only_with_the_same_shape()
    {
        var area = new PixelRect(0, 0, 2560, 1440);
        Assert.True(ScreenMap.Linked(1920, 1080, area));
        Assert.False(ScreenMap.Linked(1920, 1200, area));
        Assert.False(ScreenMap.Linked(0, 0, area));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void A_window_place_maps_into_the_image_and_back(double dpiScale)
    {
        var area = new PixelRect(-1920, 0, 3840, 2160);   // a 4K monitor left of the main one
        var (ix, iy) = ScreenMap.DipToImage(-1000, 800, dpiScale, 1920, 1080, area);
        Assert.Equal(((-1000 * dpiScale + 1920) / 2, 800 * dpiScale / 2), (ix, iy));
        var (dx, dy) = ScreenMap.ImageToDip(ix, iy, dpiScale, 1920, 1080, area);
        Assert.Equal(-1000, dx, 6);
        Assert.Equal(800, dy, 6);
    }

    // ── applying the editor's copy ───────────────────────────────────────────────────

    /// <summary>적용 copies what the editor changes and nothing the engine or the tray wrote meanwhile.</summary>
    [Fact]
    public void Applying_the_editor_copy_keeps_what_the_app_wrote_meanwhile()
    {
        var live = WithCharacter();
        var edited = RoundTrip(live);
        (edited.Overlay.Left, edited.Overlay.PlateColor, edited.Gauge.Thickness, edited.Buffs.Detached) = (5, "#111111", 8, true);
        ClassProfiles.Edit(edited, Warlord).Mine.Pulse.Add(SlotGroup.각성기);
        edited.PickCharacter("전사", Bard, "절실한구원");   // the class previewed in the editor
        edited.Profiles[3].Overlay.Top = 9;

        live.Fits["1920×1080"] = new Fit(1, 1, 0);
        live.Overlay.Visible = false;

        live.ApplyLayoutFrom(edited);
        Assert.Equal((5.0, "#111111", 8.0, true), (live.Overlay.Left, live.Overlay.PlateColor, live.Gauge.Thickness, live.Buffs.Detached));
        Assert.True(ClassProfiles.Mine(live).Effects!.Pulses(SlotGroup.각성기));
        Assert.Equal(9, live.Profiles[3].Overlay.Top);
        Assert.Equal("워로드 · 고독한기사", live.ActiveCharacter);
        Assert.DoesNotContain("바드 · 절실한구원", live.Characters.Keys);
        Assert.Contains("1920×1080", live.Fits.Keys);
        Assert.False(live.Overlay.Visible);
    }

    [Fact]
    public void Editing_the_editor_copy_after_applying_leaves_the_live_settings_alone()
    {
        var live = WithCharacter();
        var edited = RoundTrip(live);
        live.ApplyLayoutFrom(edited);

        edited.Buffs.Detached = true;
        edited.Gauge.Thickness = 20;
        ClassProfiles.Edit(edited, Warlord).Mine.Pulse.Add(SlotGroup.초각스);
        edited.Profiles[2].Overlay.Top = 7;

        Assert.False(live.Buffs.Detached);
        Assert.NotEqual(20, live.Gauge.Thickness);
        Assert.False(ClassProfiles.Of(live, Warlord).Mine.Pulses(SlotGroup.초각스));
        Assert.NotEqual(7, live.Profiles[2].Overlay.Top);
    }

    // ── box drag ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Dragging_a_box_moves_it_by_the_distance_over_the_box_scale()
    {
        var start = FreeLayout.Default(6);
        var moved = FreeLayout.Drag(start, start.Keys, Part.일반1, 20, 10, (2, 2), resize: false, snap: false, link: true);
        Assert.Equal(start[Part.일반1] with { X = start[Part.일반1].X + 10, Y = start[Part.일반1].Y + 5 }, moved[Part.일반1]);
        Assert.Equal(start[Part.일반2], moved[Part.일반2]);
    }

    [Fact]
    public void Dragging_near_a_box_snaps_to_it_unless_snap_is_off()
    {
        var start = FreeLayout.Default(6);
        var target = start[Part.일반2].X;
        var dx = target - start[Part.일반1].X + 3;   // 3 DIP past the neighbour's left edge
        Assert.Equal(target, FreeLayout.Drag(start, start.Keys, Part.일반1, dx, 0, (1, 1), false, snap: true, link: true)[Part.일반1].X);
        Assert.Equal(target + 3, FreeLayout.Drag(start, start.Keys, Part.일반1, dx, 0, (1, 1), false, snap: false, link: true)[Part.일반1].X);
    }

    /// <summary>Resizing a key box resizes the set with 크기 묶기; the gauge only gets wider.</summary>
    [Fact]
    public void Resizing_keeps_linked_sizes_and_the_gauge_thickness()
    {
        var start = FreeLayout.Default(12);
        var sized = FreeLayout.Drag(start, start.Keys, Part.일반1, 10, 10, (1, 1), resize: true, snap: false, link: true);
        Assert.Equal((44.0, 44.0), (sized[Part.일반5].W, sized[Part.일반5].H));
        var gauge = FreeLayout.Drag(start, start.Keys, Part.게이지, 10, 10, (1, 1), resize: true, snap: false, link: true);
        Assert.Equal((start[Part.게이지].W + 10, start[Part.게이지].H), (gauge[Part.게이지].W, gauge[Part.게이지].H));
    }

    // ── ▶ 실행해 보기 ────────────────────────────────────────────────────────────────

    static PreviewSim Sim() => new(Snapshot.Empty(T0) with { Gauge = 0 }, T0);

    [Fact]
    public void The_sample_run_starts_with_every_skill_ready_and_no_buff()
    {
        var s = Sim().At(T0);
        Assert.All(s.Slots, st => Assert.Equal(SlotPhase.Ready, st.Phase));
        Assert.All(s.Buffs, b => Assert.False(b.Active));
        Assert.Equal(Enum.GetValues<BuffKind>().Length, s.Buffs.Length);
    }

    /// <summary>A click on a ready box uses the skill: its cooldown runs the sample length and it is ready again after.</summary>
    [Fact]
    public void Clicking_a_ready_box_starts_its_cooldown()
    {
        var sim = Sim();
        sim.Click(Part.일반3, T0);
        var st = sim.At(T0 + TimeSpan.FromSeconds(1))[SlotId.일반3];
        Assert.Equal((SlotPhase.Cooldown, T0 + PreviewSim.SkillLength, PreviewSim.SkillLength), (st.Phase, st.EndsAt, st.Total));

        var after = sim.At(T0 + PreviewSim.SkillLength + TimeSpan.FromMilliseconds(1))[SlotId.일반3];
        Assert.Equal((SlotPhase.Ready, T0 + PreviewSim.SkillLength), (after.Phase, after.ChangedAt));
    }

    /// <summary>A click on a cooling box jumps to one second before the end, keeping the full length, so the end flash
    /// and the ready shine play next.</summary>
    [Fact]
    public void Clicking_a_cooling_box_leaves_one_second()
    {
        var sim = Sim();
        sim.Click(Part.각성기, T0);
        var at = T0 + TimeSpan.FromSeconds(1);
        sim.Click(Part.각성기, at);
        var st = sim.At(at)[SlotId.각성기];
        Assert.Equal((SlotPhase.Cooldown, at + PreviewSim.NearEnd, PreviewSim.SkillLength), (st.Phase, st.EndsAt, st.Total));
    }

    [Fact]
    public void Clicking_a_buff_bar_turns_it_on_then_leaves_one_second()
    {
        var sim = Sim();
        sim.Click(FreeLayout.FirstBuff + (int)BuffKind.아덴피증, T0);
        var b = sim.At(T0).Buffs[(int)BuffKind.아덴피증];
        Assert.Equal((true, T0 + PreviewSim.BuffLength, PreviewSim.BuffLength), (b.Active, b.EndsAt, b.Total));

        var at = T0 + TimeSpan.FromSeconds(2);
        sim.Click(FreeLayout.FirstBuff + (int)BuffKind.아덴피증, at);
        Assert.Equal(at + PreviewSim.NearEnd, sim.At(at).Buffs[(int)BuffKind.아덴피증].EndsAt);
        Assert.False(sim.At(at + TimeSpan.FromSeconds(2)).Buffs[(int)BuffKind.아덴피증].Active);
    }

    /// <summary>A click on the gauge fills one more section; a click on a full gauge empties it.</summary>
    [Fact]
    public void Clicking_the_gauge_fills_a_section_at_a_time_then_empties()
    {
        var sim = Sim();
        var levels = Enumerable.Range(0, 4).Select(_ => { sim.Click(Part.게이지, T0, sections: 3); return sim.At(T0).Gauge; }).ToList();
        Assert.Equal([0.333333, 0.666667, 1.0, 0.0], levels.Select(g => Math.Round(g!.Value, 6)));
    }

    [Fact]
    public void Clicking_변화_turns_the_각성기_change_on_and_off()
    {
        var sim = Sim();
        sim.Click(Part.변화, T0);
        Assert.True(sim.At(T0).AwakeningChanged);
        sim.Click(Part.변화, T0);
        Assert.False(sim.At(T0).AwakeningChanged);
    }

    [Fact]
    public void Reset_makes_everything_ready_again()
    {
        var sim = Sim();
        sim.Click(Part.일반1, T0);
        sim.Click(FreeLayout.FirstBuff, T0);
        sim.Click(Part.게이지, T0);
        sim.Reset(T0);
        var s = sim.At(T0);
        Assert.Equal((SlotPhase.Ready, false, 0.0), (s[SlotId.일반1].Phase, s.Buffs[0].Active, s.Gauge));
    }

    static Settings LoadText(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "GaugeOverlayTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, json);
            return new SettingsStore(path).Load(out _);
        }
        finally { Directory.Delete(dir, true); }
    }
}
