namespace GaugeOverlay.Core.Domain;

/// <summary>How big the buff bars and debuff boxes are drawn: each bar's 길이 and 두께 (BuffIndicator), in every layout, and
/// each debuff box's own 너비 and 높이 (DebuffSettings.Boxes).</summary>
public static class BarSizes
{
    public const double MinLength = 0.1, MaxLength = 1, MinThickness = 0.25, MaxThickness = 3;
    public const double MinDebuff = 0.5, MaxDebuff = 3;

    public static double Length(BuffIndicator bar) => Math.Clamp(bar.Length, MinLength, MaxLength);
    public static double Thickness(BuffIndicator bar) => Math.Clamp(bar.Thickness, MinThickness, MaxThickness);

    /// <summary>A free layout's bar: its cell with 길이 along the longer side and 두께 across it. A flat bar keeps its left
    /// end and an upright one its bottom, the ends it drains towards.</summary>
    public static Cell Sized(Cell c, double length, double thickness) => c.W >= c.H
        ? c with { W = c.W * length, H = c.H * thickness }
        : new Cell(c.X, c.Bottom - c.H * length, c.W * thickness, c.H * length);

    /// <summary>The cell a bar drawn as <paramref name="drawn"/> stands for (<see cref="Sized"/> undone): what turning the
    /// fixed grid into a free layout keeps, so 길이 and 두께 aren't applied twice.</summary>
    public static Cell Unsized(Cell drawn, double length, double thickness, bool upright) => upright
        ? new Cell(drawn.X, drawn.Bottom - drawn.H / length, drawn.W / thickness, drawn.H / length)
        : drawn with { W = drawn.W / length, H = drawn.H / thickness };

    /// <summary>How far the drawn bar's bottom-right corner moves per unit its cell's moves, along each axis, so a corner
    /// drag keeps it under the pointer (an upright bar's bottom is its cell's).</summary>
    public static (double X, double Y) DragFactors(Cell c, double length, double thickness) =>
        c.W >= c.H ? (length, thickness) : (thickness, 1);

    /// <summary>The debuff row (내 색, 교체, 번개) from the left, tops level: each box <paramref name="w"/> × <paramref name="h"/>
    /// times its own size, <paramref name="gap"/> apart.</summary>
    public static IReadOnlyList<Cell> DebuffBoxes(DebuffSettings d, double w, double h, double gap)
    {
        var boxes = new List<Cell>(DebuffSettings.BoxCount);
        double x = 0;
        for (int i = 0; i < DebuffSettings.BoxCount; i++)
        {
            var own = d.Box(i);
            var cell = new Cell(x, 0, w * Math.Clamp(own.Width, MinDebuff, MaxDebuff), h * Math.Clamp(own.Height, MinDebuff, MaxDebuff));
            boxes.Add(cell);
            x = cell.Right + gap;
        }
        return boxes;
    }

    /// <summary>A debuff box's size after its drawn corner moved by <paramref name="dx"/>, <paramref name="dy"/> (레이아웃 편집기).</summary>
    public static (double W, double H) DebuffResized((double W, double H) start, (double W, double H) drawn, double dx, double dy) =>
        (Math.Round(Math.Clamp(start.W * (drawn.W + dx) / drawn.W, MinDebuff, MaxDebuff), 2),
         Math.Round(Math.Clamp(start.H * (drawn.H + dy) / drawn.H, MinDebuff, MaxDebuff), 2));
}
