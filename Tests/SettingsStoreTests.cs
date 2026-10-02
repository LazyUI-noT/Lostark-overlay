using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GaugeOverlayTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");
    static MineProfile Fx(Settings s) => ClassProfiles.Of(s, null).Mine;

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static Settings Edited()
    {
        var s = new Settings { FirstRunDone = true, ActiveCharacter = "워로드 · 고독한기사" };
        s.Keys[SlotId.초각스] = "Shift";
        s.Characters["워로드 · 고독한기사"] = new() { Class = "워로드" };
        var c = ClassProfiles.Edit(s, "워로드");
        c.Mine.Groups[SlotGroup.아덴스킬] = false;
        c.Mine.HiddenSlots.Add(SlotId.일반8);
        s.Overlay.Width = 1.4;
        c.Members.Height = 0.7;
        c.Mine.Layout = FreeLayout.Default(6);
        c.Mine.Layout[Part.일반1] = new Cell(1, 2, 3, 4);
        s.Fits["1920×810"] = new Fit(100, 1.5, -2);
        return s;
    }

    [Fact]
    public void A_settings_file_from_before_updates_checks_for_updates_and_has_notified_none()
    {
        new SettingsStore(FilePath).Save(new Settings { FirstRunDone = true });
        File.WriteAllText(FilePath, File.ReadAllText(FilePath).Replace("\"updates\"", "\"oldKey\"", StringComparison.OrdinalIgnoreCase));
        var back = new SettingsStore(FilePath).Load(out _);
        Assert.True(back.FirstRunDone);
        Assert.True(back.Updates.Check);
        Assert.Equal("", back.Updates.Notified);
    }

    [Fact]
    public void A_broken_screenshot_area_loads_as_none()
    {
        var store = new SettingsStore(FilePath);
        var s = new Settings();
        s.Profiles[0].Shot = new PixelRect(0, 0, -1, 10);
        s.Profiles[1].Shot = new PixelRect(0, 0, 1920, 1080);
        store.Save(s);
        var back = store.Load(out _);
        Assert.Null(back.Profiles[0].Shot);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), back.Profiles[1].Shot);
    }

    /// <summary>크기 직접 입력: off by default, kept across restarts, and a hand-edited size outside the game's range is
    /// pulled into it.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData(100.0, 100.0)]
    [InlineData(10.0, 70.0)]
    [InlineData(900.0, 130.0)]
    public void A_given_HUD_size_is_kept_within_range(double? given, double? loaded)
    {
        Assert.Null(new Settings().ManualHud);
        var store = new SettingsStore(FilePath);
        store.Save(new Settings { ManualHud = given });
        Assert.Equal(loaded, store.Load(out _).ManualHud);
    }

    /// <summary>직접 입력's 해상도 and the last detected one survive a restart; a hand-edited one no screen has is dropped (the game
    /// window's size is used).</summary>
    [Theory]
    [InlineData(2560, 1440, true, true)]
    [InlineData(0, 1440, false, false)]
    [InlineData(2560, 99999, false, false)]
    public void A_typed_해상도_is_kept_only_when_a_screen_can_have_it(int w, int h, bool wide, bool kept)
    {
        var store = new SettingsStore(FilePath);
        store.Save(new Settings { ManualHud = 100, ManualScreen = new ManualScreen(w, h, wide), DetectedScreen = new ManualScreen(w, h, wide) });
        var loaded = store.Load(out _);
        Assert.Equal(kept ? new ManualScreen(w, h, wide) : null, loaded.ManualScreen);
        Assert.Equal(kept ? new ManualScreen(w, h, wide) : null, loaded.DetectedScreen);
    }

    /// <summary>Turning 직접 입력 on starts from what the last 스킬바 맞추기 found: its 해상도, 21:9 and HUD size.</summary>
    [Fact]
    public void Direct_input_starts_from_the_last_detected_values()
    {
        var s = new Settings { DetectedScreen = new ManualScreen(2560, 1440, true) };
        s.Fits["2560×1080"] = new Fit(95);
        s.StartManual();
        Assert.Equal(95, s.ManualHud);
        Assert.Equal(new ManualScreen(2560, 1440, true), s.ManualScreen);

        var fresh = new Settings();
        fresh.StartManual();
        Assert.Equal(100, fresh.ManualHud);
        Assert.Equal(new ManualScreen(1920, 1080, false), fresh.ManualScreen);   // nothing found yet: the most common 해상도
    }

    [Fact]
    public void Debuff_box_sizes_are_kept_across_restarts()
    {
        var store = new SettingsStore(FilePath);
        var s = new Settings();
        (s.Debuffs.Box(1).Width, s.Debuffs.Box(1).Height) = (2, 1.5);
        store.Save(s);
        var back = store.Load(out _).Debuffs;
        Assert.Equal(2, back.Boxes.Count);
        Assert.Equal((1.0, 1.0, 2.0, 1.5), (back.Box(0).Width, back.Box(0).Height, back.Box(1).Width, back.Box(1).Height));
    }

    [Fact]
    public void Korean_enum_keys_round_trip()
    {
        var store = new SettingsStore(FilePath);
        store.Save(Edited());

        string json = File.ReadAllText(FilePath);
        Assert.Contains("\"초각스\": \"Shift\"", json);
        Assert.Contains("\"아덴스킬\": false", json);
        Assert.Contains("\"일반8\"", json);
        Assert.Contains("NaN", json);

        var s = store.Load(out bool reset);
        Assert.False(reset);
        Assert.Equal("Shift", s.Keys[SlotId.초각스]);
        var c = s.Classes["워로드"];
        Assert.False(c.Mine.Groups[SlotGroup.아덴스킬]);
        Assert.Contains(SlotId.일반8, c.Mine.HiddenSlots);
        Assert.Equal("워로드", s.Characters["워로드 · 고독한기사"].Class);
        Assert.Equal(new Fit(100, 1.5, -2), s.Fits["1920×810"]);
        Assert.True(double.IsNaN(s.Overlay.Left));
        Assert.Equal((1.4, 1.0, 1.0, 0.7), (s.Overlay.Width, s.Overlay.Height, c.Members.Width, c.Members.Height));
        Assert.DoesNotContain("\"PartyLeft\"", json);
        Assert.Equal(3, s.Party.ShowOthersBySlot.Count);   // loading fills the list, it doesn't add to the default one
        Assert.Equal(new Cell(1, 2, 3, 4), c.Mine.Layout![Part.일반1]);
        Assert.Equal(Enum.GetValues<Part>().Length, c.Mine.Layout.Count);
        Assert.Null(c.Members.Layout);   // the fixed grid
        Assert.DoesNotContain("\"Scale\"", json);
        Assert.DoesNotContain("\"GroupVisible\"", json);
    }

    /// <summary>오버레이 표시: shown by default (settings from before it existed too), and hidden stays hidden.</summary>
    [Fact]
    public void Overlay_shown_is_kept_across_restarts()
    {
        Assert.True(new Settings().Overlay.Visible);
        var store = new SettingsStore(FilePath);
        var s = Edited();
        s.Overlay.Visible = false;
        store.Save(s);
        Assert.False(store.Load(out _).Overlay.Visible);
    }

    /// <summary>준비되면 반짝임 per group: all on by default; 1.2's single switch turned off becomes every group off.</summary>
    [Fact]
    public void Shine_is_set_per_group_and_the_old_switch_carries_over()
    {
        Assert.True(Enum.GetValues<SlotGroup>().All(new MineProfile().Shines));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "Overlay": { "ShowShine": false } }""");
        var store = new SettingsStore(FilePath);
        var s = store.Load(out _);
        Assert.DoesNotContain(Enum.GetValues<SlotGroup>(), g => Fx(s).Shines(g));

        ClassProfiles.Edit(s, null).Mine.NoShine = [SlotGroup.일반스킬];
        store.Save(s);
        s = store.Load(out _);
        Assert.Equal([false, true], new[] { SlotGroup.일반스킬, SlotGroup.초각스 }.Select(Fx(s).Shines));
        Assert.DoesNotContain("ShowShine", File.ReadAllText(FilePath));
    }

    /// <summary>버프 막대 줄어듦: on by default (the bar drains), and off is kept.</summary>
    [Fact]
    public void Buff_drain_is_on_by_default_and_off_is_kept()
    {
        Assert.True(new Settings().Buffs.Drain);
        var store = new SettingsStore(FilePath);
        var s = new Settings();
        s.Buffs.Drain = false;
        store.Save(s);
        Assert.False(store.Load(out _).Buffs.Drain);
    }

    /// <summary>버프 막대 따로 두기: off by default (the bars stay on the plate); on, they have a window of their own whose
    /// place and size are kept, the place unset (NaN) until it is first placed.</summary>
    [Fact]
    public void Detached_buff_bars_keep_their_own_window_place_and_size()
    {
        var b = new Settings().Buffs;
        Assert.False(b.Detached);
        Assert.True(double.IsNaN(b.Left) && double.IsNaN(b.Top));
        Assert.Equal((1.0, 1.0), (b.Width, b.Height));

        var store = new SettingsStore(FilePath);
        var s = new Settings();
        IWindowPlace place = s.Buffs;
        (s.Buffs.Detached, place.Left, place.Top, place.Width, place.Height) = (true, 100, 200, 1.5, 0.8);
        store.Save(s);
        var back = store.Load(out _).Buffs;
        Assert.Equal((true, 100.0, 200.0, 1.5, 0.8), (back.Detached, back.Left, back.Top, back.Width, back.Height));
    }

    /// <summary>Each buff bar's 길이 and 두께: 100% of the fixed grid's size by default, and kept per indicator.</summary>
    [Fact]
    public void Each_buff_bar_keeps_its_own_length_and_thickness()
    {
        Assert.All(Enum.GetValues<BuffKind>(), k => Assert.Equal((1.0, 1.0), (new Settings().Buffs.Of(k).Length, new Settings().Buffs.Of(k).Thickness)));
        var store = new SettingsStore(FilePath);
        var s = new Settings();
        (s.Buffs.Edit(BuffKind.공증).Length, s.Buffs.Edit(BuffKind.공증).Thickness) = (0.5, 2);
        store.Save(s);
        var back = store.Load(out _).Buffs;
        Assert.Equal((0.5, 2.0), (back.Of(BuffKind.공증).Length, back.Of(BuffKind.공증).Thickness));
        Assert.Equal((1.0, 1.0), (back.Of(BuffKind.뎀증).Length, back.Of(BuffKind.뎀증).Thickness));
    }

    /// <summary>마지막 1초 깜빡임: on for 공증 and 아덴 피증, off for the others, and kept per indicator.</summary>
    [Fact]
    public void The_last_second_flash_is_on_for_공증_and_아덴피증_and_kept()
    {
        var b = new Settings().Buffs;
        Assert.Equal([true, true, false, false], Enum.GetValues<BuffKind>().Select(b.EndFlash).ToArray());
        var store = new SettingsStore(FilePath);
        var s = new Settings();
        s.Buffs.Edit(BuffKind.공증).EndFlash = false;
        s.Buffs.Edit(BuffKind.뎀증).EndFlash = true;
        s.Buffs.Edit(BuffKind.아덴피증).Length = 0.5;   // edited, flash left alone: still the default
        store.Save(s);
        var back = store.Load(out _).Buffs;
        Assert.Equal([false, true, false, true], Enum.GetValues<BuffKind>().Select(back.EndFlash).ToArray());
    }

    /// <summary>아이콘으로 표시: every group is drawn in colour until picked, and the pick is kept per group.</summary>
    [Fact]
    public void Icons_are_off_until_picked_per_group()
    {
        Assert.DoesNotContain(Enum.GetValues<SlotGroup>(), g => new MineProfile().ShowsIcon(g));
        var store = new SettingsStore(FilePath);
        var s = new Settings();
        ClassProfiles.Edit(s, null).Mine.Icons = [SlotGroup.일반스킬, SlotGroup.각성기];
        store.Save(s);
        var back = Fx(store.Load(out _));
        Assert.Equal([true, false, true, false], Enum.GetValues<SlotGroup>().Select(back.ShowsIcon).ToArray());
    }

    /// <summary>준비된 동안 계속 빛남: off for every group until picked, and kept per group.</summary>
    [Fact]
    public void Glow_while_ready_is_off_until_picked_per_group()
    {
        Assert.DoesNotContain(Enum.GetValues<SlotGroup>(), g => new MineProfile().Pulses(g));
        var store = new SettingsStore(FilePath);
        var s = new Settings();
        ClassProfiles.Edit(s, null).Mine.Pulse = [SlotGroup.아덴스킬, SlotGroup.초각스];
        store.Save(s);
        s = store.Load(out _);
        Assert.Equal([true, true, false], new[] { SlotGroup.아덴스킬, SlotGroup.초각스, SlotGroup.일반스킬 }.Select(Fx(s).Pulses));
    }

    [Fact]
    public void Save_replaces_atomically_and_keeps_previous_as_bak()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new Settings { ActiveCharacter = "first" });
        Assert.False(File.Exists(FilePath + ".bak"));   // first save: plain move, nothing to back up

        store.Save(new Settings { ActiveCharacter = "second" });
        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.Equal("second", store.Load(out _).ActiveCharacter);
        Assert.Equal("first", new SettingsStore(FilePath + ".bak").Load(out _).ActiveCharacter);
    }

    [Fact]
    public void Crash_mid_write_loads_bak()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new Settings { ActiveCharacter = "good" });
        store.Save(new Settings { ActiveCharacter = "good" });
        File.WriteAllText(FilePath + ".tmp", "{ \"ActiveCharacter\": \"hal");   // interrupted write
        File.WriteAllText(FilePath, "{ \"Version\": 1, \"Keys\": { \"일반1\": ");   // torn main file

        var s = store.Load(out bool reset);
        Assert.False(reset);
        Assert.Equal("good", s.ActiveCharacter);

        store.Save(s);   // the next save overwrites the leftover .tmp
        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.Equal("good", store.Load(out _).ActiveCharacter);
    }

    [Fact]
    public void Both_files_broken_gives_defaults_and_reset_flag()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        File.WriteAllText(FilePath + ".bak", "{ \"Keys\": { \"없는슬롯\": \"Q\" } }");

        var s = new SettingsStore(FilePath).Load(out bool reset);
        Assert.True(reset);
        Assert.False(s.FirstRunDone);
        Assert.Equal("Q", s.Keys[SlotId.일반1]);

        // The unreadable files are kept aside, so the saves that follow lose nothing.
        Assert.Equal("not json", File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "settings.json.broken-*"))));
        Assert.Contains("없는슬롯", File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "settings.json.bak.broken-*"))));
    }

    [Fact]
    public void No_file_is_first_run_not_reset()
    {
        var s = new SettingsStore(FilePath).Load(out bool reset);
        Assert.False(reset);
        Assert.False(s.FirstRunDone);
    }

    /// <summary>Before one window per party member, all of them shared one party window: its size goes to every member,
    /// its spot to the first, and the others line up below it.</summary>
    [Fact]
    public void The_one_party_window_becomes_one_window_per_member()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            { "Overlay": { "PartyLeft": 100, "PartyTop": 200, "PartyWidth": 0.8, "PartyHeight": 0.7 } }
            """);
        var store = new SettingsStore(FilePath);

        var s = store.Load(out _);

        var slots = s.Party.ShowOthersBySlot;
        var members = ClassProfiles.Of(s, ClassProfiles.Template).Members;
        Assert.Equal((0.8, 0.7), (members.Width, members.Height));
        Assert.Equal((100.0, 200.0), (slots[0].Left, slots[0].Top));
        Assert.All(slots, d => Assert.Equal(100.0, d.Left));
        Assert.True(slots[0].Top < slots[1].Top && slots[1].Top < slots[2].Top);
        store.Save(s);
        Assert.DoesNotMatch("\"Party(Left|Top|Width|Height)\"", File.ReadAllText(FilePath));
    }

    /// <summary>The 각성기 변화 box turned red; a file still holding the old orange default gets it, a chosen colour stays.</summary>
    [Theory]
    [InlineData("#FF7F27", "#FF4D4D")]
    [InlineData("#123456", "#123456")]
    public void The_old_orange_변화_default_becomes_red(string saved, string loaded)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, $$"""{ "Overlay": { "AwakeningChangedColor": "{{saved}}" } }""");
        Assert.Equal(loaded, new SettingsStore(FilePath).Load(out _).Overlay.AwakeningChangedColor);
    }

    /// <summary>Loading used to add the saved slots after the three defaults, so files grew by three per save with the
    /// latest edits first.</summary>
    [Fact]
    public void A_grown_slot_list_keeps_its_first_three()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            { "Party": { "ShowOthersBySlot": [ { "Left": 1 }, { "Left": 2 }, { "Left": 3 }, {}, {}, { "Left": 9 } ] } }
            """);

        var slots = new SettingsStore(FilePath).Load(out _).Party.ShowOthersBySlot;

        Assert.Equal([1.0, 2.0, 3.0], slots.Select(d => d.Left));
    }

    /// <summary>Before per-class settings: one display, layout and colour mode for you and one per party slot, colours per
    /// class, per character and for all classes, and a support slot 0–2.</summary>
    [Fact]
    public void Settings_from_before_per_class_profiles_move_into_them()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            {
              "ActiveCharacter": "a",
              "Characters": { "a": { "Class": "워로드", "Colors": { "초각스": "#030303" } } },
              "Classes": { "바드": { "Custom": { "일반스킬": "#010101" }, "Learned": { "각성기": "#020202" } } },
              "Overlay": { "HiddenSlots": [ "일반8" ], "GroupVisible": { "아덴스킬": false } },
              "Party": { "ColorMode": "Remote", "SupportSlot": 1,
                         "ShowOthersBySlot": [ { "Groups": { "초각스": false }, "Gauge": false, "Width": 0.6, "Left": 5 } ] }
            }
            """);
        var store = new SettingsStore(FilePath);

        var s = store.Load(out _);

        var bard = s.Classes["바드"];
        Assert.Equal((ColorSource.Personal, "#010101"), (bard.Mine.Color, bard.Mine.Colors[SlotGroup.일반스킬]));
        Assert.Equal("#010101", bard.Members.Colors[SlotGroup.일반스킬]);
        Assert.Equal(ColorSource.Received, bard.Members.Color);   // 파티원 설정 따르기
        Assert.Equal("#030303", s.Classes["워로드"].Mine.Colors[SlotGroup.초각스]);   // the character's colours
        Assert.Contains(SlotId.일반8, bard.Mine.HiddenSlots);
        Assert.False(bard.Mine.Groups[SlotGroup.아덴스킬]);
        Assert.False(bard.Members.Groups[SlotGroup.초각스]);
        Assert.False(bard.Members.Gauge);
        Assert.Equal(0.6, bard.Members.Width);
        Assert.Equal(5.0, s.Party.ShowOthersBySlot[0].Left);
        Assert.Equal((true, 3), (s.Party.SupportFixed, s.Party.SupportNumber));   // slot 1 was shown as the second member: 3

        store.Save(s);
        string json = File.ReadAllText(FilePath);
        Assert.DoesNotContain("\"Custom\"", json);
        Assert.DoesNotContain("\"ColorMode\"", json);
        Assert.DoesNotContain("\"SupportSlot\"", json);
        Assert.Null(store.Load(out _).Overlay.HiddenSlots);
    }

    [Fact]
    public void Missing_properties_keep_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            {
              "Version": 0,
              "FirstRunDone": true,
              "Keys": { "일반1": "Shift" },
              "Overlay": { "Scale": 1.5, "GroupColor": { "각성기": "#000000" } },
              "Characters": { "워로드 · 고독한기사": { "Class": "워로드", "Build": "333", "TriggerSlots": { "trigger1": "일반3" } } }
            }
            """);

        var s = new SettingsStore(FilePath).Load(out bool reset);
        Assert.False(reset);
        Assert.Equal("워로드", s.Characters["워로드 · 고독한기사"].Class);   // 1.1's build fields are ignored
        Assert.Equal(Settings.CurrentVersion, s.Version);
        Assert.True(s.FirstRunDone);
        Assert.Equal("Shift", s.Keys[SlotId.일반1]);
        Assert.Equal("W", s.Keys[SlotId.일반2]);                       // missing dictionary entry
        Assert.Equal(SlotInfo.All.Length, s.Keys.Count);
        // 1.0 had one size for both windows; it becomes the width and height of each (and of each party member's).
        Assert.Equal((1.5, 1.5), (s.Overlay.Width, s.Overlay.Height));
        var template = ClassProfiles.Of(s, ClassProfiles.Template);
        Assert.Equal((1.5, 1.5), (template.Members.Width, template.Members.Height));
        Assert.Null(s.Overlay.Scale);
        Assert.Equal(0.9, s.Overlay.Opacity);                          // missing property
        Assert.Equal(("#FFD24A", 2.0), (s.Overlay.GoldColor, s.Overlay.GoldThickness));
        // A colour set for all classes becomes every class's own (개인 설정); one left at the built-in colour doesn't.
        Assert.Equal((ColorSource.Personal, "#000000"), (template.Mine.Color, template.Mine.Colors[SlotGroup.각성기]));
        Assert.False(template.Mine.Colors.ContainsKey(SlotGroup.일반스킬));
        Assert.Equal("#000000", s.Classes["워로드"].Mine.Colors[SlotGroup.각성기]);
        Assert.Equal(Enum.GetValues<SlotGroup>().Length, template.Mine.Groups.Count);
        Assert.Equal("Ctrl+Shift+E", s.Hotkeys.EditMode);             // missing object
        Assert.Equal("Ctrl+Shift+D", s.Hotkeys.DetectCharacter);
        Assert.Equal("Ctrl+Shift+T", s.Hotkeys.TestMode);
        Assert.Equal(47800, s.Party.HostPort);
    }

    /// <summary>트레이의 클래스 → 직업각인: an existing "클래스 · 직업각인" character becomes active; another is added.</summary>
    [Fact]
    public void Picking_a_class_and_직업각인_switches_to_its_character_or_adds_it()
    {
        var s = new Settings();
        s.Characters["바드 · 절실한구원"] = new() { ClassFamily = "마법사", Class = "바드", Subclass = "절실한구원", EmblemBgra = "x" };
        Assert.Equal("바드 · 절실한구원", s.PickCharacter("마법사", "바드", "절실한구원"));
        Assert.Equal(("바드 · 절실한구원", "x"), (s.ActiveCharacter, s.Characters["바드 · 절실한구원"].EmblemBgra));
        Assert.Equal("발키리 · 해방자", s.PickCharacter("워리어", "발키리", "해방자"));
        var added = s.Characters["발키리 · 해방자"];
        Assert.Equal(("발키리 · 해방자", "워리어", "발키리", "해방자"), (s.ActiveCharacter, added.ClassFamily, added.Class, added.Subclass));
        Assert.Equal(2, s.Characters.Count);
    }

    /// <summary>초각스 icon shows another 직업각인: the same class's character for it becomes active (added if new) and
    /// gets the emblem, so the key, the tray's check mark and emblem detection all agree.</summary>
    [Fact]
    public void A_detected_직업각인_switches_to_that_character_and_keeps_the_emblem()
    {
        var s = new Settings();
        s.PickCharacter("워리어", "발키리", "해방자");
        s.Characters["발키리 · 해방자"].EmblemBgra = "x";
        s.DetectedSubclass("빛의 기사");
        Assert.Equal("발키리 · 빛의 기사", s.ActiveCharacter);
        Assert.Equal(("워리어", "빛의 기사", "x"), (s.Characters["발키리 · 빛의 기사"].ClassFamily, s.Characters["발키리 · 빛의 기사"].Subclass, s.Characters["발키리 · 빛의 기사"].EmblemBgra));
        Assert.Equal("해방자", s.Characters["발키리 · 해방자"].Subclass);   // the other one is kept as it was
        s.DetectedSubclass("해방자");
        Assert.Equal("발키리 · 해방자", s.ActiveCharacter);
    }

    [Fact]
    public void Debounce_saves_half_a_second_after_the_last_change()
    {
        var d = new SaveDebounce();
        TimeSpan T(double s) => TimeSpan.FromSeconds(s);
        Assert.False(d.Due(T(0)));

        d.Changed(T(0));
        Assert.False(d.Due(T(0.4)));
        d.Changed(T(0.3));                // slider still moving
        Assert.False(d.Due(T(0.7)));
        Assert.True(d.Due(T(0.8)));
        Assert.False(d.Due(T(0.9)));      // once

        d.Changed(T(1));
        d.Flush();
        Assert.False(d.Due(T(2)));
    }

    [Fact]
    public async Task Transient_lock_is_retried_not_treated_as_corruption()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new Settings { ActiveCharacter = "kept" });

        // Antivirus or backup holds the file for a moment, during load and during save.
        var held = HoldFor(FilePath, 150);
        Assert.Equal("kept", store.Load(out bool reset).ActiveCharacter);
        Assert.False(reset);
        await held;

        held = HoldFor(FilePath, 150);
        store.Save(new Settings { ActiveCharacter = "saved" });
        await held;
        Assert.Equal("saved", store.Load(out _).ActiveCharacter);
    }

    [Fact]
    public void Persistent_lock_throws_instead_of_resetting()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new Settings { ActiveCharacter = "kept" });
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => store.Load(out _));
        Assert.Equal("kept", store.Load(out _).ActiveCharacter);
    }

    /// <summary>Opens <paramref name="path"/> exclusively and releases it after <paramref name="ms"/> on another thread.</summary>
    private static Task HoldFor(string path, int ms)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        return Task.Delay(ms).ContinueWith(_ => fs.Dispose());
    }

    [Fact]
    public void An_old_file_gets_the_buff_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"Version\": 1, \"FirstRunDone\": true }");

        var s = new SettingsStore(FilePath).Load(out _);
        Assert.Equal(100, s.Buffs.Size);
        Assert.Equal(["#E8262B", "#FFE600", "#1BA8E0", "#A04FB0"], Enum.GetValues<BuffKind>().Select(k => s.Buffs.Of(k).Color));
        Assert.All(Enum.GetValues<BuffKind>(), k => Assert.True(s.Buffs.Of(k).Visible && s.Buffs.Of(k).ShowLabel));
    }

    [Fact]
    public void Gauge_bar_settings_round_trip_and_default_to_none()
    {
        Assert.Empty(new Settings().GaugeBars);
        var s = Edited();
        var bar = GaugeOverlay.Core.Data.GaugeBar.Edit(s, "바드", "절실한구원");
        (bar.Splits, bar.GlowStyle, bar.UnifiedColor, bar.SegmentColors) = ([0.25, 0.75], GlowStyle.Outline, false, ["#FF0000"]);
        var store = new SettingsStore(FilePath);
        store.Save(s);

        var back = store.Load(out _).GaugeBars["바드/절실한구원"];
        Assert.Equal([0.25, 0.75], back.Splits);
        Assert.Equal((GlowStyle.Outline, false), (back.GlowStyle, back.UnifiedColor));
        Assert.Equal(["#FF0000"], back.SegmentColors);
        Assert.DoesNotContain("\"Splits\": null", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Buff_settings_round_trip()
    {
        var s = Edited();
        s.Buffs.Size = 80;
        s.Buffs.Edit(BuffKind.뎀증).Visible = false;
        var store = new SettingsStore(FilePath);
        store.Save(s);

        var back = store.Load(out _).Buffs;
        Assert.Equal(80, back.Size);
        Assert.False(back.Of(BuffKind.뎀증).Visible);
        Assert.True(back.Of(BuffKind.공증).Visible);
    }
}
