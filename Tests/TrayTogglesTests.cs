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
        TrayToggles.Show(s, OverlayPart.Buffs, false);
        s.Buffs.Edit(BuffKind.공증).Visible = true;
        Assert.True(TrayToggles.IsShown(s, OverlayPart.Buffs));
    }

    [Fact]
    public void The_tray_debuff_switch_shows_the_window_always_as_before()
    {
        var s = new Settings();
        TrayToggles.Show(s, OverlayPart.Debuffs, true);
        Assert.False(s.Raids.Gate(RaidSettings.MordumGate).DebuffAuto);   // recognition is not checked on footage yet
        Assert.Equal(Raid.모르둠, s.Debuffs.Raid);
    }
}
