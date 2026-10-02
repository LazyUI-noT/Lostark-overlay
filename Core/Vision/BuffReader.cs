using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Finds the four party buffs (PLAN §4 버프) in the buff row and reads their timers. A box counts as a buff
/// only when its icon matches the buff's template in structure (a coarse colour grid) and in hue; the frame and timer
/// colours differ between players (gold, green), so neither is used. Kept apart from the skill and gauge paths.
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
    public static BuffKind? Classify(Image area, PixelRect icon) => Best(Scores(area, icon)) is { } k ? (BuffKind)k : null;

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
        for (int dy = -search; dy <= search; dy++)
            for (int dx = -search; dx <= search; dx++)
            {
                var r = inner with { X = inner.X + dx, Y = inner.Y + dy };
                if (r.X < 0 || r.Y < 0 || r.Right > area.Width || r.Bottom > area.Height) continue;
                var crop = Crop(area, r);
                var thumb = Thumb(crop);
                foreach (var (of, t) in lookAlikes) lookAlike[of] = Math.Max(lookAlike[of], ChannelNcc(thumb, t));
                for (int k = 0; k < thumbs.Length; k++)
                {
                    double s = ChannelNcc(thumb, thumbs[k]);
                    if (s <= best[k]) continue;
                    best[k] = s;
                    hue[k] = HueDistance(SlotClassifier.ColourStats(crop).MeanHue, hues[k]);
                }
            }
        return (best, hue, lookAlike);
    }

    /// <summary>Grid × Grid block means of B, G and R.</summary>
    internal static float[] Thumb(Image img)
    {
        var t = new float[Grid * Grid * 3];
        for (int gy = 0; gy < Grid; gy++)
        {
            int y0 = gy * img.Height / Grid, y1 = Math.Max(y0 + 1, (gy + 1) * img.Height / Grid);
            for (int gx = 0; gx < Grid; gx++)
            {
                int x0 = gx * img.Width / Grid, x1 = Math.Max(x0 + 1, (gx + 1) * img.Width / Grid), b = 0, g = 0, r = 0;
                for (int y = y0; y < y1; y++)
                    for (int i = y * img.Stride + x0 * 4, e = y * img.Stride + x1 * 4; i < e; i += 4)
                    { b += img.Bgra[i]; g += img.Bgra[i + 1]; r += img.Bgra[i + 2]; }
                float n = (x1 - x0) * (y1 - y0);
                int c = (gy * Grid + gx) * 3;
                (t[c], t[c + 1], t[c + 2]) = (b / n, g / n, r / n);
            }
        }
        return t;
    }

    /// <summary>Correlation of two colour thumbnails with each channel's own mean removed: the same shapes in each
    /// channel, whatever the colour balance (every gold icon has R &gt; G &gt; B; colour is the hue check's job).</summary>
    static double ChannelNcc(float[] a, float[] b)
    {
        Span<double> ma = stackalloc double[3], mb = stackalloc double[3];
        for (int i = 0; i < a.Length; i++) { ma[i % 3] += a[i]; mb[i % 3] += b[i]; }
        for (int c = 0; c < 3; c++) { ma[c] /= a.Length / 3; mb[c] /= b.Length / 3; }
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++) { double da = a[i] - ma[i % 3], db = b[i] - mb[i % 3]; sab += da * db; saa += da * da; sbb += db * db; }
        return saa < 1e-6 || sbb < 1e-6 ? 0 : sab / Math.Sqrt(saa * sbb);
    }

    static double HueDistance(double a, double b) { double d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }

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

    double? Read(Image text)
    {
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
    static (Glyph? Glyph, int X, int Y, double Score) Best(Glyph[] glyphs, Image img, int x0, int x1, int y0, int y1)
    {
        var best = ((Glyph?)null, 0, 0, -2.0);
        foreach (var g in glyphs)
            for (int y = Math.Max(0, y0); y <= Math.Min(img.Height - g.H, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(img.Width - g.W, x1); x++)
                {
                    double s = Correlate(Luma(img, new PixelRect(x, y, g.W, g.H)), g.Luma);
                    if (s > best.Item4) best = (g, x, y, s);
                }
        return best;
    }

    // ── Pixels ────────────────────────────────────────────────────────────────────────────────────────────────────
    internal static Image Crop(Image img, PixelRect r)
    {
        var dst = new Image(r.Width, r.Height);
        for (int y = 0; y < r.Height; y++) img.Row(r.Y + y).Slice(r.X * 4, r.Width * 4).CopyTo(dst.Bgra.AsSpan(y * dst.Stride));
        return dst;
    }

    /// <summary><paramref name="r"/> of <paramref name="img"/> scaled by <paramref name="f"/> (bilinear; pixels past the
    /// edge repeat it).</summary>
    internal static Image Resample(Image img, PixelRect r, double f)
    {
        int w = Math.Max(1, (int)Math.Round(r.Width * f)), h = Math.Max(1, (int)Math.Round(r.Height * f));
        var dst = new Image(w, h);
        for (int y = 0; y < h; y++)
        {
            double sy = Math.Clamp(r.Y + (y + 0.5) / f - 0.5, 0, img.Height - 1);
            int y0 = (int)sy, y1 = Math.Min(img.Height - 1, y0 + 1);
            double fy = sy - y0;
            for (int x = 0; x < w; x++)
            {
                double sx = Math.Clamp(r.X + (x + 0.5) / f - 0.5, 0, img.Width - 1);
                int x0 = (int)sx, x1 = Math.Min(img.Width - 1, x0 + 1);
                double fx = sx - x0;
                for (int c = 0; c < 4; c++)
                {
                    double t = img.Bgra[y0 * img.Stride + x0 * 4 + c] * (1 - fx) + img.Bgra[y0 * img.Stride + x1 * 4 + c] * fx;
                    double b = img.Bgra[y1 * img.Stride + x0 * 4 + c] * (1 - fx) + img.Bgra[y1 * img.Stride + x1 * 4 + c] * fx;
                    dst.Bgra[y * dst.Stride + x * 4 + c] = (byte)Math.Round(t * (1 - fy) + b * fy);
                }
            }
        }
        return dst;
    }

    /// <summary>Luminance of <paramref name="r"/> in <paramref name="img"/>, row by row.</summary>
    internal static float[] Luma(Image img, PixelRect r)
    {
        var v = new float[r.Width * r.Height];
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
            {
                int i = (r.Y + y) * img.Stride + (r.X + x) * 4;
                v[y * r.Width + x] = (img.Bgra[i] * 29 + img.Bgra[i + 1] * 150 + img.Bgra[i + 2] * 77) >> 8;
            }
        return v;
    }

    // Timer text is green-grey or gold with a dark outline. On a bright gold floor (공증bug.mp4) the green-grey fill has
    // the floor's luminance and only the outline shows, so a timer plain luminance can't read is read again with red
    // over green taken off (ReadTimer): the gold floor (R − G about 28) darkens, the green-grey fill (about −30) brightens.
    const int RedOverGreen = 2;

    /// <summary>A grey copy of <paramref name="img"/>: its <see cref="Luma"/> less <see cref="RedOverGreen"/> × (R − G),
    /// clamped to 0..255, the timer text's own brightness against a gold floor.</summary>
    static Image TextGrey(Image img)
    {
        var dst = new Image(img.Width, img.Height);
        var v = Luma(img, new PixelRect(0, 0, img.Width, img.Height));
        for (int k = 0; k < v.Length; k++)
        {
            int i = k / img.Width * img.Stride + k % img.Width * 4, o = k / img.Width * dst.Stride + k % img.Width * 4;
            byte g = (byte)Math.Clamp(v[k] - RedOverGreen * (img.Bgra[i + 2] - img.Bgra[i + 1]), 0, 255);
            (dst.Bgra[o], dst.Bgra[o + 1], dst.Bgra[o + 2], dst.Bgra[o + 3]) = (g, g, g, 255);
        }
        return dst;
    }

    /// <summary>Plain correlation of two equal-length vectors, −1..1.</summary>
    internal static double Correlate(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double ma = 0, mb = 0;
        for (int i = 0; i < a.Length; i++) { ma += a[i]; mb += b[i]; }
        ma /= a.Length; mb /= b.Length;
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++) { double da = a[i] - ma, db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
        return saa < 1e-6 || sbb < 1e-6 ? 0 : sab / Math.Sqrt(saa * sbb);
    }

    /// <summary>Top-left of the best match of a <paramref name="w"/>×<paramref name="h"/> luminance template inside
    /// <paramref name="within"/> (it may reach 2 px past its top and bottom). Used to cut the glyphs.</summary>
    internal static (int X, int Y) Find(Image img, PixelRect within, float[] template, int w, int h)
    {
        var best = (X: within.X, Y: within.Y, S: -2.0);
        for (int y = Math.Max(0, within.Y - 2); y + h <= Math.Min(img.Height, within.Bottom + 2); y++)
            for (int x = Math.Max(0, within.X); x + w <= Math.Min(img.Width, within.Right); x++)
            {
                double s = Correlate(Luma(img, new PixelRect(x, y, w, h)), template);
                if (s > best.S) best = (x, y, s);
            }
        return (best.X, best.Y);
    }
}
