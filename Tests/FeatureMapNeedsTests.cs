using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>What a switched-off 기능 맵 box needs, for its hover card (metro-map.plan.md step 4).</summary>
public class FeatureMapNeedsTests
{
    static string[][] Rows(MapNeedSection s) => [.. s.Rows.Select(r => r.Alternatives.ToArray())];

    [Fact]
    public void Homework_needs_names_content_and_a_key_then_the_timeline_or_the_boss_bar()
    {
        var needs = FeatureMap.Needs(new Settings(), "hw", hasKey: true);

        Assert.Equal([MapNeedRole.Common, MapNeedRole.Main, MapNeedRole.Fallback], needs.Select(n => n.Role));
        Assert.Equal([["char"], ["dungeon"], ["api", "#oldinstall"]], Rows(needs[0]));
        Assert.Equal([["boss"], ["shadow"]], Rows(needs[2]));
        Assert.Equal([false, true], needs[2].Rows.Select(r => r.Optional));
    }

    [Fact]
    public void An_off_timeline_under_homework_brings_the_ingame_meter_one_step_down()
    {
        var needs = FeatureMap.Needs(new Settings(), "hw", hasKey: true);   // a new install: 인게임 전분 is off

        var main = needs.Single(n => n.Role == MapNeedRole.Main);
        Assert.Equal([["rec"], ["g-jeonbun"]], Rows(main));
        Assert.Equal([0, 1], main.Rows.Select(r => r.Depth));
        Assert.False(main.Met);
    }

    [Fact]
    public void An_on_timeline_brings_nothing_under_it_and_meets_the_main_way()
    {
        var s = new Settings();
        s.Meter.On = true;

        var main = FeatureMap.Needs(s, "hw", hasKey: true).Single(n => n.Role == MapNeedRole.Main);

        Assert.Equal([["rec"]], Rows(main));
        Assert.True(main.Met);
    }

    [Fact]
    public void Homework_s_key_row_is_met_by_a_key_or_an_install_from_before_the_gate()
    {
        var s = new Settings();
        Assert.False(FeatureMap.Needs(s, "hw", hasKey: false)[0].Met);
        Assert.True(FeatureMap.Needs(s, "hw", hasKey: true)[0].Met);

        s.HomeworkWithoutKey = true;
        Assert.True(FeatureMap.Needs(s, "hw", hasKey: false)[0].Met);
    }

    [Fact]
    public void Character_names_need_one_of_a_key_a_registered_character_or_fight_names()
    {
        var needs = FeatureMap.Needs(new Settings(), "char", hasKey: false);

        var common = Assert.Single(needs);
        Assert.Equal(MapNeedRole.Common, common.Role);
        Assert.Equal([["api", "#registered", "#fightnames"]], Rows(common));
        Assert.True(common.Met);   // fight names work without a key
    }

    [Fact]
    public void A_registered_character_counts_only_when_the_list_has_one()
    {
        var s = new Settings();
        Assert.False(FeatureMap.Satisfied(s, "#registered", hasKey: false));

        s.Homework.Characters.Add(new HomeworkCharacter { Name = "살살" });
        Assert.True(FeatureMap.Satisfied(s, "#registered", hasKey: false));
    }

    [Fact]
    public void The_timeline_needs_the_ingame_meter_and_takes_skills_content_and_the_key_as_extras()
    {
        var needs = FeatureMap.Needs(new Settings(), "rec", hasKey: false);

        Assert.Equal([MapNeedRole.Common, MapNeedRole.Optional], needs.Select(n => n.Role));
        Assert.Equal([["g-jeonbun"]], Rows(needs[0]));
        Assert.Equal([["skill"], ["dungeon"], ["api"]], Rows(needs[1]));
        Assert.True(needs[1].Met);   // extras never block
    }

    [Fact]
    public void A_box_without_a_table_needs_its_solid_arrows_and_takes_its_dashed_ones()
    {
        var count = FeatureMap.Needs(new Settings(), "count", hasKey: false);
        Assert.Equal([["boss"]], Rows(Assert.Single(count)));

        var meter = FeatureMap.Needs(new Settings(), "meter", hasKey: false);   // 인게임 전분 is column 1, so it counts; party lines don't
        Assert.Equal([MapNeedRole.Common, MapNeedRole.Optional], meter.Select(n => n.Role));
        Assert.Equal([["g-jeonbun"]], Rows(meter[0]));
        Assert.Equal([["skill"]], Rows(meter[1]));
    }

    [Fact]
    public void Game_settings_and_boxes_inside_are_not_needs()
    {
        Assert.Empty(FeatureMap.Needs(new Settings(), "skill", hasKey: false));   // only 게임 설정 feeds it
        var needs = FeatureMap.Needs(new Settings(), "meter", hasKey: false).SelectMany(Rows).SelectMany(a => a);
        Assert.DoesNotContain(needs, a => a == "cover" || FeatureMap.Boxes.Single(b => b.Id == a).Column == 0);
    }

    [Fact]
    public void Expansion_stops_two_steps_down_and_lists_each_box_once()
    {
        var s = new Settings();
        s.Raids.BossHpRead = false;

        foreach (var box in FeatureMap.Boxes)
            foreach (var section in FeatureMap.Needs(s, box.Id, hasKey: false))
            {
                Assert.All(section.Rows, r => Assert.InRange(r.Depth, 0, 2));
                var expanded = section.Rows.Where(r => r.Depth > 0).Select(r => r.Alternatives[0]).ToList();
                Assert.Equal(expanded.Count, expanded.Distinct().Count());
                Assert.DoesNotContain(box.Id, expanded);
            }
    }

    [Fact]
    public void Off_follows_the_key_the_homework_gate_and_the_box_switches()
    {
        var s = new Settings();
        Assert.False(FeatureMap.Satisfied(s, "api", hasKey: false));
        Assert.True(FeatureMap.Satisfied(s, "api", hasKey: true));
        Assert.False(FeatureMap.Satisfied(s, "hw", hasKey: false));
        Assert.True(FeatureMap.Satisfied(s, "dungeon", hasKey: false));   // no switch yet: never off
        Assert.False(FeatureMap.Satisfied(s, "g-jeonbun", hasKey: false));
        Assert.True(FeatureMap.Satisfied(s, "#fightnames", hasKey: false));
    }
}
