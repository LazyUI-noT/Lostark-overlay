using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

// The DPS 미터기 and the 줄 수 창 read all the time but show only while a boss is read, and 10 s after (user, 2026-11-01).
public sealed class WindowLingerTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Theory]
    [InlineData(null, 5.0, false)]   // never read
    [InlineData(5.0, 5.0, true)]     // read now
    [InlineData(5.0, 15.0, true)]    // 10 s after: still up (a cutscene, a phase change)
    [InlineData(5.0, 15.1, false)]   // later: hidden
    public void A_window_shows_from_its_read_until_ten_seconds_after(double? read, double now, bool shown) =>
        Assert.Equal(shown, WindowLinger.InUse(read is { } r ? S(r) : null, S(now)));

    [Fact]
    public void A_read_now_moves_the_last_read()
    {
        Assert.Equal(S(9), WindowLinger.Seen(S(2), readNow: true, S(9)));
        Assert.Equal(S(2), WindowLinger.Seen(S(2), readNow: false, S(9)));
        Assert.Null(WindowLinger.Seen(null, readNow: false, S(9)));
    }

    [Fact]
    public void The_meter_was_last_read_at_its_later_read()
    {
        var status = MeterStatus.None with { LastRead = S(3), HpAt = S(7) };
        Assert.Equal(S(7), WindowLinger.LastRead(status));
        Assert.Null(WindowLinger.LastRead(MeterStatus.None));   // nothing read yet
        Assert.Null(WindowLinger.LastRead(null));
    }
}
