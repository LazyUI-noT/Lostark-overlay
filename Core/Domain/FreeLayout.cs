namespace GaugeOverlay.Core.Domain;

/// <summary>What the overlay places: the twelve slots (same numbers as <see cref="SlotId"/>), the 각성기 변화 box, the
/// 아덴 게이지 and the four buff bars (<see cref="BuffKind"/> order; yours only).</summary>
public enum Part
{
    각성기, 초각스,
    일반1, 일반2, 일반3, 일반4,
    일반5, 일반6, 일반7, 일반8,
    아덴1, 아덴2,
    변화, 게이지,
    버프공증, 버프아덴, 버프초각, 버프뎀증,
}

/// <summary>Where a part sits, in DIP at box size 1.0 (the window's width and height settings scale X/W and Y/H).</summary>
public readonly record struct Cell(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
}

/// <summary>편집 모드의 칸 배치 (free layout): the default grid, snapping a dragged box to the others, and resizing
/// with the sizes a set of boxes shares.</summary>
public static class FreeLayout
{
    public const double Box = 34, Gap = 2;   // the grid's box edge and spacing (OverlayRenderer draws the same)
    public const double ChangedShare = 0.42; // 각성기 변화 box height, share of a box
    public const double SnapDistance = 6;    // DIP at size 1.0
    public const double MinSize = 8;
    public const double BuffHeight = 12;     // a buff bar's height at size 1.0
    public const double PlatePad = 4;        // the plate's margin around its boxes (OverlayRenderer draws the same)

    /// <summary>The first buff bar's part; <see cref="BuffKind"/> k is <c>FirstBuff + k</c>.</summary>
    public const Part FirstBuff = Part.버프공증;

    /// <summary>The fixed grid: gauge on top, then the 각성기 column (변화 above 각성기), 초각스, the eight keys in
    /// 4 × 2 and the 아덴 column; under it the buff bars in two rows, 공증 | 아덴피증 and 초각스피증 | 뎀증, each half
    /// the grid wide.</summary>
    public static Dictionary<Part, Cell> Default(double gaugeThickness)
    {
        double top = gaugeThickness + Gap, colH = 2 * Box + Gap, changedH = Math.Round(Box * ChangedShare);
        double x8 = 2 * (Box + Gap), aden = x8 + 4 * (Box + Gap);
        var d = new Dictionary<Part, Cell>
        {
            [Part.게이지] = new(0, 0, aden + Box, gaugeThickness),
            [Part.변화] = new(0, top, Box, changedH),
            [Part.각성기] = new(0, top + changedH + Gap, Box, colH - changedH - Gap),
            [Part.초각스] = new(Box + Gap, top, Box, colH),
            [Part.아덴1] = new(aden, top, Box, Box),
            [Part.아덴2] = new(aden, top + Box + Gap, Box, Box),
        };
        for (int i = 0; i < 8; i++)
            d[Part.일반1 + i] = new(x8 + i % 4 * (Box + Gap), top + i / 4 * (Box + Gap), Box, Box);
        double half = (aden + Box - Gap) / 2;
        for (int k = 0; k < 4; k++)
            d[FirstBuff + k] = new(k % 2 * (half + Gap), top + colH + Gap + k / 2 * (BuffHeight + Gap), half, BuffHeight);
        return d;
    }

    /// <summary>The parts whose width (and, for the eight keys, height) follows <paramref name="part"/>'s when sizes are
    /// linked, <paramref name="part"/> included.</summary>
    public static (IReadOnlyList<Part> Parts, bool Height) Linked(Part part) => part switch
    {
        >= Part.일반1 and <= Part.일반8 => ([.. Enumerable.Range(0, 8).Select(i => Part.일반1 + i)], true),
        Part.각성기 or Part.변화 => ([Part.각성기, Part.변화], false),
        Part.아덴1 or Part.아덴2 => ([Part.아덴1, Part.아덴2], false),
        _ => ([part], true),
    };

    /// <summary>A copy of <paramref name="cells"/> with <paramref name="part"/> sized <paramref name="w"/> ×
    /// <paramref name="h"/> (at least <see cref="MinSize"/>), keeping its top-left corner. When
    /// <paramref name="linked"/>, the parts that share its size are resized with it and stay packed as they were: each
    /// moves by the growth of the linked parts before it in its row (left of it) and column (above it).</summary>
    public static Dictionary<Part, Cell> Resize(IReadOnlyDictionary<Part, Cell> cells, Part part, double w, double h, bool linked)
    {
        w = Math.Max(MinSize, w);
        h = Math.Max(MinSize, h);
        var r = new Dictionary<Part, Cell>(cells) { [part] = cells[part] with { W = w, H = h } };
        if (!linked) return r;
        var (parts, height) = Linked(part);
        var set = parts.Where(cells.ContainsKey).Select(p => (Part: p, Cell: cells[p])).ToList();
        foreach (var (p, c) in set)
        {
            double left = set.Where(o => o.Cell.Right <= c.X && o.Cell.Y < c.Bottom && c.Y < o.Cell.Bottom).Sum(o => w - o.Cell.W);
            double above = set.Where(o => o.Cell.Bottom <= c.Y && o.Cell.X < c.Right && c.X < o.Cell.Right).Sum(o => h - o.Cell.H);
            var moved = c with { X = c.X + left, Y = height ? c.Y + above : c.Y };
            r[p] = height || p == part ? moved with { W = w, H = h } : moved with { W = w };
        }
        return r;
    }

