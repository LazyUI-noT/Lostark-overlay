using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>A fight saved with its 전투력 and item level (lostark-api.plan.md phase 5), and the timeline top bar's text for them.</summary>
public class RecordPowerTests
{
    static FightRecord Fight(string? character, string? subclass = "고독한기사") =>
        new(new DateTime(2026, 11, 9, 20, 0, 0), "act3-g3", 300, [], "나", 3, subclass, [], [], [], [], Character: character);

    static readonly Dictionary<string, Dictionary<string, double>> Book = new()
    {
        ["너무아파요살살"] = new() { ["고독한기사"] = 5004.5, ["전투태세"] = 4100 },
    };

    static readonly Dictionary<string, double> Levels = new() { ["너무아파요살살"] = 1791.66 };

    [Fact]
    public void A_fight_takes_its_characters_best_for_the_subclass_played_and_the_last_looked_up_level()
    {
        var r = RecordPower.Stamp(Fight("너무아파요살살"), Book, Levels);

        Assert.Equal(5004.5, r.CombatPower);
        Assert.Equal(1791.66, r.ItemLevel);
    }

    [Fact]
    public void The_subclass_is_matched_with_or_without_spaces()
    {
        Assert.Equal(4100, RecordPower.Stamp(Fight("너무아파요살살", "전투 태세"), Book, Levels).CombatPower);
    }

    [Fact]
    public void Nothing_looked_up_saves_nulls()
    {
        var unnamed = RecordPower.Stamp(Fight(null), Book, Levels);
        var other = RecordPower.Stamp(Fight("김뚜까뚜까"), Book, Levels);
        var newSubclass = RecordPower.Stamp(Fight("너무아파요살살", "분노의망치"), Book, Levels);

        Assert.Equal((null, null), (unnamed.CombatPower, unnamed.ItemLevel));
        Assert.Equal((null, null), (other.CombatPower, other.ItemLevel));
        Assert.Null(newSubclass.CombatPower);
        Assert.Equal(1791.66, newSubclass.ItemLevel);
    }

    [Theory]
    [InlineData(1791.66, 5004.5, "1791.66 · 전투력 5,004")]
    [InlineData(1680.0, null, "1680.00")]
    [InlineData(null, 2345.9, "전투력 2,345")]
    public void The_top_bar_shows_what_was_saved(double? level, double? power, string text)
    {
        Assert.Equal(text, RecordPower.Label(Fight("나") with { ItemLevel = level, CombatPower = power }));
    }

    [Fact]
    public void An_older_record_shows_nothing()
    {
        Assert.Null(RecordPower.Label(Fight("나")));
    }

    [Fact]
    public void An_edit_to_another_character_or_subclass_drops_the_values_it_no_longer_matches()
    {
        var saved = Fight("너무아파요살살") with { CombatPower = 5004.5, ItemLevel = 1791.66 };

        var otherSubclass = RecordPower.Edited(saved, saved with { MySubclass = "전투태세" });
        var otherCharacter = RecordPower.Edited(saved, saved with { Character = "김뚜까뚜까" });
        var sameOne = RecordPower.Edited(saved, saved with { Gate = "act3-g2", MySubclass = "고독한 기사" });

        Assert.Equal((null, null), (otherSubclass.CombatPower, otherSubclass.ItemLevel));
        Assert.Equal((null, null), (otherCharacter.CombatPower, otherCharacter.ItemLevel));
        Assert.Equal((5004.5, 1791.66), (sameOne.CombatPower, sameOne.ItemLevel));
    }

    [Fact]
    public void The_values_survive_a_save()
    {
        string dir = Path.Combine(Path.GetTempPath(), "go-records-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RecordStore(dir);
            store.Save(Fight("나") with { CombatPower = 5004.5, ItemLevel = 1791.66 });

            var (_, back) = Assert.Single(store.List());

            Assert.Equal((5004.5, 1791.66), (back.CombatPower, back.ItemLevel));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
