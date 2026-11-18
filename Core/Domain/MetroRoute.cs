namespace GaugeOverlay.Core.Domain;

/// <summary>A point on the 기능 맵, in map pixels.</summary>
public readonly record struct MetroPoint(double X, double Y);

/// <summary>Where a box stands; <paramref name="Outer"/>: the open box it sits in (it moves with it), null for a top box.</summary>
public sealed record MetroRect(double X, double Y, double Width, double Height, int Column, string? Outer = null)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Top => Outer is null;
}

/// <summary>A top box to place; <paramref name="Row"/> boxes stand in the row under the columns (파티, 파티원 창).</summary>
public sealed record MetroBox(string Id, int Column, double Height, bool Row = false);

/// <summary>A shown box inside the open box <paramref name="Outer"/>, at (<paramref name="Dx"/>, <paramref name="Dy"/>) from its top-left.</summary>
public sealed record MetroInner(string Id, string Outer, double Dx, double Dy, double Width, double Height);

/// <summary>A line between two shown boxes, left to right or within a column; <paramref name="Optional"/>: dashed.</summary>
public sealed record MetroLink(string From, string To, bool Optional = false);

/// <summary>A routed line: its corners in order, from the source's side to the target's.</summary>
public sealed record MetroLine(MetroLink Link, IReadOnlyList<MetroPoint> Points);

/// <summary>The map placed and routed: each shown box's place, the lines, the columns' lefts and the channels' widths (the gap
/// right of each column), the size, and the height of the dashed line above the row.</summary>
public sealed record MetroLayout(IReadOnlyDictionary<string, MetroRect> At, IReadOnlyList<MetroLine> Lines, IReadOnlyList<double> ColumnLeft,
    IReadOnlyList<double> ChannelWidths, double Width, double Height, double Separator);

/// <summary>The 기능 맵 as a metro map (metro-map.plan.md): lines run only across, 45° and down; each vertical run has its own
/// lane in the channel right of a column; a line crossing a column goes through a gap between its boxes (a street), which
/// widens for it. The column with the most boxes stacks from the top; the others sit each box as near the height of the boxes
/// it links to as the gaps allow. Two boxes in one column with nothing between are linked straight down.</summary>
public static class MetroRoute
{
    /// <summary>A box's head band (the folded height): lines meet a box within it.</summary>
    public const double Head = 34;
    /// <summary>Between lanes and between lines in a street.</summary>
    public const double Pitch = 5;
    /// <summary>A channel's lanes keep this far from the boxes either side.</summary>
    public const double Margin = 10;
    /// <summary>The least gap between two boxes in a column, and a channel's width with no lane in it.</summary>
    public const double Gap = 12, BaseChannel = 36, LastChannel = 16;
    /// <summary>From the lowest box to the row under the columns; the dashed line sits halfway.</summary>
    public const double RowGap = 30;
    /// <summary>A street's room above its top box and below its bottom one, when it is the first or last gap of a column.</summary>
    const double StreetAbove = 80, StreetBelow = 400, StreetEdge = 6, StreetPad = 8, StreetEnd = 12;
    /// <summary>Place, route, widen the channels and streets for the lines, place again: settles in a few rounds.</summary>
    const int Rounds = 4, Sweeps = 4;
    /// <summary>A street's height pulls towards the source; the target pulls this much.</summary>
    const double TargetPull = 0.3;
    const double Flat = 0.5;

    /// <summary>The map's lines between shown boxes: each edge drawn to the box shown for its ends (the outer box while it is
    /// folded), party lines left out (파티 is drawn apart), one line per pair (dashed only when every edge in it is).</summary>
    public static IReadOnlyList<(MetroLink Link, IReadOnlyList<MapEdge> Edges)> Links(IEnumerable<MapEdge> edges, Func<string, string?> shown)
    {
        var lines = new List<(MetroLink Link, List<MapEdge> Edges)>();
        foreach (var e in edges)
        {
            if (e.Party || shown(e.From) is not { } from || shown(e.To) is not { } to || from == to) continue;
            int at = lines.FindIndex(l => l.Link.From == from && l.Link.To == to);
            if (at < 0) lines.Add((new MetroLink(from, to, e.Optional), [e]));
            else
            {
                lines[at].Edges.Add(e);
                lines[at] = (lines[at].Link with { Optional = lines[at].Link.Optional && e.Optional }, lines[at].Edges);
            }
        }
        return [.. lines.Select(l => (l.Link, (IReadOnlyList<MapEdge>)l.Edges))];
    }

