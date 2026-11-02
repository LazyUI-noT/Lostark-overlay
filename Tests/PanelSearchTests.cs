using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>When the whole screen is searched for the 전분 panel: no spot or a spot that stopped reading, and only while a
/// boss HP bar was read lately (the panel shows in a boss fight, so 지옥 and the open world don't search forever).</summary>
public sealed class PanelSearchTests
{
    static readonly TimeSpan Now = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(false, 0, true)]                         // no spot yet
    [InlineData(true, PanelSearch.AfterMisses, true)]    // a spot that stopped reading
    [InlineData(true, PanelSearch.AfterMisses - 1, false)]
    public void Searches_without_a_spot_that_reads(bool hasSpot, int misses, bool due)
    {
        Assert.Equal(due, PanelSearch.Due(hasSpot, misses, bossSeen: Now - TimeSpan.FromSeconds(1), inDungeon: false, Now));
    }

    [Fact]
    public void A_first_find_searches_at_once()
    {
        // no spot yet: search from the first second, or the fight's first seconds are lost (1막: 5 s)
        Assert.True(PanelSearch.Due(false, 0, bossSeen: null, inDungeon: false, Now));
    }

    [Fact]
    public void A_spot_that_stopped_reading_waits_for_a_boss_bar_or_a_dungeon()
    {
        // 지옥 and the open world: the saved spot shows no panel, and searching every 3 s found nothing
        int m = PanelSearch.AfterMisses;
        Assert.False(PanelSearch.Due(true, m, bossSeen: null, inDungeon: false, Now));
        Assert.False(PanelSearch.Due(true, m, bossSeen: Now - PanelSearch.BossRecent - TimeSpan.FromSeconds(1), inDungeon: false, Now));
        Assert.True(PanelSearch.Due(true, m, bossSeen: Now - PanelSearch.BossRecent, inDungeon: false, Now));
        Assert.True(PanelSearch.Due(true, m, bossSeen: null, inDungeon: true, Now));
    }

    [Fact]
    public void A_spot_that_reads_is_not_searched()
    {
        Assert.False(PanelSearch.Due(true, 0, bossSeen: Now, inDungeon: true, Now));
    }
}
