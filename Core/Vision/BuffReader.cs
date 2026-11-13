using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.Core.Vision.ImageOps;

namespace GaugeOverlay.Core.Vision;

/// <summary>Finds the four party buffs (PLAN §4 버프) in the buff row and reads their timers. A box counts as a buff
/// only when its icon matches the buff's template in structure (a coarse colour grid) and in hue; the frame and timer
/// colours differ between players (gold, green), so the frame only tells where the row's icons end (<see cref="ReadRow"/>).
/// Kept apart from the skill and gauge paths.
/// Not thread-safe: one instance per reading thread.</summary>
public sealed class BuffReader
{
    // ── Icons ─────────────────────────────────────────────────────────────────────────────────────────────────────
    const int Grid = 10;           // thumbnail cells per side (4k_down's icon interior is about 15 px)
    const double InsetShare = 0.1; // the gold frame and dark border: 3 of 31 px at buff 120%
    // Measured on every labelled box of buff_gameplay (1080p, buff 120%) and 4k_down (810-high viewport, buff 100%;
    // BuffReaderTests labels): the weakest real buff scores 0.633 (아덴피증 at 4k_down's size), the strongest look-alike
    // within the hue limit 0.560 (발키리's gold coin and cross against 아덴피증); real buffs sit within 10° of their
    // template's hue.
    internal const double MinStructure = 0.60, MaxHueDistance = 15;

    static readonly Image[] Templates = [.. BuffTemplates.Icons.Select(s => Image.FromBytes(Convert.FromBase64String(s))!)];
    static readonly float[][] TemplateThumbs = [.. Templates.Select(Thumb)];
    static readonly double[] TemplateHues = [.. Templates.Select(t => SlotClassifier.ColourStats(t).MeanHue)];
    // Icons that pass a buff's checks but aren't it (발키리's own gold dome scores 0.63 against 아덴피증, as high as the
    // weakest real one): a box matching one of these at least as well as the buff it resembles is rejected. Only that
    // buff: structure ignores colour, so at small sizes the gold dome also outscores a real red 공증.
    static readonly (int Of, float[] Thumb)[] LookAlikeThumbs =
        [.. BuffTemplates.LookAlikes.Select(l => ((int)l.Of, Thumb(Image.FromBytes(Convert.FromBase64String(l.Icon))!)))];

    /// <summary>The icon inside its frame.</summary>
    public static PixelRect Interior(PixelRect icon)
    {
        int d = (int)Math.Round(icon.Width * InsetShare);
        return new(icon.X + d, icon.Y + d, icon.Width - 2 * d, icon.Height - 2 * d);
    }

    /// <summary>The buff in the icon at <paramref name="icon"/> (relative to <paramref name="area"/>), or null for
    /// another buff, an empty box or an icon under the game's periodic glare.</summary>
    public static BuffKind? Classify(Image area, PixelRect icon) => Read(area, icon)?.Kind;

    /// <summary><see cref="Classify"/> and the winning template's structure score, for <see cref="ReadRow"/>'s stricter bar.</summary>
    public static (BuffKind Kind, double Structure)? Read(Image area, PixelRect icon)
    {
        var scores = Scores(area, icon);
        return Best(scores) is { } k ? ((BuffKind)k, scores.Structure[k]) : null;
    }

    // ── Frame ─────────────────────────────────────────────────────────────────────────────────────────────────────
    // A buff's frame is gold or green by who cast it (olive at buff 80%); some icons (another player's purple 뎀증) have none.
    // Frame-band share in those colours (scratch probe over every buffrow fixture and v1, 2026-11-12): real buffs 0.19+ on
    // buffrow_bg, dbl, dohwa1/2, gzbug, hud80/90 and wrong; down to 0.00 on 4k_down (frames shrunk to grey) and a dimmed
    // screen; v1's empty boxes over the scenery 0.00–0.05.
    internal const double MinFrame = 0.05;
    // Past the framed run a box needs this structure: v1's empty boxes reach 0.69 (60 fps scan); frameless real icons past
    // the run score 0.71–0.87 on 4k_down, so 2 of its 493 reads (0.71, 0.73) are lost, and a dimmed screen (the settings
    // window in buff_80_to_110) drops a real icon for those frames. BuffTracker rides over short gaps.
    internal const double MinUnframedStructure = 0.75;