    public static MetroLayout Layout(IReadOnlyList<double> widths, IReadOnlyList<MetroBox> boxes, IReadOnlyList<MetroInner> inner, IReadOnlyList<MetroLink> links)
    {
        int n = widths.Count;
        var slot = new Dictionary<(int, int), int>();
        var channels = Enumerable.Range(0, n).Select(c => c == n - 1 ? LastChannel : BaseChannel).ToArray();
        Placed placed = null!;
        Routed routed = null!;
        for (int round = 0; round < Rounds; round++)
        {
            placed = Place(widths, boxes, inner, links, slot, channels);
            routed = Route(placed, widths, links);
            (slot, channels) = (routed.Streets, routed.Channels);
        }
        return new(placed.At, routed.Lines, placed.Left, placed.Channels, placed.Width, placed.Height, placed.Separator);
    }

    sealed record Placed(Dictionary<string, MetroRect> At, double[] Left, double[] Channels, List<List<MetroBox>> Order,
        Dictionary<(int, int), (double Lo, double Hi)> Slots, HashSet<string> Row, double Width, double Height, double Separator);

    sealed record Routed(List<MetroLine> Lines, Dictionary<(int, int), int> Streets, double[] Channels);

    static double Mid(MetroRect r) => r.Y + Head / 2;

    static Placed Place(IReadOnlyList<double> widths, IReadOnlyList<MetroBox> boxes, IReadOnlyList<MetroInner> inner,
        IReadOnlyList<MetroLink> links, Dictionary<(int, int), int> slot, double[] channels)
    {
        int n = widths.Count;
        var at = new Dictionary<string, MetroRect>();
        var left = new double[n];
        double x = 0;
        for (int c = 0; c < n; c++) { left[c] = x; x += widths[c] + channels[c]; }
        var order = Enumerable.Range(0, n).Select(c => boxes.Where(b => b.Column == c && !b.Row).ToList()).ToList();
        var row = boxes.Where(b => b.Row).Select(b => b.Id).ToHashSet();
        var innerOf = inner.ToLookup(i => i.Outer);

        void Put(MetroBox b, double y)
        {
            at[b.Id] = new(left[b.Column], y, widths[b.Column], b.Height, b.Column);
            foreach (var k in innerOf[b.Id]) at[k.Id] = new(left[b.Column] + k.Dx, y + k.Dy, k.Width, k.Height, b.Column, b.Id);
        }
        double GapBefore(int c, int k) => (k == 0 ? 0 : Gap) + (slot.GetValueOrDefault((c, k)) is var s and > 0 ? s * Pitch + StreetPad : 0);

        for (int c = 0; c < n; c++)
        {
            double y = 0;
            for (int k = 0; k < order[c].Count; k++)
            {
                y += GapBefore(c, k);
                Put(order[c][k], y);
                y += order[c][k].Height;
            }
        }
        int anchor = Enumerable.Range(0, n).MaxBy(c => order[c].Count);   // the first of the longest
        var free = Enumerable.Range(anchor + 1, n - anchor - 1).Concat(Enumerable.Range(0, anchor).Reverse()).ToList();
        for (int sweep = 0; sweep < Sweeps; sweep++)
            foreach (int c in free) Arrange(c);

        // Each box as near the mean height of the boxes it links to in other columns, keeping the order and every gap: the
        // isotonic fit (pool adjacent violators) of the wanted tops.
        void Arrange(int c)
        {
            var want = new Dictionary<MetroBox, double>();
            foreach (var b in order[c])
            {
                var ys = links.Select(l => l.From == b.Id ? l.To : l.To == b.Id ? l.From : null)
                    .Where(o => o is not null && o != b.Id && !row.Contains(o) && at.TryGetValue(o, out var r) && r.Column != c)
                    .Select(o => Mid(at[o!])).ToList();
                want[b] = ys.Count > 0 ? ys.Average() : Mid(at[b.Id]);
            }
            var list = order[c] = [.. order[c].OrderBy(b => want[b])];
            var cum = new double[list.Count];
            var blocks = new List<Block>();
            for (int k = 0; k < list.Count; k++)
            {
                if (k > 0) cum[k] = cum[k - 1] + list[k - 1].Height + GapBefore(c, k);
                blocks.Add(new Block(want[list[k]] - Head / 2 - cum[k], k));
                while (blocks.Count > 1 && blocks[^1].Mean < blocks[^2].Mean)
                {
                    blocks[^2].Take(blocks[^1]);
                    blocks.RemoveAt(blocks.Count - 1);
                }
            }
            double least = GapBefore(c, 0);
            foreach (var block in blocks)
                foreach (int k in block.Items) Put(list[k], Math.Max(least, block.Mean) + cum[k]);
        }

        var slots = new Dictionary<(int, int), (double Lo, double Hi)>();
        double bottom = 0;
        for (int c = 0; c < n; c++)
        {
            var list = order[c];
            for (int k = 0; k <= list.Count; k++)
            {
                var prev = k > 0 ? at[list[k - 1].Id] : null;
                var next = k < list.Count ? at[list[k].Id] : null;
                slots[(c, k)] = (prev is not null ? prev.Bottom + StreetEdge : (next?.Y ?? 0) - StreetAbove,
                    next is not null ? next.Y - StreetEdge : (prev?.Bottom ?? 0) + StreetBelow);
            }
            if (list.Count > 0)
            {
                int s = slot.GetValueOrDefault((c, list.Count));
                bottom = Math.Max(bottom, at[list[^1].Id].Bottom + (s > 0 ? s * Pitch + StreetEnd : 0));
            }
        }
        double rowY = bottom + RowGap, end = bottom;
        foreach (var b in boxes.Where(b => b.Row))
        {
            Put(b, rowY);
            end = Math.Max(end, rowY + b.Height);
        }
        return new(at, left, channels, order, slots, row, x, end, bottom + RowGap / 2);
    }

