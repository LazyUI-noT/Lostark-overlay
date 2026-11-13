using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정 edits 편집 중's 직업각인 while the overlay stays on the played one (edit-follows-pick.plan.md).</summary>
public class EditTargetTests
{
    const string Lone = "워로드 · 고독한기사", Combat = "워로드 · 전투태세";

    static Settings Playing(string key)
    {
        var s = SettingsStore.Normalize(new Settings());
        s.Characters[Lone] = new() { Class = "워로드", Subclass = "고독한기사" };
        s.Characters[Combat] = new() { Class = "워로드", Subclass = "전투태세" };
        Go(s, key);
        return s;
    }

    static void Go(Settings s, string key)
    {
        s.ActiveCharacter = key;
        s.SyncClass();
    }

    [Fact]
    public void Editing_the_played_class_edits_the_live_values()
    {
        var s = Playing(Lone);

        Assert.Null(s.EditTarget(Lone));
    }

    [Fact]
    public void An_edit_of_another_class_lands_in_its_entry_and_leaves_the_live_values()
    {
        var s = Playing(Lone);
        s.Overlay.Left = 100;

        var target = s.EditTarget(Combat)!;
        target.Overlay.Left = 500;
        target.Gauge.Fill = "#123456";
        s.KeepEdit(Combat, target);

        Assert.Equal(100, s.Overlay.Left);
        Assert.NotEqual("#123456", s.Gauge.Fill);
        Assert.Equal(500, s.Profiles[0].EntryOf(Combat).Overlay.Left);
        Assert.Equal("#123456", s.Profiles[0].EntryOf(Combat).GaugeLook!.Fill);
    }

    [Fact]
    public void A_reset_on_the_target_resets_only_that_classes_entry()
    {
        var s = Playing(Lone);
        s.Gauge.Fill = "#111111";
        var edit = s.EditTarget(Combat)!;
        edit.Gauge.Fill = "#222222";
        s.KeepEdit(Combat, edit);

        var target = s.EditTarget(Combat)!;
        SettingsResets.Gauge(target);
        s.KeepEdit(Combat, target);

        Assert.Equal("#111111", s.Gauge.Fill);
        Assert.Equal(new Settings().Gauge.Fill, s.Profiles[0].EntryOf(Combat).GaugeLook!.Fill);
    }

    [Fact]
    public void Mirroring_takes_live_only_values_and_keeps_the_targets_own()
    {
        var s = Playing(Lone);
        var target = s.EditTarget(Combat)!;
        var (amsu, judgment, boxes) = (target.Amsu, target.Judgment, target.Debuffs.Boxes);
        target.Overlay.Left = 500;
        target.Gauge.Fill = "#222222";
        (s.Overlay.TimerFont, s.Meter.Value, s.Raids.Opacity, s.Debuffs.HideWhenEmpty) = (20, MeterValue.DPS, 0.5, true);   // edited live
        (s.Overlay.Left, s.Gauge.Fill) = (100, "#111111");   // Lone's own

        s.MirrorLive(target);

        Assert.Equal((20.0, MeterValue.DPS, 0.5, true), (target.Overlay.TimerFont, target.Meter.Value, target.Raids.Opacity, target.Debuffs.HideWhenEmpty));
        Assert.Equal((500.0, "#222222"), (target.Overlay.Left, target.Gauge.Fill));
        Assert.Same(amsu, target.Amsu);   // rows bound to them keep editing the target
        Assert.Same(judgment, target.Judgment);
        Assert.Same(boxes, target.Debuffs.Boxes);
        Assert.Equal((100.0, "#111111"), (s.Overlay.Left, s.Gauge.Fill));
    }

    [Fact]
    public void An_edit_kept_after_its_class_became_the_played_one_goes_on_screen()
    {
        var s = Playing(Lone);
        var target = s.EditTarget(Combat)!;
        Go(s, Combat);
        target.Gauge.Fill = "#222222";

        s.KeepEdit(Combat, target);

        Assert.Equal("#222222", s.Gauge.Fill);
    }

    [Fact]
    public void The_template_key_edits_the_shared_entry()
    {
        var s = Playing(Lone);
        var target = s.EditTarget(ClassProfiles.Template)!;
        target.Overlay.Left = 333;

        s.KeepEdit(ClassProfiles.Template, target);

        Assert.Equal(333, s.Profiles[0].Overlay.Left);
    }

    [Fact]
    public void Without_an_active_profile_there_is_no_target()
    {
        var s = Playing(Lone);
        s.ActiveProfile = s.Profiles.Count;

        Assert.Null(s.EditTarget(Combat));
    }

    [Fact]
    public void The_target_shows_the_classes_stored_values()
    {
        var s = Playing(Combat);
        s.Overlay.Left = 500;
        Go(s, Lone);

        Assert.Equal(500, s.EditTarget(Combat)!.Overlay.Left);
    }

    [Fact]
    public void A_class_without_an_entry_shows_the_unkept_live_shared_values()
    {
        var s = Playing(Lone);
        s.ActiveCharacter = null;
        s.SyncClass();   // played: no 직업각인, the shared values
        s.Overlay.Left = 250;   // not kept yet

        Assert.Equal(250, s.EditTarget(Combat)!.Overlay.Left);
    }

