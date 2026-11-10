namespace GaugeOverlay.Core.Domain;

/// <summary>설정's row rule (settings-design.plan.md §2): a row is marked when its value differs from a fresh
/// <see cref="Settings"/>, and its ↺ copies that default back. Defaults come from <c>new Settings()</c>, never a second table.
/// Rows read leaf values (bool, int, double, enum, string); <c>get</c> must not hand out a mutable part of the defaults.</summary>
public static class RowDefault
{
    // Slider steps land on values like 0.9000000000001; closer than this counts as the default.
    private const double Tolerance = 1e-9;

    private static readonly Settings Defaults = new();

    public static bool Differs<T>(Settings s, Func<Settings, T> get) => !Same(get(s), get(Defaults));

    public static void Reset<T>(Settings s, Func<Settings, T> get, Action<Settings, T> set) => set(s, get(Defaults));

    private static bool Same<T>(T a, T b) => (a, b) switch
    {
        (double x, double y) => x.Equals(y) || Math.Abs(x - y) < Tolerance,   // Equals: NaN default stays NaN
        _ => EqualityComparer<T>.Default.Equals(a, b),
    };
}