    sealed class Block(double sum, int item)
    {
        double _sum = sum;
        public List<int> Items { get; } = [item];
        public double Mean => _sum / Items.Count;

        public void Take(Block other)
        {
            _sum += other._sum;
            Items.AddRange(other.Items);
        }
    }

    sealed class Edge(MetroLink link, MetroRect a, MetroRect b)
    {
        public MetroLink Link { get; } = link;
        public MetroRect A { get; } = a;
        public MetroRect B { get; } = b;
        public List<Street> Streets { get; } = [];
        public List<Hop> Hops { get; } = [];
        public double Ya { get; set; }
        public double Yb { get; set; }
        public List<MetroPoint>? Direct { get; set; }
    }

    sealed class Street(int column, int gap, double y)
    {
        public int Column { get; } = column;
        public int Gap { get; } = gap;
        public double Y { get; set; } = y;
    }

    sealed class Hop(int column, double y0, double y1, bool same)
    {
        public int Column { get; } = column;
        public double Y0 { get; } = y0;
        public double Y1 { get; } = y1;
        public bool Same { get; } = same;
        public int Lane { get; set; }
        public double Low => Math.Min(Y0, Y1);
        public double High => Math.Max(Y0, Y1);
        public bool Runs => Math.Abs(Y0 - Y1) > Flat;
    }

