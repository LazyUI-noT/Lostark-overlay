using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class HotkeySettingsTests
{
    [Fact]
    public void Every_action_is_listed_with_the_five_profiles_unset_by_default()
    {
        var all = new HotkeySettings().All();
        Assert.Contains((nameof(HotkeySettings.EditMode), "Ctrl+Shift+E"), all);
        for (int i = 1; i <= OverlayProfile.Count; i++) Assert.Contains(($"Profile{i}", ""), all);
    }

    [Fact]
    public void A_profile_action_names_its_profile()
    {
        Assert.Equal(0, HotkeySettings.ProfileOf("Profile1"));
        Assert.Equal(4, HotkeySettings.ProfileOf("Profile5"));
        Assert.Null(HotkeySettings.ProfileOf(nameof(HotkeySettings.EditMode)));
        Assert.Null(HotkeySettings.ProfileOf("Profile6"));
    }

    [Fact]
    public void A_combo_used_twice_or_by_a_skill_key_is_a_clash()
    {
        var s = new HotkeySettings { Profile1 = "Ctrl+Shift+E", Profile2 = "Q", Profile3 = "F5" };
        var clashes = s.Clashes(["Q", "W"]);
        Assert.Equal([nameof(HotkeySettings.EditMode), "Profile1", "Profile2"], clashes.Order());
    }

    [Fact]
    public void Icon_reread_is_an_action_unset_by_default_and_clashes_like_the_others()
    {
        Assert.Contains((nameof(HotkeySettings.RereadIcons), ""), new HotkeySettings().All());
        Assert.Equal([nameof(HotkeySettings.DetectCharacter), nameof(HotkeySettings.RereadIcons)],
            new HotkeySettings { RereadIcons = "Ctrl+Shift+D" }.Clashes([]).Order());
    }

    [Fact]
    public void Unset_profiles_never_clash() => Assert.Empty(new HotkeySettings().Clashes([""]));
}
