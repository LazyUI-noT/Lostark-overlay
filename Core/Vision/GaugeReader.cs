using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>A gauge described by its own pixels (Profiles/aden/README.md): the pixels that differ between the empty
/// and full screenshots, each with its filled colour, the connected piece it belongs to, and its place in the fill
/// order (0..1). <see cref="Filling"/>: colours (0xRRGGBB) that also count as filled at any pixel, such as the gold of a
/// 바드 crystal that is still filling. <see cref="Slack"/>: how many pixels away, across the fill direction
/// (<see cref="AcrossX"/>, <see cref="AcrossY"/>; none for rings), a pixel may match its own filled colour, for a gauge
/// thin enough that the fit's error of a few pixels would miss it. <see cref="At"/>: for each position, its index in
/// <see cref="Pixels"/>, or -1 off the gauge.</summary>
public sealed record GaugeShape(int Width, int Height, int[] Pixels, int[] FilledBgr, int[] Piece, int Pieces, float[] Order)
{
    public int[] At { get; init; } = [];
    public int[] Filling { get; init; } = [];
    public int Slack { get; init; }
    public int AcrossX { get; init; }
    public int AcrossY { get; init; }
}

/// <summary>One reader for every gauge shape: bars and arcs fill along an order, orbs and stacks are counted.</summary>
public static class GaugeReader
{
    private const int Bins = 100;

    /// <summary>The gauge pixels: those where <paramref name="empty"/> and <paramref name="full"/> differ by more than
    /// <paramref name="threshold"/> on some channel. Pieces are 8-connected groups. The order starts as left→right.</summary>
    public static GaugeShape Build(Image empty, Image full, int threshold = 40) => FromMask(Mask(empty, full, threshold));

    /// <summary>What --build-gauge saves as mask.png: <paramref name="full"/>'s colour, opaque, where it differs from
    /// <paramref name="empty"/> by more than <paramref name="threshold"/> on some channel; transparent elsewhere.</summary>
    public static Image Mask(Image empty, Image full, int threshold = 40)
    {
        var mask = new Image(full.Width, full.Height);
        for (int y = 0; y < full.Height; y++)
            for (int x = 0; x < full.Width; x++)
            {
                int i = y * full.Stride + x * 4, j = y * empty.Stride + x * 4;
                if (Math.Abs(full.Bgra[i] - empty.Bgra[j]) <= threshold && Math.Abs(full.Bgra[i + 1] - empty.Bgra[j + 1]) <= threshold
                    && Math.Abs(full.Bgra[i + 2] - empty.Bgra[j + 2]) <= threshold) continue;
                (mask.Bgra[i], mask.Bgra[i + 1], mask.Bgra[i + 2], mask.Bgra[i + 3]) = (full.Bgra[i], full.Bgra[i + 1], full.Bgra[i + 2], 255);
            }
        return mask;
    }

    /// <summary>The gauge in a mask (<see cref="Mask"/>): its opaque pixels with their filled colours.</summary>
    public static GaugeShape FromMask(Image mask)
    {
        int w = mask.Width, h = mask.Height;
        var index = new int[w * h];
        Array.Fill(index, -1);
        var pixels = new List<int>();
        var colours = new List<int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * mask.Stride + x * 4;
                if (mask.Bgra[i + 3] < 128) continue;
                index[y * w + x] = pixels.Count;
                pixels.Add(y * w + x);
                colours.Add(mask.Bgra[i] | mask.Bgra[i + 1] << 8 | mask.Bgra[i + 2] << 16);
            }