    static Routed Route(Placed p, IReadOnlyList<double> widths, IReadOnlyList<MetroLink> links)
    {
        int n = widths.Count;
        var es = links.Where(l => p.At.ContainsKey(l.From) && p.At.ContainsKey(l.To)).Select(l => new Edge(l, p.At[l.From], p.At[l.To])).ToList();
        var streets = new Dictionary<(int, int), int>();
        // streets: the gap in each crossed column nearest the source's height (the target pulls a little)
        foreach (var e in es)
            for (int m = e.A.Column + 1; m < e.B.Column; m++)
            {
                (int K, double D, double Y)? best = null;
                for (int k = 0; k <= p.Order[m].Count; k++)
                {
                    var (lo, hi) = p.Slots[(m, k)];
                    double y = Math.Min(Math.Max(Mid(e.A), lo), Math.Max(lo, hi));
                    double d = Math.Abs(y - Mid(e.A)) + TargetPull * Math.Abs(y - Mid(e.B));
                    if (best is null || d < best.Value.D) best = (k, d, y);
                }
                e.Streets.Add(new Street(m, best!.Value.K, best.Value.Y));
                streets[(m, best.Value.K)] = streets.GetValueOrDefault((m, best.Value.K)) + 1;
            }
        // a street's lines side by side in the order of their sources' heights, as near their own height as the gap allows
        foreach (var group in es.SelectMany(e => e.Streets.Select(s => (e, s))).GroupBy(x => (x.s.Column, x.s.Gap)))
        {
            var list = group.OrderBy(x => Mid(x.e.A)).ToList();
            var (lo, hi) = p.Slots[group.Key];
            double span = (list.Count - 1) * Pitch, ideal = list.Average(x => Mid(x.e.A)) - span / 2;
            double top = Math.Max(lo, Math.Min(ideal, hi - span));
            for (int i = 0; i < list.Count; i++) list[i].s.Y = top + i * Pitch;
        }
        // one column, nothing between: straight from the middle of one box's frame to the other's
        foreach (var e in es.Where(e => e.A.Column == e.B.Column))
        {
            var (upper, lower) = e.A.Y < e.B.Y ? (e.A, e.B) : (e.B, e.A);
            bool between = p.At.Any(kv => kv.Key != e.Link.From && kv.Key != e.Link.To && kv.Value.Top && kv.Value.Column == e.A.Column
                && !p.Row.Contains(kv.Key) && kv.Value.Y >= upper.Bottom && kv.Value.Bottom <= lower.Y);
            if (between) continue;
            double x = e.A.X + e.A.Width / 2;
            e.Direct = e.A.Y < e.B.Y ? [new(x, e.A.Bottom), new(x, e.B.Y)] : [new(x, e.A.Y), new(x, e.B.Bottom)];
        }
        Ports(es);
        // hops: each vertical run, in the channel right of its column; lanes per channel
        var hops = Enumerable.Range(0, n).Select(_ => new List<Hop>()).ToList();
        foreach (var e in es.Where(e => e.Direct is null))
        {
            if (e.A.Column == e.B.Column) e.Hops.Add(new Hop(e.A.Column, e.Ya, e.Yb, true));
            else
            {
                double[] ys = [e.Ya, .. e.Streets.Select(s => s.Y), e.Yb];
                for (int k = 0; k < ys.Length - 1; k++) e.Hops.Add(new Hop(e.A.Column + k, ys[k], ys[k + 1], false));
            }
            foreach (var h in e.Hops) hops[h.Column].Add(h);
        }
        var channels = new double[n];
        for (int c = 0; c < n; c++)
        {
            int lanes = Lanes([.. hops[c].Where(h => h.Runs)]);
            channels[c] = Math.Max(c == n - 1 ? (lanes > 0 ? LastChannel : 0) : BaseChannel, lanes > 0 ? 2 * Margin + (lanes - 1) * Pitch : 0);
        }
        var lines = es.Select(e => new MetroLine(e.Link, e.Direct ?? Points(e, p, widths))).ToList();
        return new(lines, streets, channels);
    }

    /// <summary>Where each line meets a box side: spread over the head band in the order of where the line heads next, so lines
    /// on one side don't cross at the box.</summary>
    static void Ports(List<Edge> es)
    {
        var sides = new Dictionary<(string, bool Right), List<(Edge E, bool Start, double Toward)>>();
        void Add(string id, bool right, Edge e, bool start, double toward) =>
            (sides.TryGetValue((id, right), out var l) ? l : sides[(id, right)] = []).Add((e, start, toward));
        foreach (var e in es.Where(e => e.Direct is null))
        {
            bool same = e.A.Column == e.B.Column;
            Add(e.Link.From, true, e, true, e.Streets.Count > 0 ? e.Streets[0].Y : Mid(e.B));
            Add(e.Link.To, same, e, false, same ? Mid(e.A) : e.Streets.Count > 0 ? e.Streets[^1].Y : Mid(e.A));
        }
        foreach (var list in sides.Values)
        {
            var sorted = list.OrderBy(x => x.Toward).ToList();
            var box = sorted[0].Start ? sorted[0].E.A : sorted[0].E.B;
            double band = Head - 8, step = sorted.Count > 1 ? Math.Min(Pitch + 1, band / (sorted.Count - 1)) : 0;
            double first = Mid(box) - step * (sorted.Count - 1) / 2;
            for (int i = 0; i < sorted.Count; i++)
                if (sorted[i].Start) sorted[i].E.Ya = first + i * step;
                else sorted[i].E.Yb = first + i * step;
        }
    }

