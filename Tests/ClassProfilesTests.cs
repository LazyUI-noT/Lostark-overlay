using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Per-class settings, yours (나) and party members' (파티원): what is shown, where, in what colours and at
/// what size, in that order, with the 내 설정 적용 ticks and received sizes and layouts fitted to the window.</summary>
public class ClassProfilesTests
{
    const string Warlord = "워로드", Bard = "바드";

    static Settings WithCharacter()
    {
        var s = new Settings { ActiveCharacter = "워로드 · 고독한기사" };
        s.Characters["워로드 · 고독한기사"] = new() { Class = Warlord, Subclass = "고독한기사" };
        return s;
    }

    static string MineColor(Settings s, SlotGroup g) => ClassProfiles.Mine(s).Colors[g];
    static string MemberColor(Settings s, SlotGroup g, string? cls, Role role = Role.딜러, Look? look = null) =>
        ClassProfiles.Member(s, cls, role, look).Colors[g];

    // ── yours ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_각성기_변화_box_is_red_by_default() =>
        Assert.Equal("#FF4D4D", new OverlaySettings().AwakeningChangedColor);

    /// <summary>Each of the eight 일반스킬 can have its own colour (yours, 개인 설정); the rest keep the group's. Never
    /// sent to the party.</summary>
    [Fact]
    public void One_일반스킬_can_have_its_own_colour_in_개인_설정()
    {
        var s = WithCharacter();
        var c = ClassProfiles.Edit(s, Warlord);
        (c.Mine.Color, c.Mine.Colors[SlotGroup.일반스킬], c.Mine.SlotColors[SlotId.일반3]) = (ColorSource.Personal, "#111111", "#333333");
        var a = ClassProfiles.Mine(s);
        Assert.Equal("#333333", a.ColorOf(SlotId.일반3, SlotGroup.일반스킬));
        Assert.Equal("#111111", a.ColorOf(SlotId.일반4, SlotGroup.일반스킬));
        c.Mine.Color = ColorSource.Default;   // learned colours: a slot's own is set aside, not lost
        Assert.Equal(ClassProfiles.Mine(s).Colors[SlotGroup.일반스킬], ClassProfiles.Mine(s).ColorOf(SlotId.일반3, SlotGroup.일반스킬));
        Assert.Equal("#333333", c.Mine.SlotColors[SlotId.일반3]);
    }

    /// <summary>일반스킬 모두 같은 색: on (the default for a profile with no skill colours), every 일반스킬 takes the group
    /// colour and its own colours are set aside, not lost; a profile that already has some starts with it off.</summary>
    [Fact]
    public void 일반스킬_one_colour_sets_the_skills_own_colours_aside()
    {
        var s = WithCharacter();
        var c = ClassProfiles.Edit(s, Warlord);
        c.Mine.Color = ColorSource.Personal;
        Assert.True(c.Mine.SkillsOneColor);
        c.Mine.SlotColors[SlotId.일반3] = "#333333";
        Assert.False(c.Mine.SkillsOneColor);
        c.Mine.SkillsUnified = true;
        Assert.Equal(ClassProfiles.Mine(s).Colors[SlotGroup.일반스킬], ClassProfiles.Mine(s).ColorOf(SlotId.일반3, SlotGroup.일반스킬));
        Assert.Equal("#333333", c.Mine.SlotColors[SlotId.일반3]);
    }

