namespace GaugeOverlay.Core.Domain;

/// <summary>The 따로 buff window in 칸 맞춤 (slot-layout.plan.md §4): the bars in one column, in the saved arrangement
/// (<see cref="BarArrangement"/>) read row by row; one row of upright bars when 세로.</summary>
public static class BuffStack
{
    /// <summary>The shown bars in arrangement order.</summary>
    public static IReadOnlyList<BuffKind> Order(IReadOnlyList<IReadOnlyList<BuffKind>> rows, Func<BuffKind, bool> shown) =>
        [.. rows.SelectMany(r => r).Where(shown)];

    /// <summary>Each bar's cell from (0, 0), and the window's size (the bars closed up: a hidden bar takes its share off).
    /// Flat: top to bottom, <paramref name="full"/> wide, each bar <paramref name="length"/> of it, drained from the left.
    /// Upright: the first bar rightmost, <paramref name="full"/> tall, each bar <paramref name="length"/> of it standing on the
    /// bottom edge. <paramref name="thickness"/> is a bar's own (DIP): its share of the stack.</summary>
    public static (IReadOnlyList<(BuffKind Kind, Cell Cell)> Bars, double Width, double Height) Arrange(IReadOnlyList<BuffKind> order,
        bool upright, double full, double length, double gap, Func<BuffKind, double> thickness)
    {
        if (order.Count == 0) return ([], 0, 0);
        double Thick(BuffKind k) => Math.Max(2, Math.Round(thickness(k)));
        double along = Math.Max(2, Math.Round(full * length));
        var bars = new List<(BuffKind, Cell)>(order.Count);
        if (!upright)
        {
            double y = 0;
            foreach (var k in order) { bars.Add((k, new Cell(0, y, along, Thick(k)))); y += Thick(k) + gap; }
            return (bars, full, y - gap);
        }
        double width = order.Sum(Thick) + (order.Count - 1) * gap, x = width;
        foreach (var k in order) { x -= Thick(k); bars.Add((k, new Cell(x, full - along, Thick(k), along))); x -= gap; }
        return (bars, width, full);
    }
}

/// <summary>Drops in the 따로 buff window (§4): bars swap, or insert before/after another, as in §2; the arrangement's rows keep
/// their shape, only the order of the bars through them changes.</summary>
public static class BuffStackDrop
{
    /// <summary>A bar's share along the stack, at each end, that inserts.</summary>
    public const double End = 0.25;

    /// <summary>The target under the cursor: Top/Bottom (flat) or Left/Right (upright) near the ends, else the middle; null over
    /// nothing or the dragged bar.</summary>
    public static BarTarget? TargetAt(BuffKind dragged, IEnumerable<(BuffKind Kind, Cell Cell)> bars, bool upright, double x, double y)
    {
        foreach (var (kind, c) in bars)
        {
            if (x < c.X || x >= c.X + c.W || y < c.Y || y >= c.Y + c.H) continue;
            if (kind == dragged) return null;
            double f = upright ? (x - c.X) / c.W : (y - c.Y) / c.H;
            var low = upright ? DropSide.Left : DropSide.Top;
            var high = upright ? DropSide.Right : DropSide.Bottom;
            return new BarTarget(kind, f < End ? low : f > 1 - End ? high : DropSide.Middle);
        }
        return null;
    }

    /// <summary>The arrangement after the drop. Upright, the first bar is rightmost, so Right means before.</summary>
    public static IReadOnlyList<IReadOnlyList<BuffKind>> Apply(IReadOnlyList<IReadOnlyList<BuffKind>> rows, BuffKind dragged,
        BarTarget target, bool upright)
    {
        if (target.Anchor == dragged) return rows;
        var flat = rows.SelectMany(r => r).ToList();
        if (target.Side == DropSide.Middle)
        {
            int i = flat.IndexOf(dragged), j = flat.IndexOf(target.Anchor);
            (flat[i], flat[j]) = (flat[j], flat[i]);
        }
        else
        {
            flat.Remove(dragged);
            bool before = upright ? target.Side == DropSide.Right : target.Side == DropSide.Top;
            flat.Insert(flat.IndexOf(target.Anchor) + (before ? 0 : 1), dragged);
        }
        var result = new List<IReadOnlyList<BuffKind>>();
        int at = 0;
        foreach (var row in rows) { result.Add([.. flat.Skip(at).Take(row.Count)]); at += row.Count; }
        return result;
    }
}
