namespace GaugeOverlay.Core.Domain;

/// <summary>A group on the 합쳐서 plate (slot-layout.plan.md §3). 스킬 is the anchor; the others dock to it.</summary>
public enum DockGroup { Skills, Gauge, Buffs }

public enum DockSide { Top, Bottom, Left, Right }

/// <summary>방향: 자동 follows the dock side (top/bottom flat, left/right upright), or fixed 가로 / 세로.</summary>
public enum Direction { Auto, Flat, Upright }

/// <summary>A docked group, its side and 방향.</summary>
public readonly record struct DockSpot(DockGroup Group, DockSide Side, Direction Direction = Direction.Auto)
{
    /// <summary>자동 follows the side; 가로 / 세로 are kept, except 아덴 게이지 above or below the skills, which lies flat
    /// (a thin upright gauge beside a grid-wide band read as broken, dock-gbv-bth.png).</summary>
    public bool Upright => Direction == Direction.Auto || Group == DockGroup.Gauge && Side is DockSide.Top or DockSide.Bottom
        ? Side is DockSide.Left or DockSide.Right
        : Direction == Direction.Upright;
}

/// <summary>Where each group lands, from (0, 0) at the plate's content top left.</summary>
public sealed record Docked(Cell Skills, IReadOnlyDictionary<DockGroup, (Cell Cell, bool Upright)> Groups, double Width, double Height);

/// <summary>The dock order as text, for settings and the party Look: entries by ',', each group letter (g 게이지, b 버프),
/// side letter (t b l r) and 방향 letter (a 자동, h 가로, v 세로). Each of 게이지 and 버프 exactly once, else the default.</summary>
public static class DockOrder
{
    public const int MaxText = 16;
    public const string DefaultText = "gta,bba";
    /// <summary>Today's plate: 게이지 on top, 버프 under the skills.</summary>
    public static readonly IReadOnlyList<DockSpot> Default = [new(DockGroup.Gauge, DockSide.Top), new(DockGroup.Buffs, DockSide.Bottom)];

    const string Groups = "?gb", Sides = "tblr", Directions = "ahv";

    public static string Format(IReadOnlyList<DockSpot> order) =>
        string.Join(',', order.Select(s => $"{Groups[(int)s.Group]}{Sides[(int)s.Side]}{Directions[(int)s.Direction]}"));

    public static IReadOnlyList<DockSpot> Of(string? text) => Parse(text) ?? Default;

    public static IReadOnlyList<DockSpot>? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxText) return null;
        var order = new List<DockSpot>();
        foreach (var e in text.Split(','))
        {
            if (e.Length != 3) return null;
            int g = Groups.IndexOf(e[0]), side = Sides.IndexOf(e[1]), dir = Directions.IndexOf(e[2]);
            if (g < 1 || side < 0 || dir < 0) return null;
            order.Add(new((DockGroup)g, (DockSide)side, (Direction)dir));
        }
        return order.Count == 2 && order.Select(s => s.Group).Distinct().Count() == 2 ? order : null;
    }

    /// <summary>The order with <paramref name="group"/>'s 방향 set.</summary>
    public static IReadOnlyList<DockSpot> WithDirection(IReadOnlyList<DockSpot> order, DockGroup group, Direction direction) =>
        [.. order.Select(s => s.Group == group ? s with { Direction = direction } : s)];

    public static Direction DirectionOf(IReadOnlyList<DockSpot> order, DockGroup group) =>
        order.FirstOrDefault(s => s.Group == group).Direction;
}

/// <summary>Group docking (slot-layout.plan.md §3): each group docks in order to one side of what is already placed.</summary>
public static class DockLayout
{
    /// <summary>Lays the plate out. A flat group docked top/bottom spans the full width, an upright one docked left/right the
    /// full height (at least <paramref name="minTall"/>); a group whose 방향 crosses its side takes the skills' own width or
    /// height (at least <paramref name="minTall"/> upright). A gap separates a group only from something with size on that
    /// side.</summary>
    /// <param name="depth">The group's thickness across its dock edge, given whether it stands upright and its length.</param>
    public static Docked Arrange(IReadOnlyList<DockSpot> order, double skillW, double skillH, Func<DockGroup, bool> present,
        Func<DockGroup, bool, double, double> depth, double minTall, double gap)
    {
        var skills = new Cell(0, 0, skillW, skillH);
        var placed = new Dictionary<DockGroup, (Cell Cell, bool Upright)>();
        var b = skills;
        foreach (var spot in order.Where(s => present(s.Group)))
        {
            bool up = spot.Upright, across = spot.Side is DockSide.Top or DockSide.Bottom;
            double length = up
                ? Math.Max(across ? skillH : b.H, minTall)
                : across ? b.W : skillW;
            double d = depth(spot.Group, up, length);
            var (w, h) = up ? (d, length) : (length, d);
            double gy = b.H > 0 ? gap : 0, gx = b.W > 0 ? gap : 0;
            var c = spot.Side switch
            {
                DockSide.Top => new Cell(b.X, b.Y - gy - h, w, h),
                DockSide.Bottom => new Cell(b.X, b.Bottom + gy, w, h),
                DockSide.Left => new Cell(b.X - gx - w, b.Y, w, h),
                _ => new Cell(b.Right + gx, b.Y, w, h),
            };
            placed[spot.Group] = (c, up);
            b = Union(b, c);
        }
        Cell Shift(Cell c) => c with { X = c.X - b.X, Y = c.Y - b.Y };
        return new Docked(Shift(skills), placed.ToDictionary(p => p.Key, p => (Shift(p.Value.Cell), p.Value.Upright)), b.W, b.H);
    }

