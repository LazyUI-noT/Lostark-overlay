using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The white "심판" the game writes over the player's character when 심판 starts (단심, user 2026-10-13), in a band
/// round the screen's centre, where the camera keeps the character. It is not anchored to the character's own HP bar: the
/// bar is covered at the very moment (슈퍼차지 sits on it, 48.0–48.5 s) and moves with the camera. Damage numbers are yellow,
/// and other white text ("단죄", "슈퍼차지") another shape. Any 데미지 폰트 size (75–300%): words are compared at their own size.</summary>
public static class JudgmentText
{
    // The text frames hold 1–20 white specks; a white flash thousands.
    const int MaxBlobs = 300;

    /// <summary>One white word: its box in the band and each pixel's whiteness (0–1) in it.</summary>
    public readonly record struct Word(PixelRect Box, float[] Whiteness);

    // Words are compared as their mean whiteness in each cell of a GridW × GridH grid, whatever their size (strokes are 2–3 px
    // at 100%: a finer grid or a yes/no mask swings with a pixel's shift).
    internal const int GridW = 12, GridH = 6;
    static readonly float[] Judgment = Decode(JudgmentTemplates.TextJudgment), Conviction = Decode(JudgmentTemplates.TextConviction);
    // "심판" and "단죄" are both two syllables about twice as wide as tall (1.9–2.1 at every size in the clip); 슈퍼차지 is four.
    const double MinAspect = 1.5, MaxAspect = 2.6;
    // Real "심판" 0.6–1.0 (0.3 lead over 단죄 at least); 단죄 at 100–300% 0.13 at most, nothing else in the band above 0.1.
    internal const double MinText = 0.45, MinLead = 0.2;

    /// <summary>데미지 폰트 크기 (the game's setting, %) and the height of "심판" at it on a 1080-high viewport: the text doesn't
    /// scale in proportion (단심75%.mp4 … 단심300%.mp4).</summary>
    public static readonly IReadOnlyDictionary<int, double> WordHeight = new Dictionary<int, double>
        { [75] = 13, [100] = 19.5, [150] = 29, [200] = 42.5, [300] = 68 };
    const double HeightSlack = 0.2;   // 300% measured 66–75 (the word grows a little as it settles)

    /// <summary>"심판" is written in <paramref name="band"/> at 데미지 폰트 크기 <paramref name="fontSize"/> (a key of
    /// <see cref="WordHeight"/>; another falls back to 100); <paramref name="scale"/>: the viewport's height / 1080.</summary>
    public static bool Find(Image band, double scale, int fontSize)
    {
        double want = WordHeight.GetValueOrDefault(fontSize, WordHeight[100]) * scale;
        return Words(band).Any(w => Math.Abs(w.Box.Height - want) <= want * HeightSlack && Aspect(w) is >= MinAspect and <= MaxAspect
            && Scores(w) is var (j, c) && j >= MinText && j - c >= MinLead);
    }

    static double Aspect(Word w) => (double)w.Box.Width / w.Box.Height;

    /// <summary>The word's likeness (correlation of grids) to "심판" and to "단죄".</summary>
    internal static (double Judgment, double Conviction) Scores(Word w)
    {
        var g = Grid(w);
        return (Ncc(g, Judgment), Ncc(g, Conviction));
    }

    /// <summary>The mean whiteness per cell, row by row.</summary>
    internal static float[] Grid(Word w)
    {
        var g = new float[GridW * GridH];
        int bw = w.Box.Width, bh = w.Box.Height;
        for (int cy = 0; cy < GridH; cy++)
            for (int cx = 0; cx < GridW; cx++)
            {
                int x0 = cx * bw / GridW, x1 = Math.Max(x0 + 1, (cx + 1) * bw / GridW), y0 = cy * bh / GridH, y1 = Math.Max(y0 + 1, (cy + 1) * bh / GridH);
                int n = 0;
                float sum = 0;
                for (int y = y0; y < y1 && y < bh; y++)
                    for (int x = x0; x < x1 && x < bw; x++) { n++; sum += w.Whiteness[y * bw + x]; }
                g[cy * GridW + cx] = n == 0 ? 0 : sum / n;
            }
        return g;
    }

