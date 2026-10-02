using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>A greyed-out bar (a town) tells its 직업각인 by its 일반 skills: each is looked for in every 직업각인's pool of icons
/// submitted in colour (스킬 등록), wherever it sits on the bar. greyed out.png, No cd.png and other_* are the same 워로드;
/// lt2_clear_* is another character.</summary>
public class SkillPoolTests
{
    static List<Image> Slots(string set, params int[] n) => [.. n.Select(i => Fixture.Load($"{set}_일반{i}").Frames[0])];
    static readonly int[] All = [1, 2, 3, 4, 5, 6, 7, 8];
    static readonly List<Image> Greyed = Slots("greyed", All);

    [Fact]
    public void A_greyed_bar_picks_the_직업각인_whose_pool_holds_5_of_its_skills()
    {
        var pools = new Dictionary<string, IReadOnlyList<Image>> { ["고독한기사"] = Slots("Nocd", All), ["전투태세"] = Slots("lt2_clear", All) };
        Assert.Equal("고독한기사", SkillPool.Pick(Greyed, pools));
    }

    [Fact]
    public void The_position_on_the_bar_doesnt_matter()
    {
        var pools = new Dictionary<string, IReadOnlyList<Image>> { ["고독한기사"] = Slots("Nocd", 8, 7, 5, 4, 3, 2, 1), ["전투태세"] = Slots("lt2_clear", All) };
        Assert.Equal("고독한기사", SkillPool.Pick([.. Greyed.AsEnumerable().Reverse()], pools));
    }

    [Fact]
    public void Fewer_than_5_matches_pick_nothing()
    {
        var pools = new Dictionary<string, IReadOnlyList<Image>> { ["고독한기사"] = Slots("Nocd", 1, 2, 3, 4), ["전투태세"] = Slots("lt2_clear", All) };
        Assert.Null(SkillPool.Pick(Greyed, pools));
        Assert.Null(SkillPool.Pick(Greyed, new Dictionary<string, IReadOnlyList<Image>>()));
    }

    /// <summary>With one 직업각인 submitted, its pool alone decides (5 of 8); the other is never picked from the pools.</summary>
    [Fact]
    public void One_submitted_직업각인_is_picked_by_its_pool_alone()
    {
        var pools = new Dictionary<string, IReadOnlyList<Image>> { ["고독한기사"] = Slots("Nocd", All), ["전투태세"] = [] };
        Assert.Equal("고독한기사", SkillPool.Pick(Greyed, pools));
    }

    [Fact]
    public void A_bar_of_the_unsubmitted_직업각인_picks_nothing()
    {
        var pools = new Dictionary<string, IReadOnlyList<Image>> { ["고독한기사"] = [], ["전투태세"] = Slots("lt2_clear", All) };
        Assert.Null(SkillPool.Pick(Greyed, pools));
    }

    /// <summary>A skill both 직업각인 have counts for neither, and the rest decide by 5 of 8 of them (3 at least): here 일반1–4 are
    /// in both, and 고독한기사's own 일반5, 7 and 8 (일반6 was changed between the two screenshots) are 3 of the 4 left.</summary>
    [Fact]
    public void Skills_in_both_pools_are_left_out_and_the_rest_decide()
    {
        var pools = new Dictionary<string, IReadOnlyList<Image>>
        {
            ["고독한기사"] = Slots("Nocd", All),
            ["전투태세"] = [.. Slots("Nocd", 1, 2, 3, 4), .. Slots("lt2_clear", All)],
        };
        Assert.Equal("고독한기사", SkillPool.Pick(Greyed, pools));
    }

    [Fact]
    public void Too_few_own_skills_left_pick_nothing()
    {
        var pools = new Dictionary<string, IReadOnlyList<Image>>
        {
            ["고독한기사"] = Slots("Nocd", All),
            ["전투태세"] = [.. Slots("Nocd", 1, 2, 3, 4, 5), .. Slots("lt2_clear", All)],
        };
        Assert.Null(SkillPool.Pick(Greyed, pools));   // 일반7 and 8 alone: under 3
    }

    [Fact]
    public void Submitting_adds_only_icons_not_in_the_pool_yet()
    {
        Assert.Equal(4, SkillPool.Fresh(Slots("Nocd", 1, 2, 3, 4), Slots("nomana", All)).Count(i => i));
        Assert.Equal([true, false], SkillPool.Fresh([], Slots("Nocd", 1, 1)));   // twice in one submit: once
    }
}
