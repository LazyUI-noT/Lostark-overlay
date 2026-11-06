using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정's rail: groups in the 표시 map's colours, in the user's order.</summary>
public class FeatureMapTests
{
    static int Group(string name) => FeatureMap.Rail.ToList().FindIndex(g => g.Name == name);

    [Fact]
    public void Battle_records_stand_right_above_character_and_skills() =>
        Assert.Equal(Group("캐릭터 · 스킬") - 1, Group("전투기록"));

    [Fact]
    public void Battle_records_hold_the_timeline() =>
        Assert.Equal(["타임라인"], FeatureMap.Rail[Group("전투기록")].Areas);

    [Fact]
    public void The_meter_has_its_own_tab_apart_from_the_records() =>
        Assert.Equal(["DPS 미터기"], FeatureMap.Rail[Group("DPS 미터기")].Areas);

    [Fact]
    public void No_area_is_listed_twice()
    {
        var areas = FeatureMap.Rail.SelectMany(g => g.Areas).ToList();
        Assert.Equal(areas.Count, areas.Distinct().Count());
    }

    [Fact]
    public void Colours_are_hex_or_neutral() =>
        Assert.All(FeatureMap.Rail, g => Assert.Matches("^(#[0-9A-Fa-f]{6})?$", g.Color));
}