    /// <summary>Whether the box at <paramref name="icon"/> has a buff frame: its outer band (2 px of 31, scaled) at least
    /// <see cref="MinFrame"/> green or gold. Empty boxes show the scenery and no frame.</summary>
    public static bool Framed(Image area, PixelRect icon)
    {
        int band = Math.Max(1, (int)Math.Round(icon.Width * 2 / 31.0)), frame = 0, all = 0;
        for (int y = Math.Max(0, icon.Y); y < Math.Min(area.Height, icon.Bottom); y++)
            for (int x = Math.Max(0, icon.X); x < Math.Min(area.Width, icon.Right); x++)
            {
                if (x - icon.X >= band && icon.Right - 1 - x >= band && y - icon.Y >= band && icon.Bottom - 1 - y >= band) continue;
                var (b, g, r) = area.Pixel(x, y);
                all++;
                if (FramePixel(b, g, r)) frame++;
            }
        return all > 0 && frame >= MinFrame * all;
    }

    /// <summary>Green (as <see cref="DebuffReader.FrameColours"/>) or gold to olive: red and green well over blue.</summary>
    static bool FramePixel(int b, int g, int r) =>
        g >= 90 && g - Math.Max(r, b) >= 40 || Math.Max(r, g) >= 90 && Math.Min(r, g) - b >= 30;

    /// <summary>The buffs in a row of <paramref name="count"/> boxes, box 0 first. The row packs from box 0 (the right), so the
    /// boxes up to the last framed one (<see cref="RowCadence.Filled"/>: one unframed box passed) hold icons and read at the
    /// usual bar; a box past them is most likely empty scenery, read only at <see cref="MinUnframedStructure"/>. Pure.</summary>
    public static BuffKind?[] ReadRow(int count, Func<int, bool> framed, Func<int, (BuffKind Kind, double Structure)?> read)
    {
        int run = RowCadence.Filled(count, framed);
        var row = new BuffKind?[count];
        for (int i = 0; i < count; i++)
            if (read(i) is { } r && (i < run || r.Structure >= MinUnframedStructure)) row[i] = r.Kind;
        return row;
    }

    /// <summary>The template that passes both checks with the highest structure score, unless a look-alike of it
    /// scores as well; null for none. Shared with <see cref="DebuffReader"/>.</summary>
    internal static int? Best((double[] Structure, double[] Hue, double[] LookAlike) scores)
    {
        var (structure, hue, lookAlike) = scores;
        int best = -1;
        for (int k = 0; k < structure.Length; k++)
            if (hue[k] <= MaxHueDistance && structure[k] >= MinStructure && (best < 0 || structure[k] > structure[best])) best = k;
        return best < 0 || lookAlike[best] >= structure[best] ? null : best;
    }

    /// <summary>Structure score (−1..1) of the icon against each template, best over a small position search, the
    /// icon's hue distance to each (degrees), and its best structure score against each template's look-alikes.</summary>
    internal static (double[] Structure, double[] Hue, double[] LookAlike) Scores(Image area, PixelRect icon) =>
        Match(area, icon, TemplateThumbs, TemplateHues, LookAlikeThumbs);

