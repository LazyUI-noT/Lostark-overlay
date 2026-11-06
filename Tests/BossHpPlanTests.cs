using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>보스 체력 is its own read: 쉽게 보기 and 그림자 기믹 체력 work with the DPS 미터기 off.</summary>
public class BossHpPlanTests
{
    static RaidGate Shadow => new("x", "세르카", "1관", [], [], Raid.없음, Tab: "그림자 레이드");

    [Fact]
    public void 쉽게_보기_wants_the_read() =>
        Assert.True(BossHpPlan.Wanted(new RaidSettings { BossHpEasy = true }, []));

    [Fact]
    public void Nothing_wanting_it_leaves_it_off() =>
        Assert.False(BossHpPlan.Wanted(new RaidSettings { BossHpEasy = false }, []));

    [Fact]
    public void A_그림자_gate_with_its_switch_on_wants_the_read() =>
        Assert.True(BossHpPlan.Wanted(new RaidSettings { BossHpEasy = false }, [Shadow]));

    [Fact]
    public void A_그림자_gate_with_its_switch_off_does_not()
    {
        var s = new RaidSettings { BossHpEasy = false };
        s.ShadowHpOff.Add("세르카");
        Assert.False(BossHpPlan.Wanted(s, [Shadow]));
    }

    [Fact]
    public void The_read_switch_turns_it_off_whatever_wants_it() =>
        Assert.False(BossHpPlan.Wanted(new RaidSettings { BossHpEasy = true, BossHpRead = false }, [Shadow]));

    [Fact]
    public void No_raid_settings_no_read() => Assert.False(BossHpPlan.Wanted(null, []));

    [Fact]
    public void The_homework_fallback_wants_the_read_even_with_easy_reading_off() =>
        Assert.True(BossHpPlan.Wanted(new RaidSettings { BossHpEasy = false }, [], homework: true));

    [Fact]
    public void The_homework_fallback_gets_no_read_when_it_is_switched_off() =>
        Assert.False(BossHpPlan.Wanted(new RaidSettings { BossHpRead = false }, [], homework: true));
}