    /// <summary>쿨타임 색 자동 (the default): no cooldown colour, the renderer dims the box colour. Off, each group and each
    /// 일반스킬 with its own colour may have its own cooldown colour; one not set stays automatic.</summary>
    [Fact]
    public void Cooldown_colours_are_automatic_until_turned_off()
    {
        var s = WithCharacter();
        var c = ClassProfiles.Edit(s, Warlord);
        c.Mine.Color = ColorSource.Personal;
        (c.Mine.DimColors[SlotGroup.일반스킬], c.Mine.SlotColors[SlotId.일반3], c.Mine.SlotDimColors[SlotId.일반3]) = ("#010101", "#333333", "#030303");
        Assert.True(c.Mine.AutoDim);
        Assert.Null(ClassProfiles.Mine(s).DimOf(SlotId.일반4, SlotGroup.일반스킬));

        c.Mine.AutoDim = false;
        var a = ClassProfiles.Mine(s);
        Assert.Equal("#010101", a.DimOf(SlotId.일반4, SlotGroup.일반스킬));
        Assert.Equal("#030303", a.DimOf(SlotId.일반3, SlotGroup.일반스킬));
        Assert.Null(a.DimOf(SlotId.각성기, SlotGroup.각성기));
        c.Mine.SlotDimColors.Clear();
        Assert.Null(ClassProfiles.Mine(s).DimOf(SlotId.일반3, SlotGroup.일반스킬));   // its own colour, dimmed automatically
        c.Mine.SkillsUnified = true;
        Assert.Equal("#010101", ClassProfiles.Mine(s).DimOf(SlotId.일반3, SlotGroup.일반스킬));
    }

    /// <summary>Each 일반스킬's 준비되면 반짝임 and 쿨 돈 동안 계속 빛남 follow the group switch until set for that skill;
    /// key letters are off until picked.</summary>
    [Fact]
    public void A_일반스킬_follows_the_group_shine_until_set_on_its_own()
    {
        var o = new MineProfile();
        o.NoShine.Add(SlotGroup.일반스킬);
        o.Pulse.Add(SlotGroup.일반스킬);
        Assert.Equal((false, true), (o.Shines(SlotId.일반1, SlotGroup.일반스킬), o.Pulses(SlotId.일반1, SlotGroup.일반스킬)));
        (o.SlotShine[SlotId.일반1], o.SlotPulse[SlotId.일반1]) = (true, false);
        Assert.Equal((true, false), (o.Shines(SlotId.일반1, SlotGroup.일반스킬), o.Pulses(SlotId.일반1, SlotGroup.일반스킬)));
        Assert.Equal((false, true), (o.Shines(SlotId.일반2, SlotGroup.일반스킬), o.Pulses(SlotId.일반2, SlotGroup.일반스킬)));
        Assert.Empty(o.KeyLetters);
    }

    /// <summary>쿨 돌기 직전 깜빡임 (the buff bars' last-0.9 s sweep on a skill box): off for every group until turned on;
    /// an 일반스킬 follows its group until set on its own.</summary>
    [Fact]
    public void The_end_flash_is_off_until_turned_on_for_a_group_or_a_skill()
    {
        var o = new MineProfile();
        Assert.All(Enum.GetValues<SlotGroup>(), g => Assert.False(o.EndFlashes(g)));
        o.EndFlash.Add(SlotGroup.일반스킬);
        Assert.True(o.EndFlashes(SlotId.일반1, SlotGroup.일반스킬));
        o.SlotEndFlash[SlotId.일반1] = false;
        Assert.Equal((false, true), (o.EndFlashes(SlotId.일반1, SlotGroup.일반스킬), o.EndFlashes(SlotId.일반2, SlotGroup.일반스킬)));

        var back = System.Text.Json.JsonSerializer.Deserialize<MineProfile>(System.Text.Json.JsonSerializer.Serialize(o, GaugeOverlay.Core.Data.SettingsStore.Json), GaugeOverlay.Core.Data.SettingsStore.Json)!;
        Assert.Equal((false, true), (back.EndFlashes(SlotId.일반1, SlotGroup.일반스킬), back.EndFlashes(SlotId.일반2, SlotGroup.일반스킬)));
    }

