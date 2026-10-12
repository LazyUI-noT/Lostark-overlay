using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>While 설정 is open and another 직업각인 than the character's is picked in 편집 중, my window shows that
/// 직업각인's sample layout (<see cref="PreviewTarget"/>); the active one's entry or 기본 keeps the live data.</summary>
public class PreviewTargetTests
{
    static readonly IReadOnlyList<ClassInfo> Shipped = ProfileLoader.LoadClasses(Path.Combine(Helpers.RepoRoot(), "Profiles"));

    const string Warlord = "워로드", Combat = "워로드 · 전투태세", Lone = "워로드 · 고독한기사";

    [Fact]
    public void The_active_characters_own_entry_has_no_target()
    {
        Assert.Null(PreviewTarget.Decide(Combat, Combat, Shipped));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Combat)]
    public void 기본_has_no_target(string? active)
    {
        Assert.Null(PreviewTarget.Decide(ClassProfiles.Template, active, Shipped));
    }

    [Fact]
    public void Another_직업각인_is_the_target_with_its_class_slots_and_role()
    {
        var target = PreviewTarget.Decide(Lone, Combat, Shipped)!;
        var warlord = Shipped.Single(c => c.Name == Warlord);
        Assert.Equal((Lone, Warlord, "고독한기사", warlord.RoleOf("고독한기사")), (target.Key, target.Class, target.Subclass, target.Role));
        Assert.Equal(warlord.AdenSlotsOf("고독한기사"), target.AdenSlots);
    }

    [Fact]
    public void The_target_carries_its_class_number_for_the_gauge_rule()
    {
        var target = PreviewTarget.Decide(Lone, Combat, Shipped)!;
        Assert.Equal(Shipped.Single(c => c.Name == Warlord).Id, target.ClassId);
        Assert.NotEqual(0, target.ClassId);
    }

    [Fact]
    public void With_no_active_character_any_listed_직업각인_is_a_target()
    {
        Assert.Equal(Combat, PreviewTarget.Decide(Combat, null, Shipped)!.Key);
    }

    [Fact]
    public void A_직업각인_that_sets_its_own_아덴_slots_or_role_gives_those()
    {
        var own = new Dictionary<string, SubclassInfo?> { ["서폿각인"] = new(Role.서포터, [SlotId.아덴2], DarkCue.None) };
        var cls = new ClassInfo("직업군", "가", [SlotId.아덴1, SlotId.아덴2], new Dictionary<string, GaugeRule?> { ["서폿각인"] = null, ["딜각인"] = null }, Own: own);
        var support = PreviewTarget.Decide("가 · 서폿각인", null, [cls])!;
        Assert.Equal(Role.서포터, support.Role);
        Assert.Equal([SlotId.아덴2], support.AdenSlots);
        var dealer = PreviewTarget.Decide("가 · 딜각인", null, [cls])!;
        Assert.Equal(Role.딜러, dealer.Role);
        Assert.Equal([SlotId.아덴1, SlotId.아덴2], dealer.AdenSlots);
    }

    [Theory]
    [InlineData(Warlord)]
    [InlineData("없는클래스 · 어떤각인")]
    [InlineData("워로드 · 없는각인")]
    [InlineData("")]
    public void A_class_alone_or_unknown_key_has_no_target(string picked)
    {
        Assert.Null(PreviewTarget.Decide(picked, Combat, Shipped));
    }

    // ── the no-icons note ────────────────────────────────────────────────────────────

    static readonly Dictionary<SlotId, Image> NoIcons = [];
    static readonly Dictionary<SlotId, Image> OneIcon = new() { [SlotId.일반1] = new Image(2, 2) };

    [Fact]
    public void Icons_on_but_none_saved_is_missing()
    {
        var mine = new MineProfile { Icons = [SlotGroup.일반스킬] };
        Assert.True(PreviewTarget.IconsMissing(mine, NoIcons));
        Assert.False(PreviewTarget.IconsMissing(mine, OneIcon));
    }

    [Fact]
    public void Icons_off_needs_no_note()
    {
        Assert.False(PreviewTarget.IconsMissing(new MineProfile(), NoIcons));
    }
}
