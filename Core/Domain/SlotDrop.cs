namespace GaugeOverlay.Core.Domain;

/// <summary>Where on a block a drop lands: its middle (swap) or one of its sides (slot-layout.plan.md §2).</summary>
public enum DropSide { Middle, Left, Right, Top, Bottom }

/// <summary>A skill drop, kept as the block it lands on and the side, so it stays right once the dragged block has left.</summary>
public readonly record struct SlotTarget(SlotId Anchor, DropSide Side);

/// <summary>A buff bar drop: <see cref="DropSide.Left"/>/<see cref="DropSide.Right"/> insert beside the bar,
/// <see cref="DropSide.Top"/>/<see cref="DropSide.Bottom"/> make a new row.</summary>
public readonly record struct BarTarget(BuffKind Anchor, DropSide Side);

/// <summary>내 스킬창's arrangement as text, for settings and the party Look: columns by '|', a stack "s" + ids by ',', a key
/// run "r" + its top ids '/' its bottom ids (ids are <see cref="SlotId"/> numbers). Every box exactly once, else the default.</summary>
public static class SlotArrangement
{
    public const int MaxText = 64;

    public static string Format(IReadOnlyList<SlotColumn> columns) => string.Join('|', columns.Select(c => c switch
    {
        SlotStack s => "s" + Ids(s.Blocks),
        KeyRun r => "r" + Ids(r.Top) + "/" + Ids(r.Bottom),
        _ => "",
    }));

    static string Ids(IEnumerable<SlotId> ids) => string.Join(',', ids.Select(i => (int)i));

    /// <summary><paramref name="text"/>'s arrangement, or <see cref="SlotGrid.Default"/> when it is missing or not a whole one.</summary>
    public static IReadOnlyList<SlotColumn> Of(string? text) => Parse(text) ?? SlotGrid.Default;

    /// <summary>Null unless every box is in it once, every stack has a block and every run a key.</summary>
    public static IReadOnlyList<SlotColumn>? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxText) return null;
        var columns = new List<SlotColumn>();
        foreach (var part in text.Split('|'))
        {
            if (part.Length == 0) return null;
            SlotColumn? column = part[0] switch
            {
                's' when ParseIds(part[1..]) is { Count: > 0 } ids => new SlotStack(ids),
                'r' when part[1..].Split('/') is [var top, var bottom] && ParseIds(top) is { } t && ParseIds(bottom) is { } b
                    && t.Count + b.Count > 0 => new KeyRun(t, b),
                _ => null,
            };
            if (column is null) return null;
            columns.Add(column);
        }
        var all = columns.SelectMany(Blocks).ToList();
        return all.Count == SlotInfo.All.Length && all.Distinct().Count() == all.Count ? columns : null;
    }

    static List<SlotId>? ParseIds(string text)
    {
        if (text.Length == 0) return [];
        var ids = new List<SlotId>();
        foreach (var t in text.Split(','))
        {
            if (!int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) || !Enum.IsDefined((SlotId)n)) return null;
            ids.Add((SlotId)n);
        }
        return ids;
    }

    public static IEnumerable<SlotId> Blocks(SlotColumn c) => c switch
    {
        SlotStack s => s.Blocks,
        KeyRun r => r.Top.Concat(r.Bottom),
        _ => [],
    };
}

/// <summary>The buff bars' rows as text: rows by '/', bars by ',' (<see cref="BuffKind"/> numbers); every bar once, else the default.</summary>
public static class BarArrangement
{
    public const int MaxText = 32;

    public static string Format(IReadOnlyList<IReadOnlyList<BuffKind>> rows) => string.Join('/', rows.Select(r => string.Join(',', r.Select(k => (int)k))));

    public static IReadOnlyList<IReadOnlyList<BuffKind>> Of(string? text) => Parse(text) ?? BarRows.Default;