    /// <summary>쿨 돈 동안 계속 빛남's look per group: by default 각성기, 초각스 and 아덴스킬 sweep a diagonal band and
    /// 일반스킬 breathe, all white; each group's style and colour can be changed and are saved.</summary>
    [Fact]
    public void Each_group_glows_in_its_own_style_and_colour_while_ready()
    {
        var o = new MineProfile();
        Assert.Equal([ReadyGlowStyle.Diagonal, ReadyGlowStyle.Diagonal, ReadyGlowStyle.Breathe, ReadyGlowStyle.Diagonal],
            Enum.GetValues<SlotGroup>().Select(o.PulseStyleOf));
        Assert.All(Enum.GetValues<SlotGroup>(), g => Assert.Equal("#FFFFFF", o.PulseColorOf(g)));

        (o.PulseStyle[SlotGroup.초각스], o.PulseColor[SlotGroup.초각스]) = (ReadyGlowStyle.Outline, "#FF0000");
        var json = GaugeOverlay.Core.Data.SettingsStore.Json;
        var back = System.Text.Json.JsonSerializer.Deserialize<MineProfile>(System.Text.Json.JsonSerializer.Serialize(o, json), json)!;
        Assert.Equal((ReadyGlowStyle.Outline, "#FF0000"), (back.PulseStyleOf(SlotGroup.초각스), back.PulseColorOf(SlotGroup.초각스)));
        Assert.Equal(ReadyGlowStyle.Diagonal, back.PulseStyleOf(SlotGroup.각성기));
    }

    [Fact]
    public void With_nothing_set_your_boxes_are_the_built_in_colour() =>
        Assert.Equal(ClassProfiles.BuiltIn[SlotGroup.일반스킬], MineColor(WithCharacter(), SlotGroup.일반스킬));

    [Fact]
    public void 개인_설정_uses_your_colours_for_the_class_and_the_built_in_one_where_you_set_none()
    {
        var s = WithCharacter();
        var c = ClassProfiles.Edit(s, Warlord);
        (c.Mine.Color, c.Mine.Colors[SlotGroup.일반스킬]) = (ColorSource.Personal, "#000002");

        Assert.Equal("#000002", MineColor(s, SlotGroup.일반스킬));
        Assert.Equal(ClassProfiles.BuiltIn[SlotGroup.각성기], MineColor(s, SlotGroup.각성기));
        c.Mine.Color = ColorSource.Default;   // the colours stay, unused
        Assert.NotEqual("#000002", MineColor(s, SlotGroup.일반스킬));
    }

    [Fact]
    public void Each_class_has_its_own_display_and_layout()
    {
        var s = WithCharacter();
        ClassProfiles.Edit(s, Warlord).Mine.Groups[SlotGroup.초각스] = false;
        ClassProfiles.Edit(s, Bard).Mine.Layout = new() { [Part.일반1] = new(0, 0, 10, 10) };

        Assert.False(ClassProfiles.Mine(s).Groups[SlotGroup.초각스]);
        Assert.Null(ClassProfiles.Mine(s).Layout);
        s.Characters["워로드 · 고독한기사"].Class = Bard;   // class changed: the other class's settings apply
        Assert.True(ClassProfiles.Mine(s).Groups[SlotGroup.초각스]);
        Assert.NotNull(ClassProfiles.Mine(s).Layout);
    }

    [Fact]
    public void A_class_without_its_own_settings_starts_from_the_template_and_editing_one_leaves_the_template()
    {
        var s = new Settings();
        ClassProfiles.Edit(s, ClassProfiles.Template).Mine.HiddenSlots.Add(SlotId.일반8);

        Assert.Contains(SlotId.일반8, ClassProfiles.Mine(s).Hidden);   // no character: the template
        var bard = ClassProfiles.Edit(s, Bard);
        Assert.Contains(SlotId.일반8, bard.Mine.HiddenSlots);          // a copy of it
        bard.Mine.HiddenSlots.Clear();
        Assert.Contains(SlotId.일반8, ClassProfiles.Of(s, ClassProfiles.Template).Mine.HiddenSlots);
    }

    // ── party members: colour ───────────────────────────────────────────────────────

    [Fact]
    public void 기본_for_members_is_the_role_colour_on_일반스킬_and_the_class_colour_elsewhere()
    {
        var s = new Settings();
        s.Party.RoleColor[Role.서포터] = "#00FF00";

        Assert.Equal("#00FF00", MemberColor(s, SlotGroup.일반스킬, Bard, Role.서포터));
        Assert.Equal(ClassProfiles.BuiltIn[SlotGroup.각성기], MemberColor(s, SlotGroup.각성기, Bard, Role.서포터));
        Assert.Equal(s.Party.RoleColor[Role.딜러], MemberColor(s, SlotGroup.일반스킬, null));   // class unknown
    }