        var piece = new int[pixels.Count];
        Array.Fill(piece, -1);
        int pieces = 0;
        var stack = new Stack<int>();
        for (int k = 0; k < pixels.Count; k++)
        {
            if (piece[k] >= 0) continue;
            piece[k] = pieces;
            stack.Push(k);
            while (stack.Count > 0)
            {
                int p = pixels[stack.Pop()], px = p % w, py = p / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = px + dx, ny = py + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int n = index[ny * w + nx];
                        if (n >= 0 && piece[n] < 0) { piece[n] = pieces; stack.Push(n); }
                    }
            }
            pieces++;
        }
        var shape = new GaugeShape(w, h, pixels.ToArray(), colours.ToArray(), piece, pieces, new float[pixels.Count]) { At = index };
        return Linear(shape, 0);
    }

    /// <summary>Fill order along a direction: 0° = left→right, 90° = bottom→top, 180° = right→left.</summary>
    public static GaugeShape Linear(GaugeShape s, double angleDeg)
    {
        double a = angleDeg * Math.PI / 180, cx = Math.Cos(a), cy = -Math.Sin(a);
        bool upright = Math.Abs(cy) >= Math.Abs(cx);   // filling up or down: across is sideways
        return s with
        {
            Order = Normalise(s.Pixels.Select(p => (float)((p % s.Width) * cx + (p / s.Width) * cy)).ToArray()),
            AcrossX = upright ? 1 : 0,
            AcrossY = upright ? 0 : 1,
        };
    }

    /// <summary>Fill order for separate parts filled one after another (바드's crystals): the parts left → right by their
    /// middle, each filling along <paramref name="angleDeg"/> as in <see cref="Linear"/>.</summary>
    public static GaugeShape Piecewise(GaugeShape s, double angleDeg)
    {
        var along = Linear(s, angleDeg);
        return along with { Order = InTurn(s, Parts(s), along.Order) };
    }

    /// <summary>Fill order for separate rings filled one after another (도화가's orbs): the rings left → right, each
    /// around its own middle from <paramref name="startDeg"/> as in <see cref="Angular"/>.</summary>
    public static GaugeShape Rings(GaugeShape s, double startDeg, bool clockwise)
    {
        var parts = Parts(s);
        var local = new float[s.Pixels.Length];
        foreach (var ks in parts)
        {
            double cx = ks.Average(k => s.Pixels[k] % s.Width), cy = ks.Average(k => s.Pixels[k] / s.Width);
            foreach (int k in ks) local[k] = Turn(s.Pixels[k] % s.Width, s.Pixels[k] / s.Width, cx, cy, startDeg, clockwise);
        }
        return s with { Order = InTurn(s, parts, local), AcrossX = 0, AcrossY = 0 };
    }

    /// <summary>The pixel indices of each separate part.</summary>
    private static int[][] Parts(GaugeShape s) =>
        Enumerable.Range(0, s.Pieces).Select(p => Enumerable.Range(0, s.Pixels.Length).Where(k => s.Piece[k] == p).ToArray()).ToArray();

    /// <summary>The parts one after another, left → right by their middle, each spread over its own 0..1 of
    /// <paramref name="local"/>.</summary>
    private static float[] InTurn(GaugeShape s, int[][] parts, float[] local)
    {
        var byX = parts.OrderBy(ks => ks.Average(k => s.Pixels[k] % s.Width)).ToArray();
        var order = new float[s.Pixels.Length];
        for (int rank = 0; rank < byX.Length; rank++)
        {
            var ks = byX[rank];   // this part's own 0..1, after the parts left of it
            float lo = ks.Min(k => local[k]), span = Math.Max(1e-6f, ks.Max(k => local[k]) - lo);
            foreach (int k in ks) order[k] = (rank + (local[k] - lo) / span * 0.999f) / byX.Length;
        }
        return order;
    }

    /// <summary>Fill order around (<paramref name="centerX"/>, <paramref name="centerY"/>), starting at
    /// <paramref name="startDeg"/> (0 = right, 90 = up), for rings and arcs.</summary>
    public static GaugeShape Angular(GaugeShape s, double centerX, double centerY, double startDeg, bool clockwise) =>
        s with
        {
            Order = s.Pixels.Select(p => Turn(p % s.Width, p / s.Width, centerX, centerY, startDeg, clockwise)).ToArray(),
            AcrossX = 0,
            AcrossY = 0,
        };

    /// <summary>How far round (0..1) the pixel (x, y) is from <paramref name="startDeg"/> about the centre.</summary>
    private static float Turn(double x, double y, double centerX, double centerY, double startDeg, bool clockwise)
    {
        double deg = Math.Atan2(centerY - y, x - centerX) * 180 / Math.PI;   // counter-clockwise, y up
        double d = clockwise ? startDeg - deg : deg - startDeg;
        return (float)(((d % 360) + 360) % 360 / 360);
    }

    /// <summary>Fill 0..1: the order is split into 100 steps and the value is the end of the furthest step whose
    /// pixels mostly match their filled colour (or a <see cref="GaugeShape.Filling"/> one) within
    /// <paramref name="tolerance"/> per channel. A step lit mostly by a filling colour is a part still filling (바드's gold),
    /// never a full one: the value stays short of the end of its piece.</summary>
    public static double ReadContinuous(Image img, GaugeShape s, int tolerance = 40)
    {
        Span<int> lit = stackalloc int[Bins], own = stackalloc int[Bins], total = stackalloc int[Bins], piece = stackalloc int[Bins];
        lit.Clear(); own.Clear(); total.Clear();
        for (int k = 0; k < s.Pixels.Length; k++)
        {
            int bin = Math.Min(Bins - 1, (int)(s.Order[k] * Bins));
            total[bin]++;
            piece[bin] = s.Piece[k];
            if (Own(img, s, k, tolerance)) { own[bin]++; lit[bin]++; }
            else if (InFilling(img, s, k, tolerance)) lit[bin]++;
        }
        for (int b = Bins - 1; b >= 0; b--)
        {
            if (total[b] == 0 || 2 * lit[b] <= total[b]) continue;
            double v = (b + 1) / (double)Bins >= FullFrom ? 1 : (b + 1) / (double)Bins;
            if (2 * own[b] > total[b]) return v;
            float end = 0;   // still filling: at most the last place in the fill order of its piece, short of its end
            for (int k = 0; k < s.Pixels.Length; k++) if (s.Piece[k] == piece[b]) end = Math.Max(end, s.Order[k]);
            // and only as far as the fill runs on from the piece's start: a ring starts and ends at the same place, so a
            // spark at the start of an empty one (도화가bug_aden.mp4) also lights its last bins.
            int first = 0;
            while (total[first] == 0 || piece[first] != piece[b]) first++;
            int run = first - 1;
            for (int i = first, gap = 0; i <= b && gap <= MaxGap; i++)
                if (total[i] == 0) continue;
                else if (2 * lit[i] > total[i]) (run, gap) = (i, 0);
                else gap++;
            return Math.Min(Math.Min(v, end), (run + 1) / (double)Bins);
        }
        return 0;
    }

    // The last 5% is a sliver (the top of 발키리's orb) that the game's shine covers once the gauge is over 80%: a full orb
    // reads 0.95 as often as 1.00 (발키리아덴버그.mp4, ...2.mp4). Read as full, which glows needing a full gauge rely on.
    const double FullFrom = 0.95;
    // Unlit bins a filling piece's run may skip (a glint or a thin part of the ring) and still carry on.
    const int MaxGap = 3;

    /// <summary>Number of pieces (orbs, stacks) whose pixels mostly match their filled colour.</summary>
    public static int ReadPieces(Image img, GaugeShape s, int tolerance = 40)
    {
        Span<int> lit = stackalloc int[s.Pieces], total = stackalloc int[s.Pieces];
        lit.Clear(); total.Clear();
        for (int k = 0; k < s.Pixels.Length; k++)
        {
            total[s.Piece[k]]++;
            if (Filled(img, s, k, tolerance)) lit[s.Piece[k]]++;
        }
        int n = 0;
        for (int p = 0; p < s.Pieces; p++) if (2 * lit[p] > total[p]) n++;
        return n;
    }

    private static bool Filled(Image img, GaugeShape s, int k, int tolerance) => Own(img, s, k, tolerance) || InFilling(img, s, k, tolerance);

    /// <summary>The pixel is in a <see cref="GaugeShape.Filling"/> colour, at its own place only: 바드's gold also matches
    /// the frame around the crystals.</summary>
    private static bool InFilling(Image img, GaugeShape s, int k, int tolerance)
    {
        int p = s.Pixels[k], at = p / s.Width * img.Stride + p % s.Width * 4;
        foreach (int c in s.Filling) if (Near(img, at, c, tolerance)) return true;
        return false;
    }

    /// <summary>The pixel is in its own filled colour, here or up to <see cref="GaugeShape.Slack"/> pixels across the fill.</summary>
    private static bool Own(Image img, GaugeShape s, int k, int tolerance)
    {
        int p = s.Pixels[k], x = p % s.Width, y = p / s.Width;
        if (Near(img, y * img.Stride + x * 4, s.FilledBgr[k], tolerance)) return true;
        // Beside it: only the pixel's own colour, only a colour well away from grey, and only on the same piece of the
        // gauge. The frame around 바드's crystals matches its gold and the dark tips of the crystals; skill icons beside
        // them light up in blue.
        int own = s.FilledBgr[k], spread = Math.Max(own & 255, Math.Max(own >> 8 & 255, own >> 16 & 255)) - Math.Min(own & 255, Math.Min(own >> 8 & 255, own >> 16 & 255));
        if (spread <= 2 * tolerance) return false;
        for (int d = 1; d <= s.Slack; d++)
            for (int e = -d; e <= d; e += 2 * d)
            {
                int px = x + e * s.AcrossX, py = y + e * s.AcrossY;
                if (px < 0 || py < 0 || px >= s.Width || py >= s.Height || s.At[py * s.Width + px] is var n && (n < 0 || s.Piece[n] != s.Piece[k])) continue;
                if (Near(img, py * img.Stride + px * 4, s.FilledBgr[k], tolerance)) return true;
            }
        return false;
    }

    private static bool Near(Image img, int i, int c, int tolerance) =>
        Math.Abs(img.Bgra[i] - (c & 255)) <= tolerance && Math.Abs(img.Bgra[i + 1] - (c >> 8 & 255)) <= tolerance
            && Math.Abs(img.Bgra[i + 2] - (c >> 16 & 255)) <= tolerance;

    private static float[] Normalise(float[] v)
    {
        if (v.Length == 0) return v;
        float min = v.Min(), range = Math.Max(1e-6f, v.Max() - min);
        return v.Select(x => (x - min) / range).ToArray();
    }
}
