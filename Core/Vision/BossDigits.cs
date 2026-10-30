using System.Text;
using System.Text.RegularExpressions;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Reads the boss HP bar's white text (spec § B): "X n" at its right end (Geometry.BossCount) and "current/max"
/// (Geometry.BossHpText). The text's pixels are those whose min(R,G,B) clears every bar fill (orange, red, teal); glyphs are the
/// column runs between blank columns, each scored by NCC against BossDigitTemplates on a 7×11 grid of a box centred on it, so
/// the HUD size needs no resized templates. A comma is a short mark low on the line. Anything not read whole is null.
/// A shield shows after the HP in brackets, "current/max(+shield)"; with it added to the bar's lines the bar turns white (the
/// text stays white with dark edges), so on a bright fill the ink is what clears halfway from the fill to white.</summary>
public static class BossDigits
{
    const int MinInk = 150;            // text: 200+; the fills' min channel stays under 100 (teal's bright top row: 90)
    internal const int GridW = 7, GridH = 11;
    const double BoxWidth = 0.64;      // a glyph's box: this share of the line height, centred, so a '1' keeps its place
    // NCC. On the five bosstop shots the worst right digit scores 0.66 and wins by 0.12; '/' and 'X' by far more (BossDigitsTests).
    const double MinGlyph = 0.55;
    // The text's height at HUD 110 1080p is 11–12 px; a line outside this (art, a cutscene) is not the bar's text.
    const double MinLine = 8, MaxLine = 16;
    static readonly Regex HpText = new(@"^(\d{1,3}(?:,\d{3})*)/(\d{1,3}(?:,\d{3})*)(.*)$", RegexOptions.CultureInvariant);
    static readonly Regex ShieldText = new(@"^\D?\+(\d{1,3}(?:,\d{3})*)", RegexOptions.CultureInvariant);

    /// <summary>A glyph: a comma (short, low), a mark (short, high: the shield's '+'), or a full-height one with its grid.</summary>
    internal readonly record struct Glyph(bool Comma, float[]? Grid, bool Mark = false);

    static readonly (char Ch, float[] Grid)[] Templates =
        [.. BossDigitTemplates.Glyphs.Select(t => (t.Ch, t.Rows.SelectMany(r => r.Select(c => (float)DigitReader.GreyRamp.IndexOf(c))).ToArray()))];

    /// <summary>The bar count ("X 494" → 494), or null.</summary>
    public static int? ReadCount(Image area, double scale)
    {
        if (Glyphs(area, scale) is not { Count: >= 2 and <= 6 } g || g.Any(x => x.Comma || x.Mark)) return null;
        if (Best(g[0].Grid!, c => c is 'X' or (>= '0' and <= '9')) is not ('X', >= MinGlyph)) return null;
        var digits = new StringBuilder();
        foreach (var x in g.Skip(1))
            if (Best(x.Grid!, char.IsAsciiDigit) is (var d, >= MinGlyph)) digits.Append(d); else return null;
        return int.Parse(digits.ToString());
    }

    /// <summary>The HP text ("16,897,047,862,607/17,126,566,042,900"), or null. Exactly one glyph is the '/': the best-scoring
    /// one (a '7' next to it can score near it); the rest must be digits, grouped by commas as the game writes them.</summary>
    public static (long Current, long Max)? ReadHp(Image area, double scale) =>
        ReadHpWithShield(area, scale) is var (current, max, _) ? (current, max) : null;

    /// <summary>The HP text with its shield ("…/5,794,751,037,908(+75,331,760,729)" → shield 75,331,760,729; none: 0), or null.
    /// After the max only "(+n)" may follow: the shield's digits are read up to the last full group, so a ')' or '(' read as a
    /// digit doesn't count.</summary>
    public static (long Current, long Max, long Shield)? ReadHpWithShield(Image area, double scale)
    {
        if (Glyphs(area, scale) is not { Count: >= 3 } g) return null;
        var slash = Template('/');
        int at = -1;
        double best = MinGlyph;
        for (int i = 0; i < g.Count; i++)
            if (g[i].Grid is { } grid && Ncc(grid, slash) is var s && s >= best) (at, best) = (i, s);
        if (at < 0) return null;
        var text = new StringBuilder();
        for (int i = 0; i < g.Count; i++)
            if (i == at) text.Append('/');
            else if (g[i].Comma) text.Append(',');
            else if (g[i].Mark) text.Append('+');
            else if (Best(g[i].Grid!, char.IsAsciiDigit) is (var d, >= MinGlyph)) text.Append(d);
            else text.Append('?');   // a bracket, or a misread: only allowed after the max
        if (HpText.Match(text.ToString()) is not { Success: true } m) return null;
        long current = long.Parse(m.Groups[1].Value.Replace(",", "")), max = long.Parse(m.Groups[2].Value.Replace(",", ""));
        long shield = 0;
        if (m.Groups[3].Value is { Length: > 0 } rest)
        {
            if (ShieldText.Match(rest) is not { Success: true } sm) return null;
            shield = long.Parse(sm.Groups[1].Value.Replace(",", ""));
        }
        return current <= max ? (current, max, shield) : null;
    }

