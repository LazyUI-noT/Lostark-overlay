namespace GaugeOverlay.Core.Domain;

/// <summary>A column of 내 스킬창 in 칸 맞춤 (slot-layout.plan.md §2): a <see cref="SlotStack"/> or a <see cref="KeyRun"/>.</summary>
public abstract record SlotColumn;

/// <summary>Blocks sharing the column's height, top to bottom (각성기 carries its 변화 strip on top).</summary>
public sealed record SlotStack(IReadOnlyList<SlotId> Blocks) : SlotColumn;

/// <summary>Keys in two rows, each its own order; position i of both rows is column i.</summary>
public sealed record KeyRun(IReadOnlyList<SlotId> Top, IReadOnlyList<SlotId> Bottom) : SlotColumn;

/// <summary>One placed box: <paramref name="Hidden"/> = 표시 off with 빈 칸 줄이기 off (its spot is kept, nothing drawn).</summary>
public readonly record struct PlacedSlot(SlotId Slot, Cell Cell, bool Hidden);

/// <summary>The boxes, from (0, 0) at the grid's top left; <paramref name="Width"/> is the right edge (0 = nothing placed).</summary>
public sealed record PlacedSlots(IReadOnlyList<PlacedSlot> Boxes, double Width);

/// <summary>칸 맞춤's skill grid (slot-layout.plan.md §1): an arrangement of columns, what is shown and 빈 칸 줄이기 → where
/// each box is drawn. The game slot a box reads never changes, only where it is drawn.</summary>
public static class SlotGrid
{
    /// <summary>A box that left a key row between two others leaves this many gaps, so columns that no longer line up
    /// don't look as if they did.</summary>
    public const double WideGap = 3;

    /// <summary>각성 | 초각 | QWER / ASDF | 아1 / 아2.</summary>
    public static readonly IReadOnlyList<SlotColumn> Default =
    [
        new SlotStack([SlotId.각성기]),
        new SlotStack([SlotId.초각스]),
        new KeyRun([SlotId.일반1, SlotId.일반2, SlotId.일반3, SlotId.일반4], [SlotId.일반5, SlotId.일반6, SlotId.일반7, SlotId.일반8]),
        new SlotStack([SlotId.아덴1, SlotId.아덴2]),
    ];

    /// <param name="present">The box exists for this player (its group is shown, the class has the slot): absent boxes
    /// never take a spot.</param>
    /// <param name="hidden">Turned off in its settings: closed up with closeGaps, else kept as an empty spot.</param>
    public static PlacedSlots Arrange(IReadOnlyList<SlotColumn> columns, Func<SlotId, bool> present, IReadOnlySet<SlotId> hidden,
        bool closeGaps, double bw, double bh, double gap)
    {
        bool Gone(SlotId s) => !present(s) || closeGaps && hidden.Contains(s);
        double colH = 2 * bh + gap, x = 0;
        var boxes = new List<PlacedSlot>();
        foreach (var column in columns)
        {
            double w = column switch
            {
                SlotStack stack => Stack(stack, Gone, hidden, x, bw, colH, gap, boxes),
                KeyRun run => Run(run, Gone, hidden, x, bw, bh, gap, boxes),
                _ => 0,
            };
            if (w > 0) x += w + gap;
        }
        return new PlacedSlots(boxes, x > 0 ? x - gap : 0);
    }

    static double Stack(SlotStack stack, Func<SlotId, bool> gone, IReadOnlySet<SlotId> hidden, double x, double bw, double colH,
        double gap, List<PlacedSlot> boxes)
    {
        var shown = stack.Blocks.Where(b => !gone(b)).ToList();
        if (shown.Count == 0) return 0;
        double h = Math.Round((colH - (shown.Count - 1) * gap) / shown.Count);
        for (int i = 0; i < shown.Count; i++)
        {
            double y = i * (h + gap);
            boxes.Add(new(shown[i], new Cell(x, y, bw, i == shown.Count - 1 ? colH - y : h), hidden.Contains(shown[i])));
        }
        return bw;
    }