    /// <summary><see cref="Scores"/> against any set of <see cref="Thumb"/>s and their mean hues.</summary>
    internal static (double[] Structure, double[] Hue, double[] LookAlike) Match(Image area, PixelRect icon, float[][] thumbs, double[] hues,
        (int Of, float[] Thumb)[] lookAlikes)
    {
        var inner = Interior(icon);
        int search = Math.Max(1, inner.Width / 12);
        var best = Enumerable.Repeat(-2.0, thumbs.Length).ToArray();
        var hue = new double[thumbs.Length];
        var lookAlike = Enumerable.Repeat(-2.0, thumbs.Length).ToArray();
        int x0 = Math.Max(0, inner.X - search), y0 = Math.Max(0, inner.Y - search);
        var region = new PixelRect(x0, y0, Math.Min(area.Width, inner.Right + search) - x0, Math.Min(area.Height, inner.Bottom + search) - y0);
        if (region.Width <= 0 || region.Height <= 0) return (best, hue, lookAlike);
        // Every offset's thumbnail from one table of sums: no crop or array per offset (perf-hotspots.plan.md, 3a).
        using var sums = new BgrSums(area, region);
        Span<float> thumb = stackalloc float[Grid * Grid * 3];
        Span<double> centred = stackalloc double[Grid * Grid * 3];
        var templates = Array.ConvertAll(thumbs, Centred.Of);
        var alikes = Array.ConvertAll(lookAlikes, l => (l.Of, Thumb: Centred.Of(l.Thumb)));
        for (int dy = -search; dy <= search; dy++)
            for (int dx = -search; dx <= search; dx++)
            {
                var r = inner with { X = inner.X + dx, Y = inner.Y + dy };
                if (r.X < 0 || r.Y < 0 || r.Right > area.Width || r.Bottom > area.Height) continue;
                sums.Thumb(r, thumb);
                double squares = Centre(thumb, centred);
                foreach (var (of, t) in alikes) lookAlike[of] = Math.Max(lookAlike[of], ChannelNcc(centred, squares, t));
                double? meanHue = null;   // once per offset, and only when a template's best moved here
                for (int k = 0; k < thumbs.Length; k++)
                {
                    double s = ChannelNcc(centred, squares, templates[k]);
                    if (s <= best[k]) continue;
                    best[k] = s;
                    meanHue ??= SlotClassifier.ColourStats(area, r).MeanHue;
                    hue[k] = HueDistance(meanHue.Value, hues[k]);
                }
            }
        return (best, hue, lookAlike);
    }

    /// <summary>Grid × Grid block means of B, G and R.</summary>
    internal static float[] Thumb(Image img)
    {
        var whole = new PixelRect(0, 0, img.Width, img.Height);
        var t = new float[Grid * Grid * 3];
        using var sums = new BgrSums(img, whole);
        sums.Thumb(whole, t);
        return t;
    }

    internal const int ThumbLength = Grid * Grid * 3;

    /// <summary><see cref="Thumb(Image)"/> of <paramref name="r"/> in <paramref name="img"/> (inside it) into <paramref name="t"/>.</summary>
    internal static void Thumb(Image img, PixelRect r, Span<float> t)
    {
        using var sums = new BgrSums(img, r);
        sums.Thumb(r, t);
    }

    /// <summary>Summed-area tables of B, G and R over a region of an image: any rectangle's sums in four lookups, the same
    /// integers as adding its pixels. The array is pooled: dispose.</summary>
    internal sealed class BgrSums : IDisposable
    {
        readonly int[] _s;
        readonly int _x, _y, _w;

        public BgrSums(Image img, PixelRect region)
        {
            (_x, _y, _w) = (region.X, region.Y, region.Width + 1);
            int rows = region.Height + 1;
            _s = System.Buffers.ArrayPool<int>.Shared.Rent(_w * rows * 3);
            Array.Clear(_s, 0, _w * 3);
            for (int y = 1; y < rows; y++)
            {
                int o = y * _w * 3, up = o - _w * 3, b = 0, g = 0, r = 0;
                (_s[o], _s[o + 1], _s[o + 2]) = (0, 0, 0);
                for (int x = 1, i = (region.Y + y - 1) * img.Stride + region.X * 4; x < _w; x++, i += 4)
                {
                    b += img.Bgra[i]; g += img.Bgra[i + 1]; r += img.Bgra[i + 2];
                    int c = o + x * 3, u = up + x * 3;
                    (_s[c], _s[c + 1], _s[c + 2]) = (_s[u] + b, _s[u + 1] + g, _s[u + 2] + r);
                }
            }
        }

