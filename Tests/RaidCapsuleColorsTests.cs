using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class RaidCapsuleColorsTests
{
    [Fact]
    public void Every_listed_tier_has_its_own_colour_and_none_is_the_unknown_grey()
    {
        var defaults = RaidCapsuleColors.Editable.Select(RaidCapsuleColors.DefaultOf).ToList();
        Assert.Equal(defaults.Count, defaults.Distinct().Count());
        Assert.DoesNotContain(RaidCapsuleColors.DefaultOf(DifficultyTier.Unknown), defaults);
    }

    [Fact]
    public void Nothing_stored_gives_the_defaults()
    {
        var c = new RaidCapsuleColors();
        Assert.Equal("#F07C2C", c.HexOf(DifficultyTier.Hard));
        Assert.True(c.IsDefault(DifficultyTier.Hard));
    }

    [Fact]
    public void A_set_colour_is_kept_in_upper_case_and_reset_drops_it()
    {
        var c = new RaidCapsuleColors();
        c.Set(DifficultyTier.Hard, "#112233");
        Assert.Equal("#112233", c.HexOf(DifficultyTier.Hard));
        c.Set(DifficultyTier.Normal, "#abc");
        Assert.Equal("#AABBCC", c.HexOf(DifficultyTier.Normal));
        c.Reset(DifficultyTier.Hard);
        Assert.Equal("#F07C2C", c.HexOf(DifficultyTier.Hard));
    }

    [Fact]
    public void Setting_the_default_or_a_bad_code_stores_nothing()
    {
        var c = new RaidCapsuleColors();
        c.Set(DifficultyTier.Single, "#4cb08a");
        c.Set(DifficultyTier.Nightmare, "nope");
        Assert.Empty(c.Colors);
    }

    [Fact]
    public void A_bad_stored_code_falls_back_to_the_default()
    {
        var c = new RaidCapsuleColors { Colors = { [DifficultyTier.Nightmare] = "zzz" } };
        Assert.Equal("#8F4FD6", c.HexOf(DifficultyTier.Nightmare));
    }

    [Fact]
    public void Survives_a_json_round_trip_and_an_old_file_without_it()
    {
        var c = new RaidCapsuleColors();
        c.Set(DifficultyTier.Hard, "#112233");
        var back = System.Text.Json.JsonSerializer.Deserialize<RaidCapsuleColors>(System.Text.Json.JsonSerializer.Serialize(c))!;
        Assert.Equal("#112233", back.HexOf(DifficultyTier.Hard));
        Assert.Equal("#5E8FD6", new Settings().RaidCapsules.HexOf(DifficultyTier.Normal));
    }
}
