using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>The DPS graph's 그래프 범위 and its time lines.</summary>
public class MeterRangeTests
{
    static IReadOnlyList<(TimeSpan At, double? Share, long Dps)> Points(params int[] seconds) =>
        [.. seconds.Select(s => (TimeSpan.FromSeconds(s), (double?)null, 1L))];

    [Fact]
    public void The_whole_fight_keeps_every_point()
    {
        var all = Points(1, 100, 200);
        Assert.Same(all, MeterHistory.Shown(all, MeterRange.전체, TimeSpan.FromSeconds(200)));
    }

    [Fact]
    public void The_last_minute_pushes_older_points_out()
    {
        var shown = MeterHistory.Shown(Points(1, 139, 140, 200), MeterRange.최근1분, TimeSpan.FromSeconds(200));
        Assert.Equal([140, 200], shown.Select(p => (int)p.At.TotalSeconds));
    }

    [Fact]
    public void Time_lines_fall_on_round_fight_seconds_inside_the_window()
    {
        Assert.Equal([150.0, 165, 180, 195], GridScale.Ticks(140, 60, 15));
        Assert.Equal([15.0, 30, 45], GridScale.Ticks(0, 60, 15));
    }
}
