using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The timeline's scales are fitted to the peaks of the seconds shown (records-timeline.design.md: zooming refits both).</summary>
public class TimelinePeaksTests
{
    static FightRecord WithSamples(params RecordSample[] samples) =>
        new(DateTime.MinValue, null, 120, [], "나", 0, null, [], samples, [], []);

    [Fact]
    public void Peaks_are_the_biggest_samples_within_a_second_of_the_seconds_shown()
    {
        var r = WithSamples(new RecordSample(10, "나", 900, 90), new RecordSample(20, "나", 500, 50), new RecordSample(29, "민수", 600, 60),
            new RecordSample(30, "나", 700, 70), new RecordSample(40, "나", 800, 80));

        Assert.Equal((60L, 600L), Timeline.Peaks(r, new TimeWindow(18, 28)));   // 17 s to 29 s: the 10 s and 30 s samples are outside
    }

    [Fact]
    public void Peaks_are_zero_when_no_sample_is_near_the_seconds_shown()
        => Assert.Equal((0L, 0L), Timeline.Peaks(WithSamples(new RecordSample(10, "나", 900, 90)), new TimeWindow(100, 110)));
}