    [Fact]
    public void Playing_the_edited_class_later_shows_the_edit()
    {
        var s = Playing(Lone);
        var target = s.EditTarget(Combat)!;
        target.Overlay.Left = 500;
        s.KeepEdit(Combat, target);

        Go(s, Combat);

        Assert.Equal(500, s.Overlay.Left);
        Assert.Null(s.EditTarget(Combat));   // now played: the live values again
    }

    [Fact]
    public void Editing_basic_changes_the_shared_entry_and_a_played_class_following_it()
    {
        var s = Playing(Lone);   // Lone has no entry: it follows the shared one

        var target = s.EditTarget(ClassProfiles.Template)!;
        target.Overlay.Left = 333;
        s.KeepEdit(ClassProfiles.Template, target);

        Assert.Equal(333, s.Profiles[0].Overlay.Left);
        Assert.Equal(333, s.Overlay.Left);
        Go(s, Combat);
        Assert.Equal(333, s.Overlay.Left);
        Go(s, Lone);
        Assert.Equal(333, s.Overlay.Left);   // Lone still follows: no entry was made from the old values
        Assert.False(s.Profiles[0].Classes!.ContainsKey(Lone));
    }

    [Fact]
    public void Editing_basic_leaves_a_played_class_with_its_own_entry()
    {
        var s = Playing(Lone);
        s.Overlay.Left = 100;   // Lone's own

        var target = s.EditTarget(null)!;
        target.Overlay.Left = 333;
        s.KeepEdit(null, target);

        Assert.Equal(100, s.Overlay.Left);
        Assert.Equal(333, s.Profiles[0].Overlay.Left);
    }

    [Fact]
    public void An_edit_through_the_target_keeps_the_live_raid_count_look_and_debuff_hide()
    {
        var s = Playing(Lone);
        var target = s.EditTarget(Combat)!;
        (s.Raids.Background, s.Debuffs.HideWhenEmpty) = ("#ABCDEF", true);   // edited live after the target was made
        target.Overlay.Left = 500;

        s.KeepEdit(Combat, target);

        Assert.Equal(("#ABCDEF", true), (s.Raids.Background, s.Debuffs.HideWhenEmpty));
        Assert.All(s.Profiles[0].Entries(), e => Assert.Equal(("#ABCDEF", true), (e.RaidLook!.Background, e.DebuffHideWhenEmpty!.Value)));
    }

    [Fact]
    public void An_edit_through_the_target_keeps_live_party_spots_and_buff_bars()
    {
        var s = Playing(Lone);
        var target = s.EditTarget(Combat)!;
        s.Party.Left = 77;   // edited live after the target was made
        s.Buffs.Edit(BuffKind.공증).Color = "#ABCDEF";
        target.Overlay.Left = 500;

        s.KeepEdit(Combat, target);

        Assert.Equal(77, s.Party.Left);
        Assert.Equal("#ABCDEF", s.Buffs.Of(BuffKind.공증).Color);
        Assert.Equal(77, s.Profiles[0].BoundPartyLeft);
        Assert.All(s.Profiles[0].Entries(), e => Assert.Equal("#ABCDEF", e.BuffLook!.Bars[BuffKind.공증].Color));
    }

    [Fact]
    public void Editing_basic_keeps_live_buff_bars_on_screen()
    {
        var s = Playing(Lone);   // follows the shared entry
        var target = s.EditTarget(null)!;
        s.Buffs.Edit(BuffKind.공증).Color = "#ABCDEF";
        target.Overlay.Left = 333;

        s.KeepEdit(null, target);

        Assert.Equal("#ABCDEF", s.Buffs.Of(BuffKind.공증).Color);
    }

    [Fact]
    public void A_profile_switch_after_an_edit_loses_nothing()
    {
        var s = Playing(Lone);
        var target = s.EditTarget(Combat)!;
        target.Overlay.Left = 500;
        s.KeepEdit(Combat, target);

        s.SwitchProfile(1);
        s.SwitchProfile(0);
        Go(s, Combat);

        Assert.Equal(500, s.Overlay.Left);
    }

    [Fact]
    public void An_edit_equal_to_the_shared_values_keeps_no_entry()
    {
        var s = Playing(Lone);
        var target = s.EditTarget(Combat)!;

        s.KeepEdit(Combat, target);

        Assert.False(s.Profiles[0].Classes!.ContainsKey(Combat));
    }

    [Fact]
    public void The_target_moves_buff_bars_and_party_windows_on_the_live_settings()
    {
        var s = Playing(Lone);
        var target = s.EditTarget(Combat)!;

        target.Buffs.Left = 300;
        target.Party.Left = 400;

        Assert.Equal(300, s.Buffs.Left);
        Assert.Equal(400, s.Party.Left);
    }

    [Fact]
    public void The_target_shows_box_and_gauge_bar_edits_made_on_the_live_settings()
    {
        var s = Playing(Lone);
        var target = s.EditTarget(Combat)!;

        s.Classes["워로드"] = new();
        s.GaugeBars[Combat] = new();

        Assert.Same(s.Classes["워로드"], target.Classes["워로드"]);
        Assert.Same(s.GaugeBars[Combat], target.GaugeBars[Combat]);
    }

    [Fact]
    public void Undo_of_an_edit_puts_the_classes_entry_back()
    {
        var s = Playing(Lone);
        var before = SettingsStore.Copy(s);
        var target = s.EditTarget(Combat)!;
        target.Overlay.Left = 500;
        s.KeepEdit(Combat, target);

        s.CopyFrom(before);

        Assert.NotEqual(500, s.EditTarget(Combat)!.Overlay.Left);
    }
}
