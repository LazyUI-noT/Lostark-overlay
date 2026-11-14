namespace GaugeOverlay.Core.Domain;

/// <summary>The windows whose size is set by dragging their outline on 내 오버레이 (no slider).</summary>
public enum SizedWindow { Judgment, Amsu, Raid, Meter, Gauge, Ring }

/// <summary>What the window's Width/Height settings mean: <c>Two</c> sizes (단심 길이/두께, 암수 크기/막대 길이), one size kept in
/// both (<c>Uniform</c>), or one size in <c>WidthOnly</c> / <c>HeightOnly</c>.</summary>
public enum SizeMode { Uniform, Two, WidthOnly, HeightOnly }

/// <summary>The outline handle dragged: the corner, or the middle of the right / bottom edge.</summary>
public enum SizeHandle { Corner, Right, Bottom }

public static class WindowSizing
{
    const double Min = ClassProfiles.MinScale, Max = ClassProfiles.MaxScale;

    /// <summary>Which sizes <paramref name="w"/> has with the look <paramref name="s"/> gives it (the settings page's old sliders).</summary>
    public static SizeMode ModeOf(Settings s, SizedWindow w) => w switch
    {
        SizedWindow.Judgment => s.Judgment.Display == JudgmentDisplay.Bar ? SizeMode.Two : SizeMode.WidthOnly,
        SizedWindow.Amsu => s.Amsu.Look is AmsuLook.막대 or AmsuLook.세로막대 ? SizeMode.Two : SizeMode.HeightOnly,
        SizedWindow.Gauge or SizedWindow.Ring => SizeMode.Two,   // 길이 (Width) and 두께 (Height)
        _ => SizeMode.Uniform,
    };

    /// <summary>Width/Height settings after <paramref name="handle"/> moved by <paramref name="dx"/>, <paramref name="dy"/> DIPs on a plate
    /// of <paramref name="plate"/> drawn at <paramref name="start"/>. A corner keeps the ratio (stopping where a size reaches its
    /// limit); an edge stretches one size, only where there are two.</summary>
    public static (double W, double H) Resize(SizeMode mode, SizeHandle handle, (double W, double H) start, (double W, double H) plate, double dx, double dy)
    {
        if (plate.W <= 0 || plate.H <= 0) return start;
        double kx = (plate.W + dx) / plate.W, ky = (plate.H + dy) / plate.H;
        if (mode == SizeMode.Two && handle == SizeHandle.Right) return (Limit(start.W * kx), start.H);
        if (mode == SizeMode.Two && handle == SizeHandle.Bottom) return (start.W, Limit(start.H * ky));
        double k = Math.Abs(kx - 1) >= Math.Abs(ky - 1) ? kx : ky;
        return mode switch
        {
            SizeMode.WidthOnly => (Limit(start.W * k), start.H),
            SizeMode.HeightOnly => (start.W, Limit(start.H * k)),
            _ => Both(start, k),
        };
    }

    /// <summary>Arrow keys with Ctrl: <paramref name="dx"/> (left/right) the width, <paramref name="dy"/> (up/down) the height where
    /// there are two sizes; one size scales as a whole.</summary>
    public static (double W, double H) ByKey(SizeMode mode, (double W, double H) start, (double W, double H) plate, double dx, double dy) =>
        Resize(mode, dx != 0 ? SizeHandle.Right : SizeHandle.Bottom, start, plate, dx, dy);

    /// <summary><see cref="Resize"/> for a window's own settings: the upright 암수 bar is its 막대 길이 (Width) tall and its 크기
    /// (Height) wide, so there the screen's two axes swap places.</summary>
    public static (double W, double H) Resize(Settings s, SizedWindow w, SizeHandle handle, (double W, double H) start, (double W, double H) plate, double dx, double dy) =>
        Swapped(s, w) ? Flip(Resize(ModeOf(s, w), handle, Flip(start), plate, dx, dy)) : Resize(ModeOf(s, w), handle, start, plate, dx, dy);

    /// <summary><see cref="ByKey"/> for a window's own settings (see <see cref="Resize(Settings, SizedWindow, SizeHandle, ValueTuple{double, double}, ValueTuple{double, double}, double, double)"/>).</summary>
    public static (double W, double H) ByKey(Settings s, SizedWindow w, (double W, double H) start, (double W, double H) plate, double dx, double dy) =>
        Swapped(s, w) ? Flip(ByKey(ModeOf(s, w), Flip(start), plate, dx, dy)) : ByKey(ModeOf(s, w), start, plate, dx, dy);

    static bool Swapped(Settings s, SizedWindow w) => w == SizedWindow.Amsu && s.Amsu.Look == AmsuLook.세로막대 || w == SizedWindow.Gauge && s.Gauge.Upright;

    static (double W, double H) Flip((double W, double H) v) => (v.H, v.W);

    static (double W, double H) Both((double W, double H) start, double k)
    {
        double lo = Math.Max(Min / start.W, Min / start.H), hi = Math.Min(Max / start.W, Max / start.H);
        if (lo > hi || start.W <= 0 || start.H <= 0) return start;   // a size outside the range or zero (an old file): leave it
        k = Math.Clamp(k, lo, hi);
        return (Limit(start.W * k), Limit(start.H * k));
    }

    static double Limit(double v) => Math.Round(Math.Clamp(v, Min, Max), 2);
}