    /// <summary><paramref name="cell"/> moved onto the nearest line of <paramref name="others"/> within
    /// <paramref name="distance"/>, per axis: edges level with theirs, or one <see cref="Gap"/> beside them.</summary>
    public static Cell SnapMove(Cell cell, IEnumerable<Cell> others, double distance)
    {
        double x = cell.X, y = cell.Y, bestX = distance, bestY = distance;
        foreach (var o in others)
        {
            foreach (double t in new[] { o.X, o.Right - cell.W, o.Right + Gap, o.X - Gap - cell.W })
                Closer(t, cell.X, ref x, ref bestX);
            foreach (double t in new[] { o.Y, o.Bottom - cell.H, o.Bottom + Gap, o.Y - Gap - cell.H })
                Closer(t, cell.Y, ref y, ref bestY);
        }
        return cell with { X = x, Y = y };
    }

    /// <summary><paramref name="cell"/> with its right and bottom edges moved onto the nearest line of
    /// <paramref name="others"/> within <paramref name="distance"/> (level with an edge, or one gap before one).</summary>
    public static Cell SnapResize(Cell cell, IEnumerable<Cell> others, double distance)
    {
        double right = cell.Right, bottom = cell.Bottom, bestX = distance, bestY = distance;
        foreach (var o in others)
        {
            foreach (double t in new[] { o.X - Gap, o.X, o.Right }) Closer(t, cell.Right, ref right, ref bestX);
            foreach (double t in new[] { o.Y - Gap, o.Y, o.Bottom }) Closer(t, cell.Bottom, ref bottom, ref bestY);
        }
        return cell with { W = Math.Max(MinSize, right - cell.X), H = Math.Max(MinSize, bottom - cell.Y) };
    }

    /// <summary>The layout after dragging <paramref name="part"/> (or its corner, <paramref name="resize"/>) by
    /// (<paramref name="dx"/>, <paramref name="dy"/>) DIP from <paramref name="start"/>, drawn at box scale
    /// <paramref name="scale"/>, snapped to the other <paramref name="drawn"/> boxes when <paramref name="snap"/>; a resize
    /// with <paramref name="link"/> (크기 묶기) resizes its set, and the gauge keeps its thickness.</summary>
    public static Dictionary<Part, Cell> Drag(IReadOnlyDictionary<Part, Cell> start, IEnumerable<Part> drawn, Part part,
        double dx, double dy, (double X, double Y) scale, bool resize, bool snap, bool link)
    {
        var at = start[part];
        // A resize with linked sizes moves the linked boxes along, so their edges aren't lines to snap to.
        var moving = resize && link ? Linked(part).Parts : [part];
        var others = drawn.Where(q => !moving.Contains(q) && start.ContainsKey(q)).Select(q => start[q]).ToList();
        double distance = SnapDistance / Math.Min(scale.X, scale.Y);
        if (!resize)
        {
            var moved = at with { X = at.X + dx / scale.X, Y = at.Y + dy / scale.Y };
            return new(start) { [part] = snap ? SnapMove(moved, others, distance) : moved };
        }
        // The gauge's height is its thickness (게이지 tab), so only its width changes here.
        var sized = at with { W = at.W + dx / scale.X, H = part == Part.게이지 ? at.H : at.H + dy / scale.Y };
        if (snap) sized = SnapResize(sized, others, distance) with { H = part == Part.게이지 ? at.H : sized.H };
        return Resize(start, part, sized.W, sized.H, link);
    }

    /// <summary>The plates around <paramref name="boxes"/>, each with the indexes of the boxes on it: a box's plate is
    /// the box grown by <paramref name="pad"/>, and plates that would overlap become one (repeated until none do, so
    /// boxes closer than two pads share a plate and no part is darkened twice).</summary>
    public static List<(Cell Plate, List<int> Members)> Plates(IReadOnlyList<Cell> boxes, double pad)
    {
        var plates = boxes.Select((b, i) => (Plate: new Cell(b.X - pad, b.Y - pad, b.W + 2 * pad, b.H + 2 * pad), Members: new List<int> { i })).ToList();
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int i = 0; i < plates.Count && !merged; i++)
                for (int j = i + 1; j < plates.Count && !merged; j++)
                {
                    Cell a = plates[i].Plate, b = plates[j].Plate;
                    if (!(a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom)) continue;
                    double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
                    plates[i] = (new Cell(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y),
                        [.. plates[i].Members.Concat(plates[j].Members).Order()]);
                    plates.RemoveAt(j);
                    merged = true;
                }
        }
        return plates;
    }

    /// <summary>The skill boxes and gauge of <paramref name="cells"/> (at size 1.0) stand on more than one plate: a
    /// layout with skills placed apart. Buff bars don't count (party windows don't draw them).</summary>
    public static bool Spread(IReadOnlyDictionary<Part, Cell> cells) =>
        Plates([.. cells.Where(c => c.Key < FirstBuff).Select(c => c.Value)], PlatePad).Count > 1;

    static void Closer(double target, double from, ref double at, ref double best)
    {
        double d = Math.Abs(target - from);
        if (d > best) return;
        best = d;
        at = target;
    }
}