    static double Run(KeyRun run, Func<SlotId, bool> gone, IReadOnlySet<SlotId> hidden, double x, double bw, double bh, double gap,
        List<PlacedSlot> boxes)
    {
        // A column empty in both rows goes with no mark; then each row keeps its order, a wide gap where a box left.
        int n = Math.Max(run.Top.Count, run.Bottom.Count);
        bool Kept(IReadOnlyList<SlotId> row, int i) => i < row.Count && !gone(row[i]);
        var columns = Enumerable.Range(0, n).Where(i => Kept(run.Top, i) || Kept(run.Bottom, i)).ToList();
        var rows = new[] { run.Top, run.Bottom }.Select(row => RowPieces(row, columns, i => Kept(row, i), gap)).ToList();
        if (rows.All(r => r.Count == 0)) return 0;
        double Natural(List<(SlotId Slot, double GapBefore)> r) => r.Count * bw + r.Sum(p => p.GapBefore);
        double width = rows.Max(Natural);
        bool one = rows.Count(r => r.Count > 0) == 1;   // one row left: it fills the column height
        for (int k = 0; k < rows.Count; k++)
        {
            var row = rows[k];
            if (row.Count == 0) continue;
            double boxW = (width - row.Sum(p => p.GapBefore)) / row.Count, at = x;
            double y = one ? 0 : k * (bh + gap), h = one ? 2 * bh + gap : bh;
            foreach (var (slot, gapBefore) in row)
            {
                at += gapBefore;
                double left = Math.Round(at), right = Math.Round(at + boxW);
                boxes.Add(new(slot, new Cell(left, y, right - left, h), hidden.Contains(slot)));
                at += boxW;
            }
        }
        return width;
    }

    /// <summary>A row's kept boxes in order, each with the gap before it: none for the first, one, or
    /// <see cref="WideGap"/> where a box of a kept column left between two others.</summary>
    static List<(SlotId Slot, double GapBefore)> RowPieces(IReadOnlyList<SlotId> row, List<int> columns, Func<int, bool> kept, double gap)
    {
        var pieces = new List<(SlotId, double)>();
        bool missed = false;
        foreach (int i in columns)
        {
            if (!kept(i)) { missed = pieces.Count > 0; continue; }
            pieces.Add((row[i], pieces.Count == 0 ? 0 : missed ? WideGap * gap : gap));
            missed = false;
        }
        return pieces;
    }
}

/// <summary>The buff bars on the plate in 칸 맞춤 (slot-layout.plan.md §1): rows of bars sharing the row's width.</summary>
public static class BarRows
{
    /// <summary>공증 낙인 / 아덴 초각 / 뎀증.</summary>
    public static readonly IReadOnlyList<IReadOnlyList<BuffKind>> Default =
        [[BuffKind.공증, BuffKind.낙인], [BuffKind.아덴피증, BuffKind.초각스피증], [BuffKind.뎀증]];

    /// <summary>Each shown bar's spot from (0, 0), rows <paramref name="width"/> wide. With <paramref name="closeGaps"/> a row's
    /// shown bars share its width and an empty row closes up; off, today's flow: shown bars two to a row, each half wide.</summary>
    /// <param name="height">A bar's thickness (DIP); a row is as tall as its thickest bar.</param>
    /// <param name="length">A bar's length, share of its spot's width.</param>
    /// <returns>Each bar's cell, and <c>Full</c>: its spot's width, the bar at length 1.</returns>
    public static IReadOnlyList<(BuffKind Kind, Cell Cell, double Full)> Arrange(IReadOnlyList<IReadOnlyList<BuffKind>> rows, Func<BuffKind, bool> shown,
        bool closeGaps, double width, double gap, Func<BuffKind, double> height, Func<BuffKind, double> length)
    {
        var lines = closeGaps
            ? rows.Select(r => r.Where(shown).ToList()).Where(r => r.Count > 0).ToList()
            : rows.SelectMany(r => r).Where(shown).Chunk(2).Select(r => r.ToList()).ToList();
        var bars = new List<(BuffKind, Cell, double)>();
        double y = 0;
        foreach (var line in lines)
        {
            int share = closeGaps ? line.Count : 2;
            double spot = (width - (share - 1) * gap) / share;
            for (int j = 0; j < line.Count; j++)
                bars.Add((line[j], new Cell(j * (spot + gap), y, Math.Max(2, Math.Round(spot * length(line[j]))), Math.Max(2, Math.Round(height(line[j])))), spot));
            y += line.Max(k => Math.Max(2, Math.Round(height(k)))) + gap;
        }
        return bars;
    }
}