    /// <summary>The glyphs left to right, or null: no text, a line of the wrong height, or a short mark that is no comma.</summary>
    internal static List<Glyph>? Glyphs(Image area, double scale)
    {
        int w = area.Width, h = area.Height;
        var grey = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (b, g, r) = area.Pixel(x, y);
                grey[y * w + x] = Math.Min(b, Math.Min(g, r));
            }
        // A white bar (a shield added to the lines): the ink clears halfway from the fill to white, and the text's dark edges
        // are lifted to the fill so the glyphs look as they do on a dark fill.
        int fill = grey.Length == 0 ? 0 : grey.Order().ElementAt(grey.Length / 2), minInk = MinInk;
        if (fill > MinInk - 30)
        {
            minInk = (fill + 255) / 2;
            for (int i = 0; i < grey.Length; i++) grey[i] = Math.Max(grey[i], (byte)fill);
        }
        var runs = new List<(int X0, int X1, int Top, int Bottom)>();
        for (int x = 0, start = -1; x <= w; x++)
        {
            bool ink = x < w && Enumerable.Range(0, h).Any(y => grey[y * w + x] > minInk);
            if (ink && start < 0) start = x;
            if (ink || start < 0) continue;
            var rows = Enumerable.Range(0, h).Where(y => Enumerable.Range(start, x - start).Any(c => grey[y * w + c] > minInk)).ToList();
            runs.Add((start, x, rows[0], rows[^1] + 1));
            start = -1;
        }
        if (runs.Count == 0) return null;
        int tallest = runs.Max(r => r.Bottom - r.Top);
        var tall = runs.Where(r => r.Bottom - r.Top >= 0.6 * tallest).ToList();
        int top = Median(tall.Select(r => r.Top)), bottom = Median(tall.Select(r => r.Bottom)), line = bottom - top;
        if (line < MinLine * scale || line > MaxLine * scale) return null;
        var glyphs = new List<Glyph>(runs.Count);
        foreach (var r in runs)
        {
            if (r.Bottom - r.Top < 0.5 * line)
            {
                // High on the line: the shield's '+' (a speck elsewhere fails the read: only "(+n)" may follow the max).
                glyphs.Add(r.Top <= top + line / 2.0 ? new(false, null, Mark: true) : new(true, null));
                continue;
            }
            double cx = (r.X0 + r.X1) / 2.0, half = line * BoxWidth / 2;
            int x0 = (int)Math.Round(Math.Max(0, cx - half)), x1 = (int)Math.Round(Math.Min(w, cx + half));
            glyphs.Add(new(false, Resample(grey, w, x0, x1, top, bottom)));
        }
        return glyphs;
    }

    static int Median(IEnumerable<int> values)
    {
        var v = values.Order().ToArray();
        return v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
    }

    static float[] Resample(byte[] grey, int stride, int x0, int x1, int top, int bottom) =>
        Resample(i => grey[i], stride, x0, x1, top, bottom, GridW, GridH);

    /// <summary>The box (x0..x1, top..bottom) of a plane (<paramref name="at"/>: the value at an index, rows of
    /// <paramref name="stride"/>) averaged down to <paramref name="gw"/>×<paramref name="gh"/>.</summary>
    internal static float[] Resample(Func<int, float> at, int stride, int x0, int x1, int top, int bottom, int gw, int gh)
    {
        int bw = x1 - x0, bh = bottom - top;
        var grid = new float[gw * gh];
        for (int j = 0; j < gh; j++)
        {
            double ya = j * (double)bh / gh, yb = (j + 1) * (double)bh / gh;
            for (int i = 0; i < gw; i++)
            {
                double xa = i * (double)bw / gw, xb = (i + 1) * (double)bw / gw, sum = 0, area = 0;
                for (int y = (int)ya; y < Math.Ceiling(yb); y++)
                {
                    double wy = Math.Min(yb, y + 1) - Math.Max(ya, y);
                    for (int x = (int)xa; x < Math.Ceiling(xb); x++)
                    {
                        double wxy = (Math.Min(xb, x + 1) - Math.Max(xa, x)) * wy;
                        sum += at((top + y) * stride + x0 + x) * wxy;
                        area += wxy;
                    }
                }
                grid[j * gw + i] = (float)(sum / area);
            }
        }
        return grid;
    }

    static float[] Template(char ch) => Templates.FirstOrDefault(t => t.Ch == ch).Grid ?? new float[GridW * GridH];

    static (char Ch, double Score) Best(float[] grid, Func<char, bool> among)
    {
        (char, double) best = ('?', double.NegativeInfinity);
        foreach (var (ch, t) in Templates)
            if (among(ch) && Ncc(grid, t) is var s && s > best.Item2) best = (ch, s);
        return best;
    }

    internal static double Ncc(float[] a, float[] b)
    {
        double ma = a.Average(), mb = b.Average(), ab = 0, aa = 0, bb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double da = a[i] - ma, db = b[i] - mb;
            (ab, aa, bb) = (ab + da * db, aa + da * da, bb + db * db);
        }
        return aa > 0 && bb > 0 ? ab / Math.Sqrt(aa * bb) : 0;
    }
}
