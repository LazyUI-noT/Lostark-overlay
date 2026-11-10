namespace GaugeOverlay.Core.Domain;

/// <summary>휴지통 비우기 (user, 2026-11-10): the bin empties every week at a chosen day and hour (Wednesday 08:00 by default),
/// by the PC's clock. Whatever entered before the last such time is erased, so a PC that was off then catches up at start.</summary>
public static class BinSchedule
{
    public const DayOfWeek DefaultDay = DayOfWeek.Wednesday;
    public const int DefaultHour = 8;

    /// <summary>The latest <paramref name="day"/> at <paramref name="hour"/>:00 (clamped to 0–23) not after <paramref name="now"/>.
    /// Also the 숙제 week's start (<see cref="Homework.WeekStart"/>).</summary>
    public static DateTime Last(DateTime now, DayOfWeek day, int hour)
    {
        var at = now.Date.AddDays(-(((int)now.DayOfWeek - (int)day + 7) % 7)).AddHours(Math.Clamp(hour, 0, 23));
        return at > now ? at.AddDays(-7) : at;
    }

    /// <summary>The emptying time to run now, or null: the last one is after <paramref name="emptiedAt"/> (the last run, or the
    /// moment the day or hour was changed, so moving the time back never empties at once).</summary>
    public static DateTime? Due(DateTime now, DayOfWeek day, int hour, DateTime emptiedAt) =>
        Last(now, day, hour) is var last && last > emptiedAt ? last : null;

    /// <summary>The next emptying after <paramref name="now"/>.</summary>
    public static DateTime Next(DateTime now, DayOfWeek day, int hour) => Last(now, day, hour).AddDays(7);
}
