using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>테스트 모드 reads the video's own 버프 크기 (the recording's game settings, not the user's): on buff_80_to_110 the size
/// stepped 80 → 90 → 100 → 110 (BuffSizeTests), so each labelled second must give its own size.</summary>
public class BuffSizeFinderTests
{
    static int Buffs(string label) => label.Count(char.IsUpper);

    public static TheoryData<int> SecondsWithTwoBuffs()
    {
        var data = new TheoryData<int>();
        for (int s = 0; s < BuffSizeTests.Labels.Length; s++)
            if (Buffs(BuffSizeTests.Labels[s]) >= 2) data.Add(s);
        return data;
    }

    [Theory]
    [MemberData(nameof(SecondsWithTwoBuffs))]
    public void Two_or_more_buffs_on_screen_give_the_size_they_are_drawn_at(int second)
    {
        var (fixture, vp, fit, _, rect) = BuffSizeTests.B80;
        var frame = Fixture.Load(fixture).Frames[2 * second];
        Assert.Equal(BuffSizeTests.SizeAt(second), BuffSizeFinder.Find(frame, rect, vp, fit)?.Size);
    }

    [Fact]
    public void No_buff_on_screen_gives_no_size()
    {
        var (fixture, vp, fit, _, rect) = BuffSizeTests.B80;
        Assert.Null(BuffSizeFinder.Find(Fixture.Load(fixture).Frames[0], rect, vp, fit));   // 0 s: "-----"
    }

    [Fact]
    public void The_area_to_capture_holds_every_size_s_row()
    {
        var (_, vp, fit, _, _) = BuffSizeTests.B80;
        var area = BuffSizeFinder.Area(vp, fit);
        foreach (int size in Settings.BuffSizes)
            foreach (var b in Geometry.BuffBoxes(vp, fit, size))
                Assert.True(b.Icon.X >= area.X && b.Icon.Y >= area.Y && b.Icon.Right <= area.Right && b.Icon.Bottom <= area.Bottom, $"{size}: {b.Icon}");
    }
}
