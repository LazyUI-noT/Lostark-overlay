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

    // 대난투's number (user, 2026-11-09): read whenever 대난투 is possible and something needs it, so the record and 숙제 tell it
    // from a gone bar; the raid's 기믹 체력 switch hides only its display.
    static RaidSettings Off()
    {
        var s = new RaidSettings { BossHpEasy = false };
        s.ShadowHpOff.Add("세르카");
        return s;
    }

    [Fact]
    public void Daenantu_s_number_is_read_with_its_switch_off_while_the_meter_runs()
    {
        Assert.Equal(Shadow, ShadowRead.Gate(Off(), Shadow, [Shadow], meterOn: true, homework: false));
        Assert.False(ShadowRead.Shown(Off(), Shadow));
    }

    [Fact]
    public void Daenantu_s_number_is_read_with_its_switch_off_for_the_homework_fallback() =>
        Assert.Equal(Shadow, ShadowRead.Gate(Off(), Shadow, [Shadow], meterOn: false, homework: true));

    [Fact]
    public void Daenantu_s_number_is_not_read_when_nothing_needs_it() =>
        Assert.Null(ShadowRead.Gate(Off(), Shadow, [Shadow], meterOn: false, homework: false));

    [Fact]
    public void Daenantu_s_hp_shows_with_its_switch_on()
    {
        var s = new RaidSettings { BossHpEasy = false };
        Assert.Equal(Shadow, ShadowRead.Gate(s, Shadow, [Shadow], meterOn: false, homework: false));
        Assert.True(ShadowRead.Shown(s, Shadow));
    }

    [Fact]
    public void A_gate_without_daenantu_reads_no_number()
    {
        var plain = Shadow with { Tab = "카제로스 레이드" };
        Assert.Null(ShadowRead.Gate(new RaidSettings(), plain, [plain], meterOn: true, homework: true));
        Assert.Null(ShadowRead.Gate(new RaidSettings(), null, [Shadow], meterOn: true, homework: true));
    }
}
