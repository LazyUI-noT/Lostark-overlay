using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Where one 전분 crop's columns are (jeonbun-columns.plan.md): <see cref="Edges"/> are the cells' left and right
/// edges in the crop's pixels, whole pixels, first cell to last, at <see cref="Scale"/> (the screen's height / 1080). The game
/// makes every column the same width (139 px at 1080p since the 2026-11-18 patch, 117 before; any width is measured) and grows
/// the panel left from a fixed right edge; the title line, the rows and the timer at the right don't move. Follows the screen's
/// height only, not HUD 크기 or any in-game size (detection.md "Screen areas").</summary>
public sealed record JeonbunLayout(double Scale, IReadOnlyList<double> Edges)
{
    /// <summary>The cells' edges at 1080p in the reference 470 crop: four columns of ~117 px, as before the 2026-11-18 patch
    /// (139 px since, 559 wide). <see cref="Fixed"/> stretches them; the reads take their X from a crop's layout, except my
    /// row's green probe (its first 60 px) and the timer (from the last edge).</summary>
    public static readonly double[] RefEdges = [2, 118, 235, 352, 468];
    /// <summary>The panel's width at 1080p with the game's default four columns since the 2026-11-18 patch (139 px each), where
    /// a width must be guessed (decision 5).</summary>
    public const double DefaultW = 559;
    internal const double RefFirstEdge = 2;   // the first cell's left and the last's right sit this far in from the crop's edges

    // Two lines between cells are at least this far apart at 1080p: the narrowest column the game has drawn is 116 (before
    // the patch; 139 since). A dip closer than this to the last line is skipped (the same line, or a speck in the cell), so
    // a speck can't halve a column.
    const double MinColumn = 100;
    // The line's darkness (median of max(r,g,b) down the cell's text-free top and bottom strips) is at least this far under
    // both sides 2 px away. Measured on Tests/Fixtures: 26–60 on every crop's first line (26: jeonbun_15, a flash behind),
    // and no dip over 12 anywhere else in the strips; 15 keeps a margin both ways.
    const int MinContrast = 15;
    // The strips: the cell's top and bottom 4 px at 1080p, above and below the text (11–13 px high, centred in the 25 px cell;
    // every fixture's text stays out of them).
    const double StripH = 4;
    // The game offers at most 4 columns today; 8 leaves room for more without taking a cut crop's noise for columns.
    const int MaxColumns = 8;
    // Two lines measured are a whole number of columns apart within this (1080p px): the game rounds each column to whole
    // pixels (116, 117, 117, 116 in the 470), and a line is found to the pixel. The crop's right edge, set by the finder or
    // the user's 자리 맞추기 box, may sit further off: 10% of a column, as a 470 box a few pixels off has always read.
    const double LineSlack = 3, EdgeSlack = 0.1;
    // Lines the finder's width guesses allow for missing after the last one found: a flash behind or a stream's blur hides
    // some (jeonbun_15, jeonbun_2 and jeonbun_discord aren't found with two).
    const int MissedLines = 3;

    public int Count => Edges.Count - 1;

    /// <summary>The game's default four columns (<see cref="RefEdges"/>) stretched across a crop <paramref name="width"/> wide at
    /// <paramref name="scale"/>: any column width, today's 470 exactly. The fallback when a crop's lines can't be measured.</summary>
    public static JeonbunLayout Fixed(int width, double scale)
    {
        double left = RefFirstEdge * scale, right = width - RefFirstEdge * scale, span = RefEdges[^1] - RefEdges[0];
        return new(scale, [.. RefEdges.Select(e => Math.Round(left + (e - RefEdges[0]) / span * (right - left)))]);
    }

    /// <summary>The columns of <paramref name="crop"/> (the panel cut at its own width) from the dark lines between the cells of
    /// my row (<paramref name="row"/>): the narrowest gap is a column, every gap a whole number of them (a line missed under a
    /// flash is put back between its neighbours). <see cref="Fixed"/> when no line shows or the lines found aren't a column
    /// apart (a dip that is no line); null when the crop's right edge isn't a whole number of columns on (the crop cuts the
    /// panel), and a read gives nothing.</summary>
    public static JeonbunLayout? Measure(Image crop, double scale, int row)
    {
        double left = RefFirstEdge * scale, right = crop.Width - RefFirstEdge * scale;
        var lines = Lines(crop, scale, row, left, right);
        if (lines.Count == 0) return Fixed(crop.Width, scale);
        double[] anchors = [left, .. lines.Select(x => (double)x)];
        double column = anchors.Zip(anchors.Skip(1), (a, b) => b - a).Min();
        int Columns(double gap, double slack) =>
            (int)Math.Round(gap / column) is var k && k >= 1 && Math.Abs(gap - k * column) <= slack ? k : 0;
        // Each anchor's index among the edges: left is 0, each line a whole number of columns on.
        var at = new int[anchors.Length];
        for (int i = 1; i < anchors.Length; i++)
        {
            int k = Columns(anchors[i] - anchors[i - 1], LineSlack * scale);
            if (k == 0) return Fixed(crop.Width, scale);
            at[i] = at[i - 1] + k;
        }
        int last = Columns(right - anchors[^1], Math.Max(LineSlack * scale, EdgeSlack * column));
        int n = at[^1] + last;
        if (last == 0 || n > MaxColumns) return null;
        double[] xs = [.. anchors, right];
        int[] ks = [.. at, n];
        // Edges between two anchors share their gap (a missed line); every edge to the whole pixel, as the game draws it.
        var edges = new double[n + 1];
        for (int i = 1; i < xs.Length; i++)
            for (int k = ks[i - 1]; k <= ks[i]; k++)
                edges[k] = Math.Round(xs[i - 1] + (k - ks[i - 1]) * (xs[i] - xs[i - 1]) / (ks[i] - ks[i - 1]));
        return new(scale, edges);
    }