    [Fact]
    public void 파티원_설정_받기_uses_what_the_member_sent_and_기본_where_they_sent_nothing()
    {
        var s = new Settings();
        ClassProfiles.Edit(s, Bard).Members.Color = ColorSource.Received;
        var look = new Look(new Dictionary<SlotGroup, string> { [SlotGroup.일반스킬] = "#123456" });

        Assert.Equal("#123456", MemberColor(s, SlotGroup.일반스킬, Bard, Role.서포터, look));
        Assert.Equal(s.Party.RoleColor[Role.서포터], MemberColor(s, SlotGroup.일반스킬, Bard, Role.서포터, null));
    }

    [Fact]
    public void 개인_설정_for_members_is_kept_apart_from_yours()
    {
        var s = new Settings();
        var c = ClassProfiles.Edit(s, Bard);
        (c.Mine.Color, c.Mine.Colors[SlotGroup.일반스킬]) = (ColorSource.Personal, "#AAAAAA");
        (c.Members.Color, c.Members.Colors[SlotGroup.일반스킬]) = (ColorSource.Personal, "#BBBBBB");

        Assert.Equal("#BBBBBB", MemberColor(s, SlotGroup.일반스킬, Bard));
        c.Members.UseMineColor = true;   // 내 설정 적용 (색): your setting for the class, theirs kept
        Assert.Equal("#AAAAAA", MemberColor(s, SlotGroup.일반스킬, Bard));
        c.Members.UseMineColor = false;
        Assert.Equal("#BBBBBB", MemberColor(s, SlotGroup.일반스킬, Bard));
    }

    [Fact]
    public void A_member_who_changes_class_takes_the_new_class_settings()
    {
        var s = new Settings();
        ClassProfiles.Edit(s, Warlord).Members.Groups[SlotGroup.아덴스킬] = false;

        Assert.False(ClassProfiles.Member(s, Warlord, Role.딜러, null).Groups[SlotGroup.아덴스킬]);
        Assert.True(ClassProfiles.Member(s, Bard, Role.서포터, null).Groups[SlotGroup.아덴스킬]);
    }

    // ── party members: display, size ────────────────────────────────────────────────

    [Fact]
    public void 내_설정_적용_for_display_and_size_uses_yours_and_keeps_theirs()
    {
        var s = new Settings();
        s.Overlay.Width = 1.5;
        var c = ClassProfiles.Edit(s, Bard);
        c.Mine.Groups[SlotGroup.초각스] = false;
        (c.Members.Width, c.Members.Height) = (0.8, 0.9);

        var theirs = ClassProfiles.Member(s, Bard, Role.서포터, null);
        Assert.True(theirs.Groups[SlotGroup.초각스]);
        Assert.Equal((0.8, 0.9), (theirs.Width, theirs.Height));

        (c.Members.UseMineDisplay, c.Members.UseMineSize) = (true, true);
        var mine = ClassProfiles.Member(s, Bard, Role.서포터, null);
        Assert.False(mine.Groups[SlotGroup.초각스]);
        Assert.Equal((1.5, 1.0), (mine.Width, mine.Height));
        Assert.Equal((0.8, 0.9), (c.Members.Width, c.Members.Height));
    }

    [Fact]
    public void Received_size_and_layout_are_ignored_unless_receiving_is_on()
    {
        var s = new Settings();
        var look = new Look(new Dictionary<SlotGroup, string>(), 2.0, 2.0, new Dictionary<Part, Cell> { [Part.일반1] = new(0, 0, 500, 500) });

        var a = ClassProfiles.Member(s, Bard, Role.서포터, look);

        Assert.Equal((1.0, 1.0), (a.Width, a.Height));
        Assert.Null(a.Layout);
    }