    static List<MetroPoint> Points(Edge e, Placed p, IReadOnlyList<double> widths)
    {
        var pts = new List<MetroPoint> { new(e.A.Right, e.Ya) };
        foreach (var h in e.Hops.Where(h => h.Runs))
        {
            double left = p.Left[h.Column] + widths[h.Column];
            double x = h.Same ? left + Margin + h.Lane * Pitch : left + p.Channels[h.Column] - Margin - h.Lane * Pitch;
            pts.Add(new(x, h.Y0));
            pts.Add(new(x, h.Y1));
        }
        pts.Add(new(e.A.Column == e.B.Column ? e.B.Right : e.B.X, e.Yb));
        // a run too short to draw is left out: the line goes on at its height (under half a pixel off at the next station)
        for (int i = 1; i < pts.Count; i++)
            if (Math.Abs(pts[i].X - pts[i - 1].X) > Flat && Math.Abs(pts[i].Y - pts[i - 1].Y) <= Flat) pts[i] = pts[i] with { Y = pts[i - 1].Y };
        return pts;
    }

    /// <summary>Lane 0 is nearest the boxes the lines go to (rightmost; leftmost for a loop in one column). For two hops whose
    /// spans overlap, one must run right of the other: a hop entering where the other leaves goes left of it (else their stubs
    /// share a segment), otherwise the order with fewer crossings. A hop's lane is one past the highest lane right of it.</summary>
    static int Lanes(List<Hop> hs)
    {
        var right = hs.ToDictionary(h => h, _ => new List<Hop>());
        static bool Inside(double y, Hop h) => y > h.Low && y < h.High;
        static int Crossings(Hop r, Hop l) => (Inside(r.Y0, l) ? 1 : 0) + (Inside(l.Y1, r) ? 1 : 0);
        for (int i = 0; i < hs.Count; i++)
            for (int j = i + 1; j < hs.Count; j++)
            {
                Hop a = hs[i], b = hs[j];
                if (!(a.Low - Pitch < b.High + Pitch && b.Low - Pitch < a.High + Pitch)) continue;
                bool aRight = a.Same ? Math.Abs(a.Y1 - a.Y0) < Math.Abs(b.Y1 - b.Y0)   // loops: the shorter inside
                    : Math.Abs(a.Y0 - b.Y1) < Pitch ? false
                    : Math.Abs(b.Y0 - a.Y1) < Pitch || Crossings(a, b) <= Crossings(b, a);
                (aRight ? right[b] : right[a]).Add(aRight ? a : b);
            }
        var lane = new Dictionary<Hop, int>();
        var busy = new HashSet<Hop>();
        int Of(Hop h)
        {
            if (lane.TryGetValue(h, out int known)) return known;
            if (!busy.Add(h)) return 0;   // a cycle: break it here
            int k = right[h].Count > 0 ? 1 + right[h].Max(Of) : 0;
            busy.Remove(h);
            return lane[h] = k;
        }
        foreach (var h in hs) h.Lane = Of(h);
        return hs.Count > 0 ? 1 + hs.Max(h => h.Lane) : 0;
    }

    /// <summary>The line's corners cut at 45°, <paramref name="corner"/> px back along each side (less on a short side).</summary>
    public static IReadOnlyList<MetroPoint> Chamfer(IReadOnlyList<MetroPoint> points, double corner)
    {
        var p = points.Where((q, i) => i == 0 || Distance(q, points[i - 1]) > 0.3).ToList();
        var result = new List<MetroPoint> { p[0] };
        for (int i = 1; i < p.Count - 1; i++)
        {
            MetroPoint a = p[i - 1], at = p[i], b = p[i + 1];
            double la = Distance(a, at), lb = Distance(b, at), r = Math.Min(corner, Math.Min(la / 2, lb / 2));
            result.Add(new(at.X + (a.X - at.X) / la * r, at.Y + (a.Y - at.Y) / la * r));
            result.Add(new(at.X + (b.X - at.X) / lb * r, at.Y + (b.Y - at.Y) / lb * r));
        }
        if (p.Count > 1) result.Add(p[^1]);
        return result;
    }

    /// <summary>A tag's gap from its line, and how far it slides along a stretch per try.</summary>
    const double TagLift = 2, TagStep = 4;