        /// <summary><see cref="BuffReader.Thumb(Image)"/> of <paramref name="r"/> (image coordinates, inside the region) into <paramref name="t"/>.</summary>
        public void Thumb(PixelRect r, Span<float> t)
        {
            for (int gy = 0; gy < Grid; gy++)
            {
                int y0 = gy * r.Height / Grid, y1 = Math.Max(y0 + 1, (gy + 1) * r.Height / Grid);
                for (int gx = 0; gx < Grid; gx++)
                {
                    int x0 = gx * r.Width / Grid, x1 = Math.Max(x0 + 1, (gx + 1) * r.Width / Grid);
                    int a = Index(r.X + x0, r.Y + y0), b = Index(r.X + x1, r.Y + y0), c = Index(r.X + x0, r.Y + y1), d = Index(r.X + x1, r.Y + y1);
                    float n = (x1 - x0) * (y1 - y0);
                    int o = (gy * Grid + gx) * 3;
                    for (int ch = 0; ch < 3; ch++) t[o + ch] = (_s[d + ch] - _s[b + ch] - _s[c + ch] + _s[a + ch]) / n;
                }
            }
        }

        int Index(int x, int y) => ((y - _y) * _w + x - _x) * 3;

        public void Dispose() => System.Buffers.ArrayPool<int>.Shared.Return(_s);
    }

    /// <summary>Correlation of two colour thumbnails with each channel's own mean removed: the same shapes in each
    /// channel, whatever the colour balance (every gold icon has R &gt; G &gt; B; colour is the hue check's job).</summary>
    /// <remarks>Split in two so each side is centred once: the thumb once per offset (<see cref="Centre"/>), each template once
    /// ever (<see cref="Centred.Of"/>). Same sums in the same order as one pass, so bit for bit the same (BuffMatchExactTests).</remarks>
    internal static double ChannelNcc(ReadOnlySpan<double> a, double saa, Centred b)
    {
        double sab = 0;
        var db = b.Values;
        for (int i = 0; i < a.Length; i++) sab += a[i] * db[i];
        return saa < 1e-6 || b.Squares < 1e-6 ? 0 : sab / Math.Sqrt(saa * b.Squares);
    }

    /// <summary>Writes <paramref name="a"/> with each channel's mean removed into <paramref name="centred"/>; returns its sum of squares.</summary>
    internal static double Centre(ReadOnlySpan<float> a, Span<double> centred)
    {
        Span<double> m = stackalloc double[3];
        for (int i = 0; i < a.Length; i++) m[i % 3] += a[i];
        for (int c = 0; c < 3; c++) m[c] /= a.Length / 3;
        double squares = 0;
        for (int i = 0; i < a.Length; i++) { double d = a[i] - m[i % 3]; centred[i] = d; squares += d * d; }
        return squares;
    }

    /// <summary>A template thumb centred by <see cref="Centre"/>, kept per array (templates are never changed once built).</summary>
    internal sealed record Centred(double[] Values, double Squares)
    {
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<float[], Centred> Kept = new();

        public static Centred Of(float[] thumb) => Kept.GetValue(thumb, t =>
        {
            var values = new double[t.Length];
            return new(values, Centre(t, values));
        });
    }

    internal static double HueDistance(double a, double b) { double d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }

    // ── Timer ("7초", "0.7초") ────────────────────────────────────────────────────────────────────────────────────
    // The game's timer font does not scale in proportion to the buff size (its size steps unevenly: 80% text is 0.55
    // of 120% text, not 0.67), so there is one glyph set per buff size, each learned at a common size (the 120% cells
    // below) from text enlarged by that set's Enlarge (BuffGlyphs).
    const int DigitCell = 7, DotCell = 4, Margin = 2;
    // Measured on the labelled timers (BuffTimerTests, BuffSizeTests): '초' scores 0.81+ and a ones digit 0.69+ at 120%.
    // Left of a single digit the floor art scores up to 0.67 as a tens digit (90%, 100%), so MinTens sits above it; real
    // tens score 0.81+ at 120% but as low as 0.38 at 90–100% (a 1 px '1'), so a tens can be missed, never invented.
    // BuffTracker puts a missed tens back from its countdown.
    const double MinCho = 0.6, MinDigit = 0.6, MinDot = 0.6, MinTens = 0.70;
    // The 0 of "0.7초" decides tenths without the dot: 0.81+ on the 도화가 clips, 0.26 at most left of a whole digit.
    const double MinZero = 0.7;

