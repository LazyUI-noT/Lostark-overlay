using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>How often the dungeon panel is read with Windows OCR: every 3 s, every 10 s once the same panel was read a few
/// times running, fast again on any other read or when the HUD comes back (a loading screen ended).</summary>
public sealed class DungeonPaceTests
{
    static readonly DungeonInfo Raid = new(DungeonKind.Raid, "칠흑, 폭풍의 밤", "싱글");

    [Fact]
    public void Starts_fast()
    {
        Assert.Equal(DungeonPace.Fast, DungeonPace.Start.Every);
    }

    [Fact]
    public void Slows_once_the_same_panel_is_read_enough_times()
    {
        var p = DungeonPace.Start;
        for (int i = 0; i < DungeonPace.SettleAfter; i++) p = p.Read(Raid);
        Assert.Equal(DungeonPace.Fast, p.Every);   // the first read is a change from nothing known
        p = p.Read(Raid);
        Assert.Equal(DungeonPace.Slow, p.Every);
    }

    [Fact]
    public void No_dungeon_read_again_and_again_also_slows()
    {
        var p = DungeonPace.Start;
        for (int i = 0; i < DungeonPace.SettleAfter; i++) p = p.Read(null);
        Assert.Equal(DungeonPace.Slow, p.Every);
    }

    [Fact]
    public void Another_read_is_fast_again()
    {
        var p = DungeonPace.Start;
        for (int i = 0; i < 10; i++) p = p.Read(null);
        Assert.Equal(DungeonPace.Fast, p.Read(Raid).Every);
    }

    [Fact]
    public void Same_panel_read_as_a_new_record_counts_as_same()
    {
        var p = DungeonPace.Start;
        for (int i = 0; i < 10; i++) p = p.Read(Raid with { });
        Assert.Equal(DungeonPace.Slow, p.Every);
    }

    [Fact]
    public void Waking_is_fast_until_settled_again()
    {
        var p = DungeonPace.Start;
        for (int i = 0; i < 10; i++) p = p.Read(Raid);
        p = p.Wake();
        Assert.Equal(DungeonPace.Fast, p.Every);
        for (int i = 0; i < DungeonPace.SettleAfter; i++) p = p.Read(Raid);
        Assert.Equal(DungeonPace.Slow, p.Every);
    }
}
