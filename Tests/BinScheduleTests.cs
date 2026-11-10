using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>휴지통 비우기 (user, 2026-11-10): every week at the chosen day and hour, Wednesday 08:00 by default.</summary>
public sealed class BinScheduleTests
{
    static readonly DateTime Wed8 = new(2026, 11, 11, 8, 0, 0);   // a Wednesday

    [Theory]
    [InlineData(2026, 11, 11, 8, 0, 0)]    // on the hour: this one
    [InlineData(2026, 11, 11, 9, 30, 0)]
    [InlineData(2026, 11, 17, 23, 0, 0)]   // Tuesday night: still last Wednesday's
    public void The_last_emptying_is_the_latest_chosen_hour_not_after_now(int y, int mo, int d, int h, int mi, int s)
    {
        Assert.Equal(Wed8, BinSchedule.Last(new DateTime(y, mo, d, h, mi, s), DayOfWeek.Wednesday, 8));
    }

    [Fact]
    public void A_minute_before_the_hour_is_the_week_before()
    {
        Assert.Equal(Wed8.AddDays(-7), BinSchedule.Last(Wed8.AddMinutes(-1), DayOfWeek.Wednesday, 8));
    }

    [Fact]
    public void Due_once_per_emptying_time_and_never_for_one_before_the_last_run()
    {
        Assert.Equal(Wed8, BinSchedule.Due(Wed8.AddHours(1), DayOfWeek.Wednesday, 8, emptiedAt: Wed8.AddDays(-2)));
        Assert.Null(BinSchedule.Due(Wed8.AddHours(2), DayOfWeek.Wednesday, 8, emptiedAt: Wed8.AddHours(1)));   // done this week
        Assert.Null(BinSchedule.Due(Wed8.AddHours(1), DayOfWeek.Wednesday, 7, emptiedAt: Wed8.AddMinutes(30)));   // moved earlier after the run: next week
        Assert.Equal(Wed8, BinSchedule.Due(Wed8, DayOfWeek.Wednesday, 8, emptiedAt: DateTime.MinValue));   // never ran
    }

    [Fact]
    public void Another_day_and_hour_and_the_next_one()
    {
        Assert.Equal(new DateTime(2026, 11, 8, 23, 0, 0), BinSchedule.Last(Wed8, DayOfWeek.Sunday, 23));
        Assert.Equal(Wed8.AddDays(7), BinSchedule.Next(Wed8, DayOfWeek.Wednesday, 8));
        Assert.Equal(new DateTime(2026, 11, 4, 23, 0, 0), BinSchedule.Last(Wed8, DayOfWeek.Wednesday, 99));   // the hour clamped to 23
    }
}
