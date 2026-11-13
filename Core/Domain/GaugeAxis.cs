namespace GaugeOverlay.Core.Domain;

/// <summary>The gauge bar's direction (pure, on <see cref="Cell"/>): flat runs left to right, upright (gauge window under
/// 따로) fills from the bottom. The renderer draws the cells it returns, so it holds no direction maths.</summary>
public static class GaugeAxis
{
    public const double ShineShare = 0.25, ShineMin = 2;   // the shine band: share of the fill's length, at least this

    /// <summary>The part of <paramref name="bar"/> between fractions <paramref name="from"/> and <paramref name="to"/> along its length.</summary>
    public static Cell Span(Cell bar, double from, double to, bool upright)
    {
        double a = Math.Clamp(Math.Min(from, to), 0, 1), b = Math.Clamp(Math.Max(from, to), 0, 1);
        return upright
            ? new Cell(bar.X, bar.Y + bar.H * (1 - b), bar.W, bar.H * (b - a))
            : new Cell(bar.X + bar.W * a, bar.Y, bar.W * (b - a), bar.H);
    }

    /// <summary>A 구분선 at fraction <paramref name="at"/>, <paramref name="thickness"/> wide (horizontal when upright).</summary>
    public static Cell Mark(Cell bar, double at, double thickness, bool upright) => upright
        ? new Cell(bar.X, bar.Y + bar.H * (1 - at) - thickness / 2, bar.W, thickness)
        : new Cell(bar.X + bar.W * at - thickness / 2, bar.Y, thickness, bar.H);

    /// <summary>The shine band at <paramref name="at"/> (0..1) over <paramref name="fill"/>, sliding in from the start edge
    /// (left / bottom) and out of the far one, cut to the fill; null when nothing of it is inside.</summary>
    public static Cell? Sweep(Cell fill, double at, bool upright)
    {
        double length = upright ? fill.H : fill.W;
        double w = Math.Max(ShineMin, length * ShineShare), start = -w + (length + w) * at;
        double from = Math.Max(0, start), to = Math.Min(length, start + w);
        if (to <= from) return null;
        return upright
            ? new Cell(fill.X, fill.Bottom - to, fill.W, to - from)
            : new Cell(fill.X + from, fill.Y, to - from, fill.H);
    }
}
