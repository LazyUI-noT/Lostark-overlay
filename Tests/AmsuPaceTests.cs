using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정 → 암흑수류탄 → 읽기 빈도 (user, 2026-11-03: frame drops at 1440p / 144 Hz with 암수 on). 높음 reads as before;
/// 낮음 looks for a bomb once a second, locks a found bomb's time for a second, then checks it once a second until 3 s are left,
/// where it reads closely again (runs out, re-thrown, or the other kind). Untimed layouts always read 높음.</summary>
public sealed class AmsuPaceTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    static readonly (TimeSpan, TimeSpan) High = (RowCadence.FrameEvery, RowCadence.ClassifyEvery);

    [Fact]
    public void High_reads_as_before() =>
        Assert.Equal(High, AmsuPace.Of(AmsuRate.높음, timed: true, bombs: 1, left: 15, sinceFound: S(5)));

    [Fact]
    public void Low_looks_for_a_bomb_once_a_second() =>
        Assert.Equal(AmsuPace.Slow, AmsuPace.Of(AmsuRate.낮음, true, bombs: 0, left: null, sinceFound: TimeSpan.Zero).Classify);

    [Fact]
    public void Low_locks_a_found_bomb_closely_for_a_second() =>
        Assert.Equal(High, AmsuPace.Of(AmsuRate.낮음, true, 1, left: 18, sinceFound: S(0.5)));

    [Fact]
    public void Low_checks_a_locked_bomb_once_a_second()
    {
        var (frames, classify) = AmsuPace.Of(AmsuRate.낮음, true, 1, left: 12, sinceFound: S(3));
        Assert.Equal(AmsuPace.Slow, frames);     // the bomb's slot and timer: a re-throw shows within a second
        Assert.Equal(AmsuPace.Slow, classify);   // icons and the boss's HP text too: a moved slot or another boss within a second
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(1.0)]
    public void Low_reads_closely_in_the_last_three_seconds(double left) =>
        Assert.Equal(High, AmsuPace.Of(AmsuRate.낮음, true, 1, left, sinceFound: S(10)));

    [Fact]
    public void Low_reads_closely_while_the_time_left_is_unknown() =>
        Assert.Equal(High, AmsuPace.Of(AmsuRate.낮음, true, 1, left: null, sinceFound: S(10)));

    // No false disappearing (user's rule 3): a bomb ends only when missing 1.5 s from a visible row; at one read a second that
    // would rest on two reads, so a read that missed it returns to the close cadence until it is seen again or ends.
    [Fact]
    public void Low_reads_closely_after_a_read_that_missed_a_bomb() =>
        Assert.Equal(High, AmsuPace.Of(AmsuRate.낮음, true, 1, left: 12, sinceFound: S(5), missed: true));

    // A re-throw within ~1 s (user): a jump up in the timer waits for a second agreeing read (Amsu.Seen); read closely meanwhile.
    [Fact]
    public void Low_reads_closely_while_a_timer_jump_waits_for_its_second_read() =>
        Assert.Equal(High, AmsuPace.Of(AmsuRate.낮음, true, 1, left: 8, sinceFound: S(5), pending: true));

    [Fact]
    public void Low_reads_closely_in_the_last_three_seconds_even_with_the_row_hidden() =>
        Assert.Equal(High, AmsuPace.Of(AmsuRate.낮음, true, bombs: 0, left: 2, sinceFound: TimeSpan.Zero));

    [Fact]
    public void Untimed_layouts_always_read_high() =>
        Assert.Equal(High, AmsuPace.Of(AmsuRate.낮음, timed: false, bombs: 0, left: null, sinceFound: TimeSpan.Zero));
}