    sealed record Glyph(char Ch, int W, int H, float[] Luma);
    sealed record GlyphSet(int Size, double Enlarge, Glyph Cho, Glyph Dot, Glyph[] Digits);

    static readonly GlyphSet[] Sets = [.. BuffGlyphs.Sets.Select(s =>
    {
        var g = s.Glyphs.ToDictionary(x => x.Ch, x => new Glyph(x.Ch, x.Rows[0].Length, x.Rows.Length,
            [.. x.Rows.SelectMany(r => r.Select(c => DigitReader.GreyRamp.IndexOf(c) * 28.3f))]));
        return new GlyphSet(s.Size, s.Enlarge, g['초'], g['.'], [.. Enumerable.Range(0, 10).Select(d => g[(char)('0' + d)])]);
    })];

    readonly GlyphSet _set;
    readonly double _enlarge;

    /// <summary>A reader for boxes at <paramref name="buffScale"/> (<see cref="Geometry.BuffScale"/>): the glyph set of the
    /// nearest buff size at 1080 (scale × 100), with its enlargement adjusted by the remaining ratio.</summary>
    public BuffReader(double buffScale)
    {
        double size = buffScale * 100;
        _set = Sets.MinBy(s => Math.Abs(s.Size - size))!;
        _enlarge = _set.Enlarge * _set.Size / size;
    }

    /// <summary>Scores of the last <see cref="ReadTimer"/>: '초', the ones digit and the tens candidate, for diagnostics.</summary>
    internal (double Cho, double Ones, double Tens) LastScores { get; private set; }

    /// <summary>The timer shown in <paramref name="timer"/> (relative to <paramref name="area"/>): whole seconds, or
    /// tenths below 1 s; null when it can't be read.</summary>
    public double? ReadTimer(Image area, PixelRect timer)
    {
        // The text is matched at the glyphs' own size: the capture is scaled to it (bilinear), which keeps strokes that
        // shrinking the 10-level glyphs to a smaller text would lose.
        var text = Resample(area, new PixelRect(timer.X, timer.Y - Margin, timer.Width, timer.Height + 2 * Margin), _enlarge);
        return Read(text) ?? Read(TextGrey(text));
    }

    double? Read(Image image)
    {
        // The luminance once for the whole text (icon-gates.plan.md): each glyph window is copied out of it, the same numbers
        // LumaFast gave per window.
        var text = new Luma(LumaFast(image, new PixelRect(0, 0, image.Width, image.Height)), image.Width, image.Height);
        int top = (int)Math.Round(Margin * _enlarge), bottom = text.Height - top;
        LastScores = default;
        var cho = Best([_set.Cho], text, 0, text.Width - _set.Cho.W, top - 2, bottom - _set.Cho.H + 2);
        if (cho.Score < MinCho) return null;
        var ones = Best(_set.Digits, text, cho.X - DigitCell - 1, cho.X - DigitCell + 1, cho.Y - 1, cho.Y + 1);
        LastScores = (cho.Score, ones.Score, 0);
        if (ones.Score < MinDigit) return null;
        int d1 = ones.Glyph!.Ch - '0';
        // Tenths are shown only below 1 s ("0.7초"), so the digit before the point is 0, and a whole timer never has a 0
        // there (its tens is 1 or 2). The 4 px dot can match as faintly as 0.30 on a busy background while the 0 is clear
        // (도화가support.mp4: 0.81+, against 0.26 at most left of a whole "1"), so a clear 0 decides on its own.
        var dot = Best([_set.Dot], text, ones.X - DotCell - 1, ones.X - DotCell + 1, ones.Y - 1, ones.Y + 1);
        var zero = Best([_set.Digits[0]], text, ones.X - DotCell - DigitCell - 1, ones.X - DotCell - DigitCell + 1, ones.Y - 1, ones.Y + 1);
        if (zero.Score >= MinZero || dot.Score >= MinDot && zero.Score >= MinDigit) return d1 / 10.0;
        // Every buff lasts 20 s or less, so a tens digit is 1 or 2.
        var tens = Best([_set.Digits[1], _set.Digits[2]], text, ones.X - DigitCell - 1, ones.X - DigitCell + 1, ones.Y - 1, ones.Y + 1);
        LastScores = (cho.Score, ones.Score, tens.Score);
        return tens.Score >= MinTens ? (tens.Glyph!.Ch - '0') * 10 + d1 : d1;
    }

