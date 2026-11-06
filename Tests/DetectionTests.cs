using System.Collections.Immutable;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>감지 중 (Phase 5): ✓ read for certain (two reads, then kept with nothing to read), ? nothing yet, ! should have
/// read under its condition but didn't, with why.</summary>
public class DetectionTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    static DetectInputs In(bool hud = true, bool fit = true, bool buff = false, bool dungeon = false, bool boss = false, bool panel = false,
        bool amsuReadable = true) =>
        new(Seen: true, HasFit: fit, HudVisible: hud, AnyBuff: buff, GaugeRead: false, InDungeon: dungeon, BossUp: boss, PanelFound: panel,
            BossHpRead: false, DungeonKnown: false, AmsuReadable: amsuReadable, AmsuFound: false, JudgmentSeen: false);

    static readonly ImmutableHashSet<string> All = ["skill", "buff", "aden", "g-jeonbun", "boss", "dungeon", "amsu", "judg"];

    [Fact]
    public void A_second_of_finding_makes_it_certain_and_it_stays_with_nothing_to_read()
    {
        var b = DetectBoard.Step(DetectBoard.Empty, In(buff: true), All, S(0));
        b = DetectBoard.Step(b, In(buff: true), All, S(0.5));   // a held read seen twice is not yet certain
        Assert.Equal(DetectMark.Waiting, b["buff"].Mark);
        b = DetectBoard.Step(b, In(buff: true), All, S(1));
        Assert.Equal(DetectMark.Concrete, b["buff"].Mark);
        b = DetectBoard.Step(b, In(buff: false), All, S(60));   // town: no buff up
        Assert.Equal(DetectMark.Concrete, b["buff"].Mark);
        Assert.Equal(S(1), b["buff"].At);
    }

    [Fact]
    public void Two_short_sightings_make_it_certain_too()
    {
        var b = DetectBoard.Step(DetectBoard.Empty, In(buff: true), All, S(0));   // 공증: up 0.3 s
        b = DetectBoard.Step(b, In(buff: true), All, S(0.3));
        b = DetectBoard.Step(b, In(buff: false), All, S(0.5));
        Assert.Equal(DetectMark.Waiting, b["buff"].Mark);
        b = DetectBoard.Step(b, In(buff: true), All, S(9));   // up again
        Assert.Equal(DetectMark.Concrete, b["buff"].Mark);
    }

    [Fact]
    public void A_red_mark_goes_back_to_waiting_once_there_is_nothing_to_read()
    {
        var b = DetectBoard.Empty;
        for (double t = 0; t <= 5; t += 1) b = DetectBoard.Step(b, In(dungeon: true, boss: true), All, S(t));
        Assert.Equal(DetectMark.Failed, b["g-jeonbun"].Mark);
        b = DetectBoard.Step(b, In(), All, S(6));   // left the dungeon
        Assert.Equal(DetectMark.Waiting, b["g-jeonbun"].Mark);
        Assert.Null(b["g-jeonbun"].Why);
    }

    [Fact]
    public void The_meter_panel_missing_in_a_boss_fight_turns_red_after_five_seconds_with_why()
    {
        var b = DetectBoard.Empty;
        for (double t = 0; t <= 4; t += 1) b = DetectBoard.Step(b, In(dungeon: true, boss: true), All, S(t));
        Assert.Equal(DetectMark.Waiting, b["g-jeonbun"].Mark);
        b = DetectBoard.Step(b, In(dungeon: true, boss: true), All, S(5));
        Assert.Equal(DetectMark.Failed, b["g-jeonbun"].Mark);
        Assert.Contains("전투 분석기", b["g-jeonbun"].Why);
        b = DetectBoard.Step(DetectBoard.Step(b, In(panel: true), All, S(6)), In(panel: true), All, S(7));
        Assert.Equal(DetectMark.Concrete, b["g-jeonbun"].Mark);
    }

    [Fact]
    public void Without_a_fit_the_skill_bar_fails_at_once()
    {
        var b = DetectBoard.Step(DetectBoard.Empty, In(hud: false, fit: false), All, S(0));
        Assert.Equal(DetectMark.Failed, b["skill"].Mark);
        Assert.Contains("스킬창 맞추기", b["skill"].Why);
    }

    [Fact]
    public void Amsu_icons_too_small_fail_at_once()
    {
        var b = DetectBoard.Step(DetectBoard.Empty, In(amsuReadable: false), All, S(0));
        Assert.Equal(DetectMark.Failed, b["amsu"].Mark);
    }

    [Fact]
    public void The_character_is_certain_after_menu_reads_name_it_for_a_second()
    {
        var named = In() with { CharacterNamed = true };
        var b = DetectBoard.Step(DetectBoard.Step(DetectBoard.Empty, named, All.Add("char"), S(0)), named, All.Add("char"), S(1));
        Assert.Equal(DetectMark.Concrete, b["char"].Mark);
        b = DetectBoard.Step(b, In(), All.Add("char"), S(30));   // menu closed
        Assert.Equal(DetectMark.Concrete, b["char"].Mark);
    }

    [Fact]
    public void A_picture_is_kept_when_a_mark_turns_certain_or_red_only()
    {
        var b0 = DetectBoard.Step(DetectBoard.Empty, In(buff: true, fit: false), All, S(0));   // skill ! at once
        Assert.Contains("skill", DetectBoard.Moments(DetectBoard.Empty, b0));
        Assert.DoesNotContain("buff", DetectBoard.Moments(DetectBoard.Empty, b0));   // still ?
        var b1 = DetectBoard.Step(b0, In(buff: true, fit: false), All, S(1));
        Assert.Equal(["buff"], DetectBoard.Moments(b0, b1));
    }

    [Fact]
    public void The_picture_margin_grows_the_area_and_stays_in_the_game()
    {
        var vp = new Viewport(0, 0, 1920, 1080);
        Assert.Equal(new PixelRect(50, 75, 300, 150), DetectBoard.Around(new PixelRect(100, 100, 200, 100), vp));
        Assert.Equal(new PixelRect(0, 0, 36, 36), DetectBoard.Around(new PixelRect(0, 0, 20, 20), vp));
        Assert.Null(DetectBoard.Around(new PixelRect(3000, 0, 20, 20), vp));
    }

    [Fact]
    public void A_reader_switched_off_has_no_mark()
    {
        var b = DetectBoard.Step(DetectBoard.Empty, In(buff: true), new HashSet<string> { "skill" }, S(0));
        Assert.False(b.ContainsKey("buff"));
    }

    [Fact]
    public void Lights_run_only_while_a_certain_reader_keeps_finding()
    {
        var b = DetectBoard.Step(DetectBoard.Step(DetectBoard.Empty, In(buff: true), All, S(0)), In(buff: true), All, S(1));
        Assert.True(DetectBoard.Flowing(b, "buff", S(2)));
        Assert.False(DetectBoard.Flowing(b, "buff", S(10)));
        Assert.False(DetectBoard.Flowing(b, "boss", S(2)));
    }
}