    static Cell Union(Cell a, Cell c)
    {
        if (a.W <= 0 && a.H <= 0) return c;
        double x = Math.Min(a.X, c.X), y = Math.Min(a.Y, c.Y);
        return new Cell(x, y, Math.Max(a.Right, c.Right) - x, Math.Max(a.Bottom, c.Bottom) - y);
    }
}

/// <summary>A drag's aim: whether it moves the whole group, the group it swaps with, or the plate side it docks on.</summary>
public readonly record struct DockAim(bool Group, DockGroup? Swap, DockSide? Edge);

/// <summary>Group drags in 칸 맞춤 (slot-layout.plan.md §5): how deep the cursor is in another group, the plate edge it is
/// just outside, and the dock order after the drop.</summary>
public static class DockDrop
{
    /// <summary>Into another group past this, a block drag becomes the whole group; past <see cref="SwapDepth"/>, dropping
    /// swaps the two groups.</summary>
    public const double GroupDepth = 1.0 / 6, SwapDepth = 0.5;

    /// <summary>How far (0–1) (<paramref name="x"/>, <paramref name="y"/>) is into <paramref name="other"/>, from its edge facing
    /// <paramref name="dragged"/>; 0 outside it.</summary>
    public static double Depth(Cell dragged, Cell other, double x, double y)
    {
        if (x < other.X || x >= other.Right || y < other.Y || y >= other.Bottom) return 0;
        double dx = other.X + other.W / 2 - (dragged.X + dragged.W / 2), dy = other.Y + other.H / 2 - (dragged.Y + dragged.H / 2);
        return Math.Abs(dx) >= Math.Abs(dy)
            ? dx >= 0 ? (x - other.X) / other.W : (other.Right - x) / other.W
            : dy >= 0 ? (y - other.Y) / other.H : (other.Bottom - y) / other.H;
    }

    /// <summary>The side of <paramref name="plate"/> the cursor is within <paramref name="reach"/> outside of, or null (inside,
    /// too far, or past a corner).</summary>
    public static DockSide? EdgeAt(Cell plate, double x, double y, double reach)
    {
        bool inX = x >= plate.X && x < plate.Right, inY = y >= plate.Y && y < plate.Bottom;
        if (inX && y < plate.Y && y >= plate.Y - reach) return DockSide.Top;
        if (inX && y >= plate.Bottom && y < plate.Bottom + reach) return DockSide.Bottom;
        if (inY && x < plate.X && x >= plate.X - reach) return DockSide.Left;
        if (inY && x >= plate.Right && x < plate.Right + reach) return DockSide.Right;
        return null;
    }

    static DockSide Opposite(DockSide s) => s switch
    {
        DockSide.Top => DockSide.Bottom,
        DockSide.Bottom => DockSide.Top,
        DockSide.Left => DockSide.Right,
        _ => DockSide.Left,
    };

    /// <summary>Two docked groups trade sides, or places in the order when on one side; a group swapped with 스킬 passes to
    /// its opposite side, docking next to 스킬.</summary>
    public static IReadOnlyList<DockSpot> Swap(IReadOnlyList<DockSpot> order, DockGroup a, DockGroup b)
    {
        if (a == b) return order;
        if (a == DockGroup.Skills || b == DockGroup.Skills)
        {
            var g = a == DockGroup.Skills ? b : a;
            var spot = order.First(s => s.Group == g);
            return [spot with { Side = Opposite(spot.Side) }, .. order.Where(s => s.Group != g)];
        }
        var (pa, pb) = (order.First(s => s.Group == a), order.First(s => s.Group == b));
        if (pa.Side == pb.Side) return [.. order.Select(s => s.Group == a ? pb : s.Group == b ? pa : s)];
        return [.. order.Select(s => s.Group == a ? s with { Side = pb.Side } : s.Group == b ? s with { Side = pa.Side } : s)];
    }

    /// <summary>What a drag of <paramref name="held"/>'s block, bar or whole group (<paramref name="group"/>) aims at with the
    /// cursor at (<paramref name="x"/>, <paramref name="y"/>): past <see cref="GroupDepth"/> into another group the drag becomes
    /// the group's, past <see cref="SwapDepth"/> it swaps with it; a group drag just outside <paramref name="area"/> docks
    /// on that side.</summary>
    public static DockAim AimAt(DockGroup held, bool group, IReadOnlyDictionary<DockGroup, Cell> groups, Cell area, double x, double y, double reach)
    {
        if (!groups.TryGetValue(held, out var own)) return new(group, null, null);
        foreach (var (g, c) in groups)
        {
            if (g == held) continue;
            double depth = Depth(own, c, x, y);
            if (depth >= SwapDepth) return new(true, g, null);
            if (depth >= GroupDepth) group = true;
        }
        return new(group, null, group ? EdgeAt(area, x, y, reach) : null);
    }

    /// <summary>The dock order after dropping <paramref name="held"/> with <paramref name="aim"/>; the same order when it aims
    /// at nothing.</summary>
    public static IReadOnlyList<DockSpot> Apply(IReadOnlyList<DockSpot> order, DockGroup held, DockAim aim) =>
        aim.Swap is { } with ? Swap(order, held, with) : aim.Edge is { } side ? DockAt(order, held, side) : order;

    /// <summary><paramref name="group"/> docks last, on <paramref name="side"/> of everything placed, keeping its 방향.</summary>
    public static IReadOnlyList<DockSpot> DockAt(IReadOnlyList<DockSpot> order, DockGroup group, DockSide side) =>
        group == DockGroup.Skills ? order
            : [.. order.Where(s => s.Group != group), order.First(s => s.Group == group) with { Side = side }];
}