    /// <summary>The best of <paramref name="glyphs"/> with its top-left anywhere in x0..x1 × y0..y1.</summary>
    static (Glyph? Glyph, int X, int Y, double Score) Best(Glyph[] glyphs, Luma img, int x0, int x1, int y0, int y1)
    {
        var best = ((Glyph?)null, 0, 0, -2.0);
        foreach (var g in glyphs)
        {
            var window = new float[g.W * g.H];
            for (int y = Math.Max(0, y0); y <= Math.Min(img.Height - g.H, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(img.Width - g.W, x1); x++)
                {
                    for (int row = 0; row < g.H; row++)
                        Array.Copy(img.V, (y + row) * img.Width + x, window, row * g.W, g.W);
                    double s = Correlate(window, g.Luma);
                    if (s > best.Item4) best = (g, x, y, s);
                }
        }
        return best;
    }

    /// <summary>An image's <see cref="ImageOps.LumaFast"/> values, row by row.</summary>
    readonly record struct Luma(float[] V, int Width, int Height);

    // ── Pixels ────────────────────────────────────────────────────────────────────────────────────────────────────
    // Timer text is green-grey or gold with a dark outline. On a bright gold floor (공증bug.mp4) the green-grey fill has
    // the floor's luminance and only the outline shows, so a timer plain luminance can't read is read again with red
    // over green taken off (ReadTimer): the gold floor (R − G about 28) darkens, the green-grey fill (about −30) brightens.
    const int RedOverGreen = 2;

    /// <summary>A grey copy of <paramref name="img"/>: its <see cref="ImageOps.LumaFast"/> less <see cref="RedOverGreen"/> × (R − G),
    /// clamped to 0..255, the timer text's own brightness against a gold floor.</summary>
    static Image TextGrey(Image img)
    {
        var dst = new Image(img.Width, img.Height);
        var v = LumaFast(img, new PixelRect(0, 0, img.Width, img.Height));
        for (int k = 0; k < v.Length; k++)
        {
            int i = k / img.Width * img.Stride + k % img.Width * 4, o = k / img.Width * dst.Stride + k % img.Width * 4;
            byte g = (byte)Math.Clamp(v[k] - RedOverGreen * (img.Bgra[i + 2] - img.Bgra[i + 1]), 0, 255);
            (dst.Bgra[o], dst.Bgra[o + 1], dst.Bgra[o + 2], dst.Bgra[o + 3]) = (g, g, g, 255);
        }
        return dst;
    }

    /// <summary>Top-left of the best match of a <paramref name="w"/>×<paramref name="h"/> luminance template inside
    /// <paramref name="within"/> (it may reach 2 px past its top and bottom). Used to cut the glyphs.</summary>
    internal static (int X, int Y) Find(Image img, PixelRect within, float[] template, int w, int h)
    {
        var best = (X: within.X, Y: within.Y, S: -2.0);
        for (int y = Math.Max(0, within.Y - 2); y + h <= Math.Min(img.Height, within.Bottom + 2); y++)
            for (int x = Math.Max(0, within.X); x + w <= Math.Min(img.Width, within.Right); x++)
            {
                double s = Correlate(LumaFast(img, new PixelRect(x, y, w, h)), template);
                if (s > best.S) best = (x, y, s);
            }
        return (best.X, best.Y);
    }
}