    [Fact]
    public void A_received_size_keeps_its_proportions_inside_your_size_and_never_grows_it()
    {
        var s = new Settings { Party = { ReceiveSize = true } };
        (ClassProfiles.Edit(s, Bard).Members.Width, ClassProfiles.Edit(s, Bard).Members.Height) = (1.0, 1.0);

        var big = ClassProfiles.Member(s, Bard, Role.서포터, new Look(new Dictionary<SlotGroup, string>(), 2.0, 1.0));
        Assert.Equal(1.0, big.Width, 3);    // twice as wide as tall, fitted to your width
        Assert.Equal(0.5, big.Height, 3);

        var small = ClassProfiles.Member(s, Bard, Role.서포터, new Look(new Dictionary<SlotGroup, string>(), 0.5, 0.5));
        Assert.Equal((1.0, 1.0), (small.Width, small.Height));   // same proportions: fills your size
    }

    [Fact]
    public void A_received_layout_is_scaled_to_fit_the_area_of_yours()
    {
        var s = new Settings { Party = { ReceiveLayout = true } };
        var local = FreeLayout.Default(s.Gauge.Thickness);
        double areaW = local.Values.Max(c => c.Right) - local.Values.Min(c => c.X);
        var wide = new Dictionary<Part, Cell> { [Part.일반1] = new(0, 0, areaW * 4, 10) };   // four times as wide

        var a = ClassProfiles.Member(s, Bard, Role.서포터, new Look(new Dictionary<SlotGroup, string>(), 1.0, 1.0, wide));

        Assert.Same(wide, a.Layout);
        Assert.Equal(0.25, a.Width, 3);   // the renderer scales cells by the size: 4× as wide at a quarter = your width
        Assert.Equal(0.25, a.Height, 3);  // uniformly, keeping its proportions
    }

    [Fact]
    public void Without_a_received_layout_receiving_layouts_keeps_yours()
    {
        var s = new Settings { Party = { ReceiveLayout = true } };
        var cells = new Dictionary<Part, Cell> { [Part.일반1] = new(0, 0, 34, 34) };
        ClassProfiles.Edit(s, Bard).Members.Layout = cells;

        var a = ClassProfiles.Member(s, Bard, Role.서포터, new Look(new Dictionary<SlotGroup, string>()));

        Assert.Same(cells, a.Layout);
        Assert.Equal((1.0, 1.0), (a.Width, a.Height));
    }

    // ── copying, the gold frame ─────────────────────────────────────────────────────

    [Fact]
    public void Copy_takes_only_the_chosen_parts_once()
    {
        var s = new Settings();
        s.Overlay.Width = 1.4;
        var from = ClassProfiles.Edit(s, Bard).Mine;
        (from.Color, from.Colors[SlotGroup.일반스킬], from.Layout) = (ColorSource.Personal, "#ABCDEF", new() { [Part.일반1] = new(1, 2, 3, 4) });
        from.Groups[SlotGroup.초각스] = false;
        var to = ClassProfiles.Edit(s, Warlord).Members;

        ClassProfiles.Copy(s, from, [to], CopyParts.Color | CopyParts.Size);

        Assert.Equal((ColorSource.Personal, "#ABCDEF"), (to.Color, to.Colors[SlotGroup.일반스킬]));
        Assert.Equal(1.4, to.Width);   // your size comes from your window
        Assert.True(to.Groups[SlotGroup.초각스]);   // display not chosen
        Assert.Null(to.Layout);
        from.Colors[SlotGroup.일반스킬] = "#000000";
        Assert.Equal("#ABCDEF", to.Colors[SlotGroup.일반스킬]);   // a copy, not a link
    }

    [Fact]
    public void 내_설정_복사_copies_everything_including_what_is_shown()
    {
        var s = new Settings();
        var c = ClassProfiles.Edit(s, Bard);
        (c.Mine.Gauge, c.Mine.Groups[SlotGroup.초각스]) = (false, false);
        c.Mine.HiddenSlots.Add(SlotId.일반8);

        ClassProfiles.Copy(s, c.Mine, [c.Members], CopyParts.All);

        Assert.False(c.Members.Gauge);
        Assert.False(c.Members.Groups[SlotGroup.초각스]);
        Assert.Contains(SlotId.일반8, c.Members.HiddenSlots);
        c.Mine.HiddenSlots.Clear();
        Assert.Contains(SlotId.일반8, c.Members.HiddenSlots);
    }