    public static IReadOnlyList<IReadOnlyList<BuffKind>>? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxText) return null;
        var rows = new List<IReadOnlyList<BuffKind>>();
        foreach (var row in text.Split('/'))
        {
            var kinds = new List<BuffKind>();
            foreach (var t in row.Split(','))
            {
                if (!int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) || !Enum.IsDefined((BuffKind)n)) return null;
                kinds.Add((BuffKind)n);
            }
            rows.Add(kinds);
        }
        var all = rows.SelectMany(r => r).ToList();
        return all.Count == Enum.GetValues<BuffKind>().Length && all.Distinct().Count() == all.Count ? rows : null;
    }
}

/// <summary>Skill drops in 칸 맞춤 (slot-layout.plan.md §2): where the cursor lands, and the arrangement after the drop.</summary>
public static class SlotDrop
{
    /// <summary>A key's left/right share that inserts; a stack block's top/bottom share that joins, and left/right that
    /// makes a new column.</summary>
    public const double KeySide = 0.25, StackEnd = 0.3, StackSide = 0.2;

    /// <summary>The target under (<paramref name="x"/>, <paramref name="y"/>) among the drawn <paramref name="cells"/>, or null
    /// (nothing there, or the dragged block itself).</summary>
    public static SlotTarget? TargetAt(SlotId dragged, IEnumerable<(SlotId Slot, Cell Cell)> cells, IReadOnlyList<SlotColumn> columns,
        double x, double y)
    {
        foreach (var (slot, c) in cells)
        {
            if (x < c.X || x >= c.X + c.W || y < c.Y || y >= c.Y + c.H) continue;
            if (slot == dragged) return null;
            double fx = (x - c.X) / c.W, fy = (y - c.Y) / c.H;
            bool inRun = columns.Any(col => col is KeyRun r && r.Top.Concat(r.Bottom).Contains(slot));
            var side = inRun
                ? fx < KeySide ? DropSide.Left : fx > 1 - KeySide ? DropSide.Right : DropSide.Middle
                : fy < StackEnd ? DropSide.Top : fy > 1 - StackEnd ? DropSide.Bottom
                : fx < StackSide ? DropSide.Left : fx > 1 - StackSide ? DropSide.Right : DropSide.Middle;
            return new SlotTarget(slot, side);
        }
        return null;
    }

    sealed class Col(bool run, List<SlotId> a, List<SlotId> b)
    {
        public bool Run = run;
        public List<SlotId> A = a, B = b;   // a stack's blocks are A
        public bool Has(SlotId s) => A.Contains(s) || B.Contains(s);
        public List<SlotId> RowOf(SlotId s) => A.Contains(s) ? A : B;
    }

    /// <summary>The arrangement after <paramref name="dragged"/> drops on <paramref name="target"/>: swapped, inserted into a
    /// key row, the run split around 각성기/초각스, a stack joined or a new column; then empty columns go and neighbouring
    /// runs join.</summary>
    public static IReadOnlyList<SlotColumn> Apply(IReadOnlyList<SlotColumn> columns, SlotId dragged, SlotTarget target)
    {
        if (target.Anchor == dragged) return columns;
        var cols = columns.Select(c => c switch
        {
            KeyRun r => new Col(true, [.. r.Top], [.. r.Bottom]),
            SlotStack s => new Col(false, [.. s.Blocks], []),
            _ => new Col(false, [], []),
        }).ToList();
        if (target.Side == DropSide.Middle)
        {
            var (from, to) = (cols.First(c => c.Has(dragged)).RowOf(dragged), cols.First(c => c.Has(target.Anchor)).RowOf(target.Anchor));
            int i = from.IndexOf(dragged), j = to.IndexOf(target.Anchor);
            (from[i], to[j]) = (target.Anchor, dragged);
            return Tidy(cols);
        }
        cols.First(c => c.Has(dragged)).RowOf(dragged).Remove(dragged);
        var home = cols.First(c => c.Has(target.Anchor));
        int at = cols.IndexOf(home);
        bool after = target.Side is DropSide.Right or DropSide.Bottom;
        if (home.Run && target.Side is DropSide.Left or DropSide.Right)
        {
            var row = home.RowOf(target.Anchor);
            int k = row.IndexOf(target.Anchor) + (after ? 1 : 0);
            if (dragged is not (SlotId.각성기 or SlotId.초각스)) row.Insert(k, dragged);
            else
            {
                var right = new Col(true, [.. home.A.Skip(k)], [.. home.B.Skip(k)]);
                (home.A, home.B) = ([.. home.A.Take(k)], [.. home.B.Take(k)]);
                cols.InsertRange(at + 1, [new Col(false, [dragged], []), right]);
            }
        }
        else if (!home.Run && target.Side is DropSide.Top or DropSide.Bottom)
            home.A.Insert(home.A.IndexOf(target.Anchor) + (after ? 1 : 0), dragged);
        else
            cols.Insert(at + (after ? 1 : 0), new Col(false, [dragged], []));
        return Tidy(cols);
    }

