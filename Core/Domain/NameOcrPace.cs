namespace GaugeOverlay.Core.Domain;

/// <summary>How often the boss name's Windows OCR runs while it finds no gate (perf-hotspots.plan.md step 2). The same boss, by
/// its max HP, missing again and again (a boss not in raids.json: 일리아간 in v1_1막_clear) needs no OCR every second: the wait
/// doubles from 1 s up to <see cref="Longest"/>. Another max HP, or none read, is every second again, as before. The name strip
/// itself can't tell: the scene shows through it (frame-to-frame grey difference median 9.6 on v1, against about 1–2 for the
/// video's compression), so its pixels change while the name doesn't.</summary>
public sealed record NameOcrPace(int Misses, long? MaxHp)
{
    public static readonly NameOcrPace Start = new(0, null);
    static readonly TimeSpan First = TimeSpan.FromSeconds(1), Longest = TimeSpan.FromSeconds(30);

    /// <summary>The wait after the last OCR for a boss showing <paramref name="maxHp"/>.</summary>
    public TimeSpan Every(long? maxHp) => maxHp is null || maxHp != MaxHp || Misses <= 1
        ? First
        : TimeSpan.FromSeconds(Math.Min(Longest.TotalSeconds, Math.Pow(2, Misses - 1)));

    /// <summary>After an OCR: a gate found starts over; a miss counts on while the max HP is the same.</summary>
    public NameOcrPace Read(bool missed, long? maxHp) => !missed ? Start : new(maxHp == MaxHp ? Misses + 1 : 1, maxHp);
}
