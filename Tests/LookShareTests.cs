using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The 모든 위치 프로필에 적용 mark's line under each look group (display-split.plan.md, step 4).</summary>
public class LookShareTests
{
    [Fact]
    public void A_shared_look_says_every_profile_uses_it_and_names_what_it_covers()
    {
        var line = LookShare.Status(LookGroup.버프막대, shared: true, activeProfile: 2);

        Assert.StartsWith("위치 프로필 1–5가 같은 값을 씁니다", line);
        Assert.Contains("움직임", line);
    }

    [Fact]
    public void An_own_look_names_the_active_profile()
    {
        var line = LookShare.Status(LookGroup.암수, shared: false, activeProfile: 2);

        Assert.StartsWith("위치 프로필 3의 값입니다", line);
    }

    [Fact]
    public void Every_look_group_names_what_it_covers()
    {
        Assert.All(Enum.GetValues<LookGroup>(), g => Assert.False(string.IsNullOrWhiteSpace(LookShare.Covers(g))));
    }
}
