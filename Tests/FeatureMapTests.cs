using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정's rail: groups in the 표시 map's colours, in the user's order (settings-reorg.plan.md, orders 0, 3–6, 13).</summary>
public class FeatureMapTests
{
    static int Group(string name) => FeatureMap.Rail.ToList().FindIndex(g => g.Name == name);

    [Fact]
    public void Favourites_stand_alone_at_the_very_top_then_the_map()
    {
        Assert.Equal(["즐겨찾기"], FeatureMap.Rail[0].Areas);
        Assert.Equal(["기능 맵"], FeatureMap.Rail[1].Areas);
    }

    [Fact]
    public void The_battle_overlay_group_holds_the_six_parts_in_order() =>
        Assert.Equal(["직업 인식", "스킬창", "버프 줄", "단심", "암수", "DPS 미터기"], FeatureMap.Rail[Group("전투 오버레이")].Areas);

    [Fact]
    public void The_raid_is_a_group_of_one_between_the_overlay_and_the_records()
    {
        Assert.Equal(["레이드"], FeatureMap.Rail[Group("레이드")].Areas);
        Assert.Equal(Group("전투 오버레이") + 1, Group("레이드"));
        Assert.Equal(Group("레이드") + 1, Group("전투기록"));
    }

    [Fact]
    public void The_records_group_holds_the_timeline_and_the_homework_tab_beside_it() =>
        Assert.Equal(["타임라인", "레이드 숙제"], FeatureMap.Rail[Group("전투기록")].Areas);

    [Fact]
    public void The_raid_tab_has_its_two_line_label() =>
        Assert.Equal("(HP 간소화, 줄수, 대난투 체력)", FeatureMap.RailSubtitles["레이드"]);

    [Fact]
    public void Settings_saved_under_the_old_names_still_find_their_rows()
    {
        Assert.Equal("직업 인식/직업 인식/스킬 칸/x", FeatureMap.MigrateSettingId("캐릭터/캐릭터/스킬 칸/x"));
        Assert.Equal("기능 맵/기능 맵/오버레이 표시/y", FeatureMap.MigrateSettingId("표시/표시/오버레이 표시/y"));
        Assert.Equal("기능 맵/기능 맵/a", FeatureMap.MigrateSettingId("기능 Map/기능 Map/a"));
        Assert.Equal("표시할 화면/b/c", FeatureMap.MigrateSettingId("화면 선택 및 출력/b/c"));
        Assert.Equal("레이드 숙제/d/e", FeatureMap.MigrateSettingId("원정대 (레이드 선택)/d/e"));
        Assert.Equal("파티 공유/f/g", FeatureMap.MigrateSettingId("파티/f/g"));
        Assert.Equal("파티 공유/f/g", FeatureMap.MigrateSettingId("파티 공유/f/g"));   // today's name stays
    }

    [Fact]
    public void Favourites_saved_under_the_old_names_follow_their_rows_on_load()
    {
        var s = new Settings { FavoriteSettings = ["캐릭터/캐릭터/스킬 칸/x", "표시/표시/오버레이 표시", "화면·인식/감지/y"] };
        Assert.Equal(["직업 인식/직업 인식/스킬 칸/x", "기능 맵/기능 맵/오버레이 표시", "화면·인식/감지/y"], SettingsStore.Normalize(s).FavoriteSettings);
    }

    [Fact]
    public void Ids_of_areas_that_kept_their_names_are_left_alone() =>
        Assert.Equal("화면·인식/감지/x", FeatureMap.MigrateSettingId("화면·인식/감지/x"));

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
