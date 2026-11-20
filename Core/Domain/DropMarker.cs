namespace GaugeOverlay.Core.Domain;

/// <summary>Where the gold drop flash goes for a <see cref="DropSide"/> on an anchor block (slot-layout.plan.md §2): the block
/// itself for a swap, else a <see cref="Line"/>-thick line in the gap beside it.</summary>
public static class DropMarker
{
    public const double Line = 3;

    /// <param name="line">The line's thickness in the anchor's units: <see cref="Line"/>, or a fixed screen width over the zoom.</param>
    public static Cell Of(Cell anchor, DropSide side, double gap, double line = Line)
    {
        double near = gap / 2 + line / 2;
        return side switch
        {
            DropSide.Left => new(anchor.X - near, anchor.Y, line, anchor.H),
            DropSide.Right => new(anchor.X + anchor.W + near - line, anchor.Y, line, anchor.H),
            DropSide.Top => new(anchor.X, anchor.Y - near, anchor.W, line),
            DropSide.Bottom => new(anchor.X, anchor.Y + anchor.H + near - line, anchor.W, line),
            _ => anchor,
        };
    }
}
