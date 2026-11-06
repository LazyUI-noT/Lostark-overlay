using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The map's switches: each box part reads and sets the setting behind it; a part with none yet reads null.</summary>
public class FeatureMapSwitchTests
{
    static MapBox Box(string id) => FeatureMap.Boxes.Single(b => b.Id == id);

    [Fact]
    public void Every_wired_choice_is_one_the_box_offers()
    {
        var s = new Settings();
        foreach (var b in FeatureMap.Boxes)
            for (int part = 0; part < b.Choices.Length; part++)
                if (FeatureMap.Selected(s, b.Id, part) is { } i) Assert.InRange(i, 0, b.Choices[part].Length - 1);
    }

    [Fact]
    public void Choosing_sets_the_setting_and_reads_back()
    {
        var s = new Settings();
        foreach (var (id, part) in new[] { ("skill", 0), ("cls", 0), ("meter", 0), ("count", 0), ("peers", 0), ("boss", 0), ("boss", 1), ("amsu", 0) })
            for (int i = 0; i < Box(id).Choices[part].Length; i++)
            {
                FeatureMap.Choose(s, id, part, i);
                Assert.Equal(i, FeatureMap.Selected(s, id, part));
            }
    }

    [Fact]
    public void Turning_the_timeline_on_turns_the_meter_on_without_its_window()
    {
        var s = new Settings();
        Assert.Equal(ThreeWay.끄기, s.Meter.Mode());
        var note = FeatureMap.Choose(s, "rec", 0, 1);
        Assert.Equal(ThreeWay.표시안함, s.Meter.Mode());
        Assert.True(s.Meter.Record);
        Assert.NotNull(note);
    }

    [Fact]
    public void A_part_without_a_setting_yet_reads_null_and_choosing_it_changes_nothing()
    {
        var s = new Settings();
        Assert.Null(FeatureMap.Selected(s, "dungeon", 0));
        Assert.Null(FeatureMap.Choose(s, "dungeon", 0, 2));
    }

    [Fact]
    public void Every_box_opens_a_page_of_the_rail()
    {
        var areas = FeatureMap.Rail.SelectMany(g => g.Areas).ToHashSet();
        Assert.All(FeatureMap.Boxes, b => Assert.Contains(FeatureMap.Page(b.Id), areas));
    }
}