    static double Ncc(float[] a, float[] b)
    {
        double ma = a.Average(), mb = b.Average(), sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++) { double da = a[i] - ma, db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
        return saa == 0 || sbb == 0 ? 0 : sab / Math.Sqrt(saa * sbb);
    }

    static float[] Decode(string grid) => grid.Length == 0 ? new float[GridW * GridH] : [.. Convert.FromBase64String(grid).Select(v => v / 255f)];

    /// <summary>A grid as the templates store it.</summary>
    internal static string Encode(float[] grid) => Convert.ToBase64String([.. grid.Select(v => (byte)Math.Round(v * 255))]);

    /// <summary>How white a pixel is, 0–1: bright in every channel and grey. Over half counts as part of a word.</summary>
    static float Whiteness(ReadOnlySpan<byte> px)
    {
        int lo = Math.Min(px[0], Math.Min(px[1], px[2])), hi = Math.Max(px[0], Math.Max(px[1], px[2]));
        return Math.Clamp((lo - 150) / 60f, 0, 1) * Math.Clamp((75 - (hi - lo)) / 40f, 0, 1);
    }

    static PixelRect Ltrb(int l, int t, int r, int b) => new(l, t, r - l, b - t);

    /// <summary>The white words in <paramref name="band"/>: white blobs, joined while they sit within half a word's height
    /// of each other (a syllable's strokes, then the syllables).</summary>
    public static List<Word> Words(Image band)
    {
        int w = band.Width, h = band.Height;
        var whiteness = new float[w * h];
        var mask = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            var row = band.Row(y);
            for (int x = 0, i = y * w; x < w; x++, i++)
            {
                var px = row.Slice(x * 4, 3);
                if (px[0] < 150 || px[1] < 150 || px[2] < 150) continue;   // most of the band: no whiteness at all
                float v = Whiteness(px);
                (whiteness[i], mask[i]) = (v, v > 0.5f);
            }
        }
        var boxes = Blobs(mask, w, h).Where(b => b.Width * b.Height >= 4).ToList();
        if (boxes.Count > MaxBlobs) return [];   // a bright flash, not text: joining that many would stall the capture thread
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int i = 0; i < boxes.Count && !merged; i++)
                for (int j = i + 1; j < boxes.Count && !merged; j++)
                    if (Near(boxes[i], boxes[j])) { boxes[i] = Union(boxes[i], boxes[j]); boxes.RemoveAt(j); merged = true; }
        }
        return [.. boxes.Select(b => new Word(b, Cut(whiteness, w, b)))];
    }

    /// <summary>Within half the taller box's height of each other on both axes.</summary>
    static bool Near(PixelRect a, PixelRect b)
    {
        int gap = Math.Max(a.Height, b.Height) / 2;
        return b.X - a.Right <= gap && a.X - b.Right <= gap && b.Y - a.Bottom <= gap && a.Y - b.Bottom <= gap;
    }

    static PixelRect Union(PixelRect a, PixelRect b) =>
        Ltrb(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));

    static float[] Cut(float[] values, int width, PixelRect r)
    {
        var cut = new float[r.Width * r.Height];
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++) cut[y * r.Width + x] = values[(r.Y + y) * width + r.X + x];
        return cut;
    }

    /// <summary>Bounding boxes of the 8-connected true regions.</summary>
    static List<PixelRect> Blobs(bool[] mask, int w, int h)
    {
        var seen = new bool[mask.Length];
        var boxes = new List<PixelRect>();
        var stack = new Stack<int>();
        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || seen[start]) continue;
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, y = i / w;
                (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy, j = ny * w + nx;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[j] || !mask[j]) continue;
                        seen[j] = true;
                        stack.Push(j);
                    }
            }
            boxes.Add(Ltrb(x0, y0, x1 + 1, y1 + 1));
        }
        return boxes;
    }
}
