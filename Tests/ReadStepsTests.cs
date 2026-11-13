using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정 → 초당 읽기 횟수: the read loop's steps a second (10–30, default 20), the same on any monitor's refresh rate.</summary>
public sealed class ReadStepsTests
{
    static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Defaults_to_20_and_new_settings_start_there() =>
        Assert.Equal((20, 20), (ReadSteps.Default, new Settings().ReadsPerSecond));

    [Theory]
    [InlineData(1, 10)]
    [InlineData(20, 20)]
    [InlineData(99, 30)]
    public void Values_are_kept_between_5_and_30(int given, int kept) => Assert.Equal(kept, ReadSteps.Clamp(given));

    [Fact]
    public void A_step_is_due_once_per_interval()
    {
        Assert.True(ReadSteps.Due(TimeSpan.MinValue, 20, Ms(0)));
        Assert.False(ReadSteps.Due(Ms(0), 20, Ms(49)));
        Assert.True(ReadSteps.Due(Ms(0), 20, Ms(50)));
        Assert.True(ReadSteps.Due(Ms(0), 10, Ms(100)));
    }

    [Theory]
    [InlineData(0, 20, 1, 10)]     // 49 ms to the next step: the idle cap
    [InlineData(0, 20, 45, 5)]     // 5 ms left
    [InlineData(0, 20, 50, 1)]     // due now (the caller steps first): never less than 1 ms
    [InlineData(0, 20, 70, 1)]     // late
    public void Idle_sleeps_until_the_next_step_at_most_the_cap(double last, int perSecond, double now, double sleep) =>
        Assert.Equal(Ms(sleep), ReadSteps.Idle(Ms(last), perSecond, Ms(now)));

    [Fact]
    public void Idle_before_any_step_is_the_shortest_sleep() => Assert.Equal(Ms(1), ReadSteps.Idle(TimeSpan.MinValue, 20, Ms(0)));

    [Fact]
    public void An_out_of_range_value_paces_at_its_clamped_rate() => Assert.Equal(ReadSteps.Every(30), ReadSteps.Every(500));

    // A step lands on the first new frame after its slot; the next slot is counted from the slot, not the frame, so a
    // 144 Hz monitor (6.94 ms frames) still takes 10 steps a second at 10, spread over the second (user, 2026-11-03).
    [Theory]
    [InlineData(144, 10)]
    [InlineData(144, 20)]
    [InlineData(60, 10)]
    [InlineData(60, 30)]
    [InlineData(165, 20)]
    public void Steps_spread_evenly_and_keep_their_count_at_any_refresh_rate(int hz, int perSecond)
    {
        var last = TimeSpan.MinValue;
        var steps = new List<double>();
        for (int f = 0; f < hz * 10; f++)
        {
            var now = TimeSpan.FromSeconds(f / (double)hz);
            if (!ReadSteps.Due(last, perSecond, now)) continue;
            last = ReadSteps.Next(last, perSecond, now);
            steps.Add(now.TotalMilliseconds);
        }
        Assert.InRange(steps.Count, perSecond * 10 - 1, perSecond * 10 + 1);
        var gaps = steps.Zip(steps.Skip(1), (a, b) => b - a).ToList();
        Assert.True(gaps.Max() < 1000.0 / perSecond + 1000.0 / hz + 0.01);   // never more than one frame late
    }

    [Fact]
    public void After_a_stall_the_steps_start_again_from_now_without_a_burst()
    {
        var last = ReadSteps.Next(TimeSpan.MinValue, 10, Ms(0));
        last = ReadSteps.Next(last, 10, Ms(1000));   // the loop stalled a second (a hitch, a hidden game)
        Assert.Equal(Ms(1000), last);
        Assert.False(ReadSteps.Due(last, 10, Ms(1050)));
    }

    // A 100 ms read (buff row, debuff and 암수 timers) counts from its last frame, a little after its step's slot. Judged at
    // the slot it often just misses and slips a step (150 ms at 20 steps); judged half a step ahead it keeps 10 a second (review).
    [Theory]
    [InlineData(60, 10)]
    [InlineData(60, 20)]
    [InlineData(60, 30)]
    [InlineData(144, 10)]
    [InlineData(144, 20)]
    [InlineData(144, 30)]
    public void A_100_ms_read_keeps_10_a_second_at_any_step_rate(int hz, int perSecond)
    {
        Assert.InRange(ReadsOf100Ms(hz, perSecond, slack: true), 98, 101);
        if (hz == 144 && perSecond == 20) Assert.True(ReadsOf100Ms(hz, perSecond, slack: false) < 90);   // the slip it fixes (60 Hz frames fall on 50 ms slots)
    }

    /// <summary>10 s of a loop polling every millisecond: a step on the first new frame after its slot, the read when due by
    /// the step's (ahead) time, timestamped with that frame.</summary>
    static int ReadsOf100Ms(int hz, int perSecond, bool slack)
    {
        TimeSpan last = TimeSpan.MinValue, read = TimeSpan.MinValue, every = TimeSpan.FromMilliseconds(100);
        int reads = 0;
        for (int ms = 0; ms < 10_000; ms++)
        {
            var now = Ms(ms);
            if (!ReadSteps.Due(last, perSecond, now)) continue;
            last = ReadSteps.Next(last, perSecond, now);
            var due = slack ? now + ReadSteps.Slack(perSecond) : now;
            var frame = TimeSpan.FromSeconds(Math.Ceiling(now.TotalSeconds * hz) / hz);   // the next new frame
            if (read != TimeSpan.MinValue && due - read < every) continue;
            read = frame;
            reads++;
        }
        return reads;
    }
}