    /// <summary>The top-left of a line's words (<paramref name="width"/> × <paramref name="height"/>): on a level stretch of
    /// <paramref name="corners"/>, longest first, above the line or else below it, at the middle or slid along it, overlapping
    /// nothing in <paramref name="taken"/> (boxes and tags placed before). Above the middle of a stretch through a street, the
    /// words sat on the box over it ("사용 기록" on 스킬창 표시, ui-audit-2026-11-18). No free spot: above the longest middle.</summary>
    public static MetroPoint TagSpot(IReadOnlyList<MetroPoint> corners, double width, double height, IReadOnlyList<MetroRect> taken)
    {
        if (corners.Count == 0) throw new ArgumentException("A line needs at least one point.", nameof(corners));
        // The centre stays on the stretch; a tag wider than it overhangs. No level stretch (vertical line, one point): the
        // tag hangs off the first point's Y.
        var level = corners.Zip(corners.Skip(1)).Where(s => Math.Abs(s.First.Y - s.Second.Y) < Flat)
            .Select(s => (Left: Math.Min(s.First.X, s.Second.X), Right: Math.Max(s.First.X, s.Second.X), s.First.Y))
            .OrderByDescending(s => s.Right - s.Left).ToList();
        if (level.Count == 0) level.Add((Math.Min(corners[0].X, corners[^1].X), Math.Max(corners[0].X, corners[^1].X), corners[0].Y));
        bool Free(double x, double y) => !taken.Any(r => x < r.Right + TagLift && x + width > r.X - TagLift && y < r.Bottom + TagLift && y + height > r.Y - TagLift);
        foreach (var (left, right, y) in level)
        {
            double middle = (left + right) / 2;
            for (double slide = 0; slide <= (right - left) / 2; slide += TagStep)
                foreach (double centre in slide == 0 ? [middle] : new[] { middle + slide, middle - slide })
                    foreach (double top in new[] { y - height - TagLift, y + TagLift })
                        if (Free(centre - width / 2, top)) return new(centre - width / 2, top);
        }
        var (l, r, ly) = level[0];
        return new((l + r) / 2 - width / 2, ly - height - TagLift);
    }

    static double Distance(MetroPoint a, MetroPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The layout's faults: lines through a box not their own, pairs of lines sharing a stretch, and crossings.</summary>
    public static (int Through, int Shared, int Crossings) Check(MetroLayout layout)
    {
        int through = 0, shared = 0, cross = 0;
        var segs = layout.Lines.SelectMany((l, i) => l.Points.Zip(l.Points.Skip(1)).Select(s => (A: s.First, B: s.Second, Line: i))).ToList();
        foreach (var s in segs)
        {
            var link = layout.Lines[s.Line].Link;
            var own = new HashSet<string?> { link.From, link.To, layout.At[link.From].Outer, layout.At[link.To].Outer };
            int steps = (int)Math.Ceiling(Distance(s.A, s.B) / 2);
            for (int k = 1; k < steps; k++)
            {
                double x = s.A.X + (s.B.X - s.A.X) * k / steps, y = s.A.Y + (s.B.Y - s.A.Y) * k / steps;
                if (layout.At.Any(kv => !own.Contains(kv.Key) && x > kv.Value.X + 1 && x < kv.Value.Right - 1 && y > kv.Value.Y + 1 && y < kv.Value.Bottom - 1)) { through++; break; }
            }
        }
        for (int i = 0; i < segs.Count; i++)
            for (int j = i + 1; j < segs.Count; j++)
            {
                if (segs[i].Line == segs[j].Line) continue;
                var (a, b, c, d) = (segs[i].A, segs[i].B, segs[j].A, segs[j].B);
                bool hA = Math.Abs(a.Y - b.Y) < Flat, hB = Math.Abs(c.Y - d.Y) < Flat, vA = Math.Abs(a.X - b.X) < Flat, vB = Math.Abs(c.X - d.X) < Flat;
                if (hA && hB && Math.Abs(a.Y - c.Y) < 1 && Overlap(a.X, b.X, c.X, d.X) > 2) shared++;
                else if (vA && vB && Math.Abs(a.X - c.X) < 1 && Overlap(a.Y, b.Y, c.Y, d.Y) > 2) shared++;
                else if (hA && vB || vA && hB)
                {
                    var (h1, h2, v1, v2) = hA ? (a, b, c, d) : (c, d, a, b);
                    if (v1.X > Math.Min(h1.X, h2.X) + 1 && v1.X < Math.Max(h1.X, h2.X) - 1 && h1.Y > Math.Min(v1.Y, v2.Y) + 1 && h1.Y < Math.Max(v1.Y, v2.Y) - 1) cross++;
                }
            }
        return (through, shared, cross);
    }

    static double Overlap(double a0, double a1, double b0, double b1) =>
        Math.Min(Math.Max(a0, a1), Math.Max(b0, b1)) - Math.Max(Math.Min(a0, a1), Math.Min(b0, b1));
}
