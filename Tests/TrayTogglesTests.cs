using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Tray → 표시: each item turns the same setting as the window's own show switch.</summary>
public class TrayTogglesTests
{
    const string Bard = "바드 · 진실된 용기";

    static Settings WithCharacter() => new() { ActiveCharacter = Bard };

    [Theory]
    [InlineData(OverlayPart.Skills)]
    [InlineData(OverlayPart.Buffs)]
    [InlineData(OverlayPart.Gauge)]
    [InlineData(OverlayPart.Judgment)]
    [InlineData(OverlayPart.Debuffs)]
    [InlineData(OverlayPart.Meter)]
    [InlineData(OverlayPart.BarCount)]
    [InlineData(OverlayPart.Party)]
    public void Each_part_turns_off_and_on_again(OverlayPart part)
    {
        var s = WithCharacter();
        TrayToggles.Show(s, part, true);
        Assert.True(TrayToggles.IsShown(s, part));

        TrayToggles.Show(s, part, false);
        Assert.False(TrayToggles.IsShown(s, part));
    }

    [Fact]
    public void Skills_off_hides_every_group_of_the_character_shown()
    {
        var s = WithCharacter();
        TrayToggles.Show(s, OverlayPart.Skills, false);
        Assert.All(Enum.GetValues<SlotGroup>(), g => Assert.False(ClassProfiles.Of(s, Bard).Mine.Groups[g]));
    }

    [Fact]
    public void Skills_are_off_while_스킬창_읽기_is_and_showing_them_reads_again()
    {
        var s = WithCharacter();
        s.Overlay.SkillRead = false;
        Assert.False(TrayToggles.IsShown(s, OverlayPart.Skills));
        TrayToggles.Show(s, OverlayPart.Skills, true);
        Assert.True(s.Overlay.SkillRead);
        Assert.True(TrayToggles.IsShown(s, OverlayPart.Skills));
    }

    [Fact]
    public void Judgment_without_a_character_stays_off()
    {
        var s = new Settings();
        TrayToggles.Show(s, OverlayPart.Judgment, true);
        Assert.False(TrayToggles.IsShown(s, OverlayPart.Judgment));
    }

    [Fact]
    public void Buffs_shown_while_any_bar_is()
    {
        var s = new Settings();
        foreach (var k in Enum.GetValues<BuffKind>()) s.Buffs.Edit(k).Visible = false;
        Assert.False(TrayToggles.IsShown(s, OverlayPart.Buffs));
        s.Buffs.Edit(BuffKind.공증).Visible = true;
        Assert.True(TrayToggles.IsShown(s, OverlayPart.Buffs));
    }

    [Fact]
    public void The_meter_off_hides_its_window_and_keeps_reading_for_the_records()
    {
        var s = new Settings();
        TrayToggles.Show(s, OverlayPart.Meter, true);
        Assert.True(s.Meter.On && s.Meter.Show);
        TrayToggles.Show(s, OverlayPart.Meter, false);
        Assert.True(s.Meter.On);
        Assert.False(TrayToggles.IsShown(s, OverlayPart.Meter));   // hidden for the character played (ClassProfiles.MeterShown)
    }

    [Fact]
    public void The_bar_count_switch_is_the_main_줄_수_switch_and_keeps_each_gate()
    {
        var s = new Settings();
        s.Raids.Gate("act3-g3").CountWindow = true;
        TrayToggles.Show(s, OverlayPart.BarCount, false);
        Assert.False(s.Raids.CountEnabled);
        Assert.True(s.Raids.Gate("act3-g3").CountWindow);
    }

    [Fact]
    public void Party_windows_are_shown_by_default() => Assert.True(TrayToggles.IsShown(new Settings(), OverlayPart.Party));

    [Fact]
    public void The_tray_debuff_switch_picks_the_raid_by_hand_and_leaves_자동()
    {
        var s = new Settings();
        TrayToggles.Show(s, OverlayPart.Debuffs, true);
        Assert.Equal(Raid.모르둠, s.Debuffs.Raid);
        TrayToggles.Show(s, OverlayPart.Debuffs, false);
        Assert.Equal(Raid.없음, s.Debuffs.Raid);
        Assert.True(s.Raids.DebuffsAuto);   // 설정 → 레이드's switch, not the tray's
    }
}
