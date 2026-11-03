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

    /// <summary>The 그림자 레이드 gimmick's number (the damage dealt in it, "134,046,661,186"), or null: digits grouped by commas,
    /// nothing else (the bar's "current/max" fails on its '/'). It pops up to half as big again as it jumps.</summary>
    public static long? ReadNumber(Image area, double scale)
    {
        if (Glyphs(area, scale * NumberPop) is not { Count: >= 5 } g || g.Any(x => x.Mark)) return null;
        var text = new StringBuilder();
        foreach (var x in g)
            if (x.Comma) text.Append(',');
            else if (Best(x.Grid!, char.IsAsciiDigit) is (var d, >= MinGlyph)) text.Append(d);
            else return null;
        // Specks of the fill bar's frame below the line read as commas at either end (shadow_number 8, 19): not the number's.
        string number = text.ToString().Trim(',');
        return Grouped.IsMatch(number) ? long.Parse(number.Replace(",", "")) : null;
    }
    const double NumberPop = 1.5;
    static readonly Regex Grouped = new(@"^\d{1,3}(?:,\d{3})+$", RegexOptions.CultureInvariant);

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

    // Between words ("Lv.60", the HP, "X n"): wider than the space in "X 494" and any gap inside the HP text, at HUD 110 1080p.
    const double WordGap = 11;

    /// <summary>The bar's whole text row (Geometry.BossTextRow): its words split at wide gaps, each read as "X n" and as the HP,
    /// so neither depends on where the bar starts or ends (21:9, the HUD).</summary>
    /// <param name="hp">false: the count alone (read every poll for the 줄 수 창): the HP words aren't read, and the right-most
    /// words are tried first, where "X n" is.</param>
    public static (int? Count, (long Current, long Max, long Shield)? Hp, PixelRect? CountArea) ReadBar(Image row, double scale, bool hp = true)
    {
        int? count = null;
        (long, long, long)? read = null;
        PixelRect? countArea = null;
        var words = Words(row, scale).ToList();
        if (!hp) words.Reverse();
        foreach (var (word, at) in words)
        {
            if (count is null && word.Width <= MaxCountWidth * scale && ReadCount(word, scale) is { } c) (count, countArea) = (c, at);   // 화면 그대로 shows this part
            if (hp) read ??= ReadHpWithShield(word, scale);
            else if (count is not null) break;
        }
        return (count, read, countArea);
    }

    // "X 12345" at HUD 110 1080p is under 60 px: a wider word is the HP text, not tried as a count.
    const double MaxCountWidth = 70;

    /// <summary>The row cut at every run of blank columns wider than <see cref="WordGap"/> (a column margin kept each side).</summary>
    static IEnumerable<(Image Word, PixelRect At)> Words(Image row, double scale)
    {
        var (grey, minInk) = Ink(row);
        int w = row.Width, h = row.Height, gap = (int)Math.Ceiling(WordGap * scale);
        bool Inked(int x)
        {
            for (int y = 0; y < h; y++) if (grey[y * w + x] > minInk) return true;
            return false;
        }
        for (int x = 0, start = -1, last = -1; x <= w; x++)
        {
            bool ink = x < w && Inked(x);
            if (ink) { if (start < 0) start = x; last = x; continue; }
            if (start < 0 || (x < w && x - last <= gap)) continue;
            int x0 = Math.Max(0, start - 1), x1 = Math.Min(w, last + 2);
            var at = new PixelRect(x0, 0, x1 - x0, h);
            yield return (row.Crop(at), at);
            start = -1;
        }
    }

    /// <summary>Each pixel's min(R,G,B), and the level text clears. A white bar (a shield added to the lines): the ink clears
    /// halfway from the fill to white, and the text's dark edges are lifted to the fill so the glyphs look as on a dark fill.</summary>
    static (byte[] Grey, int MinInk) Ink(Image area)
    {
        int w = area.Width, h = area.Height;
        var grey = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (b, g, r) = area.Pixel(x, y);
                grey[y * w + x] = Math.Min(b, Math.Min(g, r));
            }
        int fill = MedianByte(grey), minInk = MinInk;
        if (fill > MinInk - 30)
        {
            minInk = (fill + 255) / 2;
            for (int i = 0; i < grey.Length; i++) grey[i] = Math.Max(grey[i], (byte)fill);
        }
        return (grey, minInk);
    }

    /// <summary>The glyphs left to right, or null: no text, a line of the wrong height, or a short mark that is no comma.</summary>
    internal static List<Glyph>? Glyphs(Image area, double scale)
    {
        int w = area.Width, h = area.Height;
        var (grey, minInk) = Ink(area);
        // The ink runs between blank columns, a column's ink looked for in rows y0..y1 only.
        List<(int X0, int X1, int Top, int Bottom)> Runs(int y0, int y1)
        {
            var found = new List<(int X0, int X1, int Top, int Bottom)>();
            for (int x = 0, start = -1; x <= w; x++)
            {
                bool ink = x < w && Enumerable.Range(y0, y1 - y0).Any(y => grey[y * w + x] > minInk);
                if (ink && start < 0) start = x;
                if (ink || start < 0) continue;
                var rows = Enumerable.Range(y0, y1 - y0).Where(y => Enumerable.Range(start, x - start).Any(c => grey[y * w + c] > minInk)).ToList();
                found.Add((start, x, rows[0], rows[^1] + 1));
                start = -1;
            }
            // A run taller than any text (a bright tick or highlight of the bar, bosstop_2560) is no glyph and sets no line height.
            return [.. found.Where(r => r.Bottom - r.Top <= MaxLine * scale)];
        }
        var runs = Runs(0, h);
        if (runs.Count == 0) return null;
        int tallest = runs.Max(r => r.Bottom - r.Top);
        var tall = runs.Where(r => r.Bottom - r.Top >= 0.6 * tallest).ToList();
        int top = Median(tall.Select(r => r.Top)), bottom = Median(tall.Select(r => r.Bottom)), line = bottom - top;
        if (line < MinLine * scale || line > MaxLine * scale) return null;
        // Again within the line's rows (commas below it): a bright speck of the bar above or below the text would join two
        // glyphs' columns (a taller area over a compressed video: bosstop_2560's "954").
        runs = Runs(Math.Max(0, top - 1), Math.Min(h, bottom + (int)Math.Ceiling(line * 0.4)));
        if (runs.Count == 0) return null;
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

    /// <summary>The middle value of <paramref name="v"/> by a histogram (no sort: the whole row is read every poll).</summary>
    static int MedianByte(byte[] v)
    {
        if (v.Length == 0) return 0;
        Span<int> counts = stackalloc int[256];
        foreach (byte b in v) counts[b]++;
        for (int i = 0, seen = 0; i < 256; i++)
            if ((seen += counts[i]) > v.Length / 2) return i;
        return 255;
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
