namespace GaugeOverlay.Core.Domain;

/// <summary>When the DPS 미터기 and the 줄 수 창 show (user, 2026-11-01): they read all the time, but show only from a read (boss
/// HP or the 전분 panel; a boss bar count) until <see cref="For"/> after the last one, so a cutscene doesn't blink them out.
/// 편집 모드 and 설정 show them regardless (App). Pure.</summary>
public static class WindowLinger
{
    public static readonly TimeSpan For = TimeSpan.FromSeconds(10);

    /// <summary>Whether a window last read at <paramref name="lastRead"/> (null: never) shows at <paramref name="now"/>.</summary>
    public static bool InUse(TimeSpan? lastRead, TimeSpan now) => lastRead is { } t && now - t <= For;

    /// <summary>The DPS 미터기 window: from its first read (a boss hit) until <see cref="For"/> after the last, and on while the
    /// player stays in the dungeon that read was in (<paramref name="presence"/>: the in-game meter or a boss bar still seen),
    /// so a long phase change or cutscene doesn't hide it.</summary>
    public static bool MeterInUse(TimeSpan? lastRead, RaidPresence presence, TimeSpan now) =>
        InUse(lastRead, now) || lastRead is { } r && presence.Since is { } since && r >= since && presence.InDungeon(now);

    /// <summary>The last read after this step.</summary>
    public static TimeSpan? Seen(TimeSpan? lastRead, bool readNow, TimeSpan now) => readNow ? now : lastRead;

    /// <summary>The meter's last read: the later of its 전분 panel and boss HP reads (null: neither yet).</summary>
    public static TimeSpan? LastRead(MeterStatus? status)
    {
        if (status is null) return null;
        var t = status.LastRead > status.HpAt ? status.LastRead : status.HpAt;
        return t > TimeSpan.Zero ? t : null;
    }
}