    [Fact]
    public void Copying_파티원_설정_받기_to_your_profile_becomes_기본()
    {
        var s = new Settings();
        var from = ClassProfiles.Edit(s, Bard).Members;
        from.Color = ColorSource.Received;
        var to = ClassProfiles.Edit(s, Bard).Mine;

        ClassProfiles.Copy(s, from, [to], CopyParts.Color);

        Assert.Equal(ColorSource.Default, to.Color);
    }

    [Theory]
    [InlineData("#FFD24A", "#FFD24A", true)]
    [InlineData("#FFD24A", "#F0C850", true)]
    [InlineData("#FFD24A", "#FF6B70", false)]
    [InlineData("#FFD24A", "#8098FF", false)]
    public void A_colour_close_to_the_gold_frame_is_flagged(string gold, string colour, bool close) =>
        Assert.Equal(close, GaugeOverlay.Core.Vision.ColorDistance.CloseTo(gold, colour));

    // ── what the engine reads ───────────────────────────────────────────────────────────

    [Fact]
    public void A_slot_neither_shown_nor_sent_to_the_party_is_not_read()
    {
        var s = WithCharacter();   // a class following 기본 (with none, 아덴 slots are never read)
        var mine = ClassProfiles.Edit(s, null).Mine;
        mine.Groups[SlotGroup.초각스] = false;
        mine.Groups[SlotGroup.각성기] = false;
        mine.HiddenSlots.Add(SlotId.일반8);
        Assert.Empty(ClassProfiles.Reads(s, []).Skip);   // all sent

        s.Party.Send[SlotGroup.초각스] = false;
        s.Party.Send[SlotGroup.일반스킬] = false;
        var plan = ClassProfiles.Reads(s, []);
        Assert.Equal([SlotId.초각스, SlotId.일반8], plan.Skip.Order());
        Assert.True(plan.Label);   // 각성기 is still sent

        s.Party.Send[SlotGroup.각성기] = false;
        Assert.False(ClassProfiles.Reads(s, []).Label);
    }

    [Fact]
    public void The_gauge_is_read_while_it_is_shown_or_sent()
    {
        var s = WithCharacter();
        Assert.True(ClassProfiles.Reads(s, []).Gauge);
        ClassProfiles.Edit(s, null).Mine.Gauge = false;
        Assert.True(ClassProfiles.Reads(s, []).Gauge);   // still sent
        s.Party.SendGauge = false;
        Assert.False(ClassProfiles.Reads(s, []).Gauge);
    }

    [Fact]
    public void The_buff_row_is_read_only_while_an_indicator_is_on()
    {
        var s = new Settings();
        s.Buffs.Size = 90;
        Assert.Equal(90, ClassProfiles.Reads(s, []).BuffSize);
        foreach (var k in Enum.GetValues<BuffKind>()) s.Buffs.Edit(k).Visible = false;
        Assert.Null(ClassProfiles.Reads(s, []).BuffSize);
    }

    /// <summary>기본 with no class known yet: buffs, 초각스, 각성기 and 일반스킬 only. The 아덴 slots (Z, X) and the gauge
    /// are neither shown nor read, whatever 기본's boxes say; a class that follows 기본 keeps them.</summary>
    [Fact]
    public void With_no_class_the_아덴_slots_and_gauge_are_left_out()
    {
        var s = new Settings();
        var none = ClassProfiles.Mine(s);
        Assert.False(none.Gauge);
        Assert.Contains(SlotId.아덴1, none.Hidden);
        Assert.Contains(SlotId.아덴2, none.Hidden);
        var reads = ClassProfiles.Reads(s, []);
        Assert.False(reads.Gauge);
        Assert.Contains(SlotId.아덴1, reads.Skip);
        Assert.DoesNotContain(SlotId.일반1, reads.Skip);
        Assert.DoesNotContain(SlotId.초각스, reads.Skip);

        var known = WithCharacter();
        Assert.True(ClassProfiles.Mine(known).Gauge);
        Assert.DoesNotContain(SlotId.아덴1, ClassProfiles.Mine(known).Hidden);
        Assert.DoesNotContain(SlotId.아덴1, ClassProfiles.Reads(known, [SlotId.아덴1, SlotId.아덴2]).Skip);
    }
}