    /// <summary>The finder's guesses at the panel's width, the game's known widths first, then fewest columns first, from
    /// <paramref name="wide"/>: a crop from the panel's left as far right as the screen allows, so past the panel. The lines a
    /// column apart from the first, one after another, give the column and a count k; the panel ends near the last of them
    /// (one column before it when a scene's dip past the panel happened to fall a column on, at it when the panel's right
    /// edge is dark, up to three after when lines were missed under a flash), which only a read tells (the timer sits at the
    /// last cell's right). No line: the default and the pre-patch widths.</summary>
    internal static IEnumerable<int> Widths(Image wide, double scale, int row)
    {
        double left = RefFirstEdge * scale;
        var lines = Lines(wide, scale, row, left, wide.Width - left);
        if (lines.Count == 0) return [(int)Math.Round(DefaultW * scale), (int)Math.Round(JeonbunReader.RefW * scale)];
        double last = lines[0], column = last - left;
        int k = 1;
        // Only lines a column apart count: a gap of several columns past the panel is a scene's dip as often as a missed line,
        // and taking it would drop the panel's own width from the guesses (a missed line is covered by the guesses above k).
        // ponytail: a line missed before the first found doubles the column, and nothing reads; Measure's narrowest-gap rule
        // would fix it, but past the panel a scene's dip can be narrower than a column.
        foreach (int x in lines.Skip(1))
        {
            if (Math.Abs(x - last - column) > LineSlack * scale) break;
            (k, last) = (k + 1, x);
            column = (last - left) / k;
        }
        int fewest = Math.Max(1, k - 1), most = Math.Min(MaxColumns, k + MissedLines);
        return Enumerable.Range(fewest, most - fewest + 1)
            .Select(n => (Snapped: Snap(2 * left + n * column, scale), N: n))
            .OrderBy(c => Known(c.Snapped, scale) ? 0 : 1).ThenBy(c => c.N).Select(c => c.Snapped);
    }

    static bool Known(int width, double scale) => KnownWidths.Any(known => width == (int)Math.Round(known * scale));

    // The game's widths so far (470 before the 2026-11-18 patch, 559 since) come out a pixel off from columns drawn in whole
    // pixels (116, 117, 117, 116); a guess that close is the game's.
    static readonly double[] KnownWidths = [JeonbunReader.RefW, DefaultW];

    static int Snap(double width, double scale)
    {
        foreach (double known in KnownWidths)
            if (Math.Abs(width - known * scale) <= 2 * scale) return (int)Math.Round(known * scale);
        return (int)Math.Round(width);
    }

    /// <summary>The x of each dark line between my row's cells, left to right (none: an empty list).</summary>
    static List<int> Lines(Image crop, double scale, int row, double left, double right)
    {
        var lines = new List<int>();
        double top = (JeonbunReader.CellTop + row * JeonbunReader.CellPitch) * scale, bottom = top + JeonbunReader.CellHeight * scale;
        int strip = Math.Max(2, (int)Math.Round(StripH * scale)), side = Math.Max(2, (int)Math.Round(2 * scale));
        int y0 = (int)Math.Round(top), y1 = (int)Math.Round(bottom);
        int x0 = (int)Math.Round(left + MinColumn * scale), x1 = (int)Math.Round(right) - side;
        if (y1 > crop.Height || x1 - x0 < 2 * side) return lines;
        int[] ys = [.. Enumerable.Range(y0 + 1, strip), .. Enumerable.Range(y1 - strip, strip)];
        var dark = new int[x1 - x0 + 2 * side];   // dark[i] is x0 - side + i
        Span<int> v = stackalloc int[ys.Length];
        for (int i = 0; i < dark.Length; i++)
        {
            for (int j = 0; j < ys.Length; j++)
            {
                var (b, g, r) = crop.Pixel(x0 - side + i, ys[j]);
                v[j] = Math.Max(b, Math.Max(g, r));
            }
            v.Sort();
            dark[i] = v[v.Length / 2];
        }
        int gap = (int)Math.Round(MinColumn * scale);
        for (int i = side; i < dark.Length - side; i++)
        {
            if (Math.Min(dark[i - side], dark[i + side]) - dark[i] < MinContrast) continue;
            int best = i;   // the darkest of the line's pixels (2 at 4K)
            for (int j = i + 1; j < Math.Min(dark.Length - side, i + side); j++) if (dark[j] < dark[best]) best = j;
            lines.Add(x0 - side + best);
            i = best + gap - 1;   // the next line is a column on
        }
        return lines;
    }
}
