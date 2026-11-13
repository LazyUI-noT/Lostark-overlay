using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>What 복사 says about each look group (user, 2026-11-13).</summary>
public class LookShareTests
{
    [Fact]
    public void Every_look_group_names_what_it_covers()
    {
        Assert.All(Enum.GetValues<LookGroup>(), g => Assert.False(string.IsNullOrWhiteSpace(LookShare.Covers(g))));
    }

    [Fact]
    public void Every_look_group_has_its_own_name()
    {
        var names = Enum.GetValues<LookGroup>().Select(LookShare.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