    static IReadOnlyList<SlotColumn> Tidy(List<Col> cols)
    {
        var kept = new List<Col>();
        foreach (var c in cols.Where(c => c.A.Count + c.B.Count > 0))
            if (c.Run && kept.LastOrDefault() is { Run: true } last) { last.A.AddRange(c.A); last.B.AddRange(c.B); }
            else kept.Add(c);
        return [.. kept.Select(c => c.Run ? (SlotColumn)new KeyRun(c.A, c.B) : new SlotStack(c.A))];
    }
}

/// <summary>Buff bar drops in 칸 맞춤 (slot-layout.plan.md §2).</summary>
public static class BarDrop
{
    /// <summary>A bar's end share that inserts beside it.</summary>
    public const double End = 0.2;

    /// <summary>The target under the cursor: on a bar, its middle or an end; in the gap past its long side
    /// (<paramref name="gap"/> deep), a new row there; else null.</summary>
    public static BarTarget? TargetAt(BuffKind dragged, IEnumerable<(BuffKind Kind, Cell Cell)> bars, double gap, double x, double y)
    {
        var list = bars.ToList();
        // a bar under the cursor first: rows closer than the reach would otherwise hand it to the gap of the bar above
        var on = list.Where(b => y >= b.Cell.Y && y < b.Cell.Y + b.Cell.H);
        foreach (var (kind, c) in on.Concat(list))
        {
            if (x < c.X || x >= c.X + c.W || y < c.Y - gap || y >= c.Y + c.H + gap) continue;
            if (kind == dragged) return null;
            double fx = (x - c.X) / c.W;
            var side = y < c.Y ? DropSide.Top : y >= c.Y + c.H ? DropSide.Bottom
                : fx < End ? DropSide.Left : fx > 1 - End ? DropSide.Right : DropSide.Middle;
            return new BarTarget(kind, side);
        }
        return null;
    }

    public static IReadOnlyList<IReadOnlyList<BuffKind>> Apply(IReadOnlyList<IReadOnlyList<BuffKind>> rows, BuffKind dragged, BarTarget target)
    {
        if (target.Anchor == dragged) return rows;
        var r = rows.Select(x => x.ToList()).ToList();
        var from = r.First(x => x.Contains(dragged));
        var home = r.First(x => x.Contains(target.Anchor));
        if (target.Side == DropSide.Middle)
            (from[from.IndexOf(dragged)], home[home.IndexOf(target.Anchor)]) = (target.Anchor, dragged);
        else
        {
            from.Remove(dragged);
            if (target.Side is DropSide.Left or DropSide.Right)
                home.Insert(home.IndexOf(target.Anchor) + (target.Side == DropSide.Right ? 1 : 0), dragged);
            else
                r.Insert(r.IndexOf(home) + (target.Side == DropSide.Bottom ? 1 : 0), [dragged]);
        }
        return [.. r.Where(x => x.Count > 0)];
    }
}
