using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>What the 전분 (전투분석기) panel shows for me: 피해량 (조력 피해량 for a support), DPS, its timer in seconds, and
/// whether it is the support view.</summary>
public sealed record JeonbunReading(long Damage, long Dps, int Timer, bool Support);

/// <summary>What a 전분 column holds, by its title: the player picks the four columns and their order. Only 피해량 (조력 피해량
/// for a support) and DPS are read; 1분 피해량, 1분 DPS, the 유효율 columns and any other are 기타.</summary>
public enum JeonbunColumn { 피해량, DPS, 조력피해량, 기타 }

/// <summary>Reads the 전분 panel (spec § C Inputs) from its crop (MeterPanels: 470 wide at 1080p and 114 high, room for a second
/// row; it scales with the screen's height, never the HUD size): the yellow "MM : SS" timer and my row's four cells (피해량 · DPS · 1분 피해량 · 1분 DPS, or for a support
/// 조력 피해량 · 낙인 · 공격 증가 · 피해 증가 유효율). The panel is see-through, so a cell's text is the near-white ink brighter
/// than that cell's own background (a flash behind shows through tinted by the cell); the timer is ink far yellower than blue.
/// With a saved record to compare (the toggle on) there are two rows, ordered by damage: mine is the one whose first cell is
/// green (the saved one's is grey). Glyphs are column runs scored by NCC against JeonbunTemplates, as BossDigits does.
/// Anything not read whole is null.</summary>
public static class JeonbunReader
{
    public const double RefW = 470, RefH = 114;   // the crop at 1080p; every box below is in its pixels, scaled by the width
    static readonly (double X0, double X1, double Y0, double Y1) TimerBox = (236, 306, 3, 29);
    static readonly double[] CellX = [2, 118, 235, 352, 468];
    const double CellY0 = 62, CellY1 = 87, RowPitch = 26, CellInset = 3;
    const double MinLine = 8, MaxLine = 18;   // the text's height at 1080p is 11–13 px
    const double MinGlyph = 0.55;
    const double TitleY0 = 33, TitleY1 = 60;   // the column titles' line, between the timer and the first row
    internal const int TitleW = 56, TitleH = 9;
    const double MinTitle = 0.85;   // the right title scores 0.98+, the nearest other that matters 0.66
    const double MaxGlyphWidth = 1.25;        // in line heights: 억 and % are about 1, two touching digits about 1.4

    /// <summary>One glyph: a short mark ('m' low on the line: a dot or comma, '–' mid-line) or a grid to match, and whether it
    /// is narrow (under half the line's height: only '1' and ':' are, which keeps a '4' from matching a '1' by its stem).</summary>
    public readonly record struct Glyph(char? Mark, float[]? Grid, bool Narrow = false);

    static readonly (char Ch, float[] Grid)[] TimerTemplates = Load(JeonbunTemplates.Timer);
    static readonly (char Ch, float[] Grid)[] ValueTemplates = Load(JeonbunTemplates.Value);
    static readonly (JeonbunColumn Kind, float[] Grid)[] TitleTemplates =
        [.. JeonbunTitles.All.Select(t => (KindOf(t.Name), t.Rows.SelectMany(r => r.Select(c => (float)DigitReader.GreyRamp.IndexOf(c) * 28.3f)).ToArray()))];

    internal static JeonbunColumn KindOf(string title) => title switch
    {
        "피해량" => JeonbunColumn.피해량,
        "DPS" => JeonbunColumn.DPS,
        "조력 피해량" => JeonbunColumn.조력피해량,
        _ => JeonbunColumn.기타,
    };

    static (char, float[])[] Load((char Ch, string[] Rows)[] set) =>
        [.. set.Select(t => (t.Ch, t.Rows.SelectMany(r => r.Select(c => (float)DigitReader.GreyRamp.IndexOf(c) * 28.3f)).ToArray()))];

    static readonly Regex Clock = new(@"^(\d{2}):(\d{2})$", RegexOptions.CultureInvariant);
    static readonly Regex AmountText = new(@"^(\d{1,3}(?:,\d{3})*)(?:\.(\d{2})(억|만))?$", RegexOptions.CultureInvariant);

    /// <summary>My figures and the timer, or null: no panel there, covered, or not my row (the 보관 기록 view).
    /// The columns are found by their titles (<see cref="Columns"/>); a DPS column missing is the damage over the timer, a
    /// damage column missing is DPS times the timer. A column there but not read whole (a digit mid-change) gives no reading.</summary>
    public static JeonbunReading? Read(Image panel) => Read(panel, soft: false);

    /// <summary>테스트 모드 강화 읽기: <see cref="Read"/> on video whose scaling and compression lose the small marks (720p: the
    /// timer's ':' and an amount's '.' and ',' fade under the ink threshold while the digits stay). They are put back from the
    /// format: the timer is always mm:ss, an amount in 억 / 만 always has two decimals. The game reads with <see cref="Read"/>.</summary>
    public static JeonbunReading? ReadSoft(Image panel, int? clock = null) => ReadHinted(panel, clock).Reading;

    /// <summary><see cref="ReadSoft"/>, and whether its timer is <paramref name="clock"/>'s (the panel's own didn't read).</summary>
    internal static (JeonbunReading? Reading, bool Hinted) ReadHinted(Image panel, int? clock)
    {
        var r = Read(panel, soft: true, clock, out bool hinted);
        return (r, hinted);
    }

    static JeonbunReading? Read(Image panel, bool soft) => Read(panel, soft, null, out _);

    static JeonbunReading? Read(Image panel, bool soft, int? clockHint, out bool hinted)
    {
        _soft = soft;
        try { return ReadAs(panel, soft, clockHint, out hinted); }
        finally { _soft = false; }
    }

    /// <summary>강화: a digit counts from <see cref="SoftGlyph"/> (blur lowers every score; the format checks the rest).</summary>
    [ThreadStatic] static bool _soft;
    const double SoftGlyph = 0.4;
    // 강화: a stream's blur greys the timer's yellow (discord_전분_sample.png: pale, its strokes under the game's floor of 60).
    const int SoftYellowFloor = 50;

    /// <param name="clockHint">강화: the timer the fight's clock expects, used when the panel's own doesn't read.</param>
    static JeonbunReading? ReadAs(Image panel, bool soft, int? clockHint, out bool hinted)
    {
        hinted = false;
        string? clock = soft ? SoftClock(panel) : TimerText(panel);
        int timer;
        if (clock is not null && Clock.Match(clock) is { Success: true } m) timer = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
        else if (clockHint is { } hint) (timer, hinted) = (hint, true);
        else return null;
        var kinds = Columns(panel);
        int damageAt = Array.FindIndex(kinds, k => k is JeonbunColumn.피해량 or JeonbunColumn.조력피해량), dpsAt = Array.IndexOf(kinds, JeonbunColumn.DPS);
        if (damageAt < 0 && dpsAt < 0) return null;
        var cells = Cells(panel);
        Func<string, long?> amount = soft ? SoftAmount : Amount;
        long? damage = damageAt >= 0 && cells[damageAt] is { } d ? amount(d) : null;
        long? dps = dpsAt >= 0 && cells[dpsAt] is { } p ? amount(p) : null;
        if (damageAt >= 0 && damage is null || dpsAt >= 0 && dps is null) return null;
        damage ??= dps!.Value * timer;
        dps ??= timer > 0 ? damage.Value / timer : 0;
        return new(damage.Value, dps.Value, timer, damageAt >= 0 && kinds[damageAt] == JeonbunColumn.조력피해량);
    }

    /// <summary>Each of the four columns' kind by its title (기타 when the title isn't one read or isn't read at all).</summary>
    public static JeonbunColumn[] Columns(Image panel) =>
        [.. Enumerable.Range(0, 4).Select(c => Classify(panel, c))];

    const int TitleShift = 3;   // px up or down tried: the panel's crop (a found spot) can sit a pixel or two off

    static JeonbunColumn Classify(Image panel, int column)
    {
        var (kind, score) = (JeonbunColumn.기타, double.NegativeInfinity);
        for (int dy = -TitleShift; dy <= TitleShift; dy++)
            if (TitleGrid(panel, column, dy) is { } grid)
                foreach (var t in TitleTemplates)
                    if (BossDigits.Ncc(grid, t.Grid) is var s && s > score) (kind, score) = (t.Kind, s);
        return score >= MinTitle ? kind : JeonbunColumn.기타;
    }

    /// <summary>A column's title as ink in a box of the column's size centred on the text (the whole box is compared, not a cut
    /// around the ink, which a speck or a flash behind would move), scaled to <see cref="TitleW"/>×<see cref="TitleH"/>; null: no text.</summary>
    internal static float[]? TitleGrid(Image panel, int column, int dy = 0)
    {
        double s = Scale(panel);
        var (bx0, bx1, by0, by1) = Box(CellX[column] + CellInset, CellX[column + 1] - CellInset, TitleY0, TitleY1, s);
        (by0, by1) = (by0 + dy, by1 + dy);
        int x0 = Math.Max(0, bx0), x1 = Math.Min(panel.Width, bx1), y0 = Math.Max(0, by0), y1 = Math.Min(panel.Height, by1);
        int w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0) return null;
        // Medians from counts, not sorted copies (28 boxes per panel read, perf-trace): the same values.
        var ink = new int[w * h];
        Span<int> levels = stackalloc int[256];   // WhiteInk is 0..255
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (b, g, r) = panel.Pixel(x0 + x, y0 + y);
                levels[ink[y * w + x] = WhiteInk(b, g, r)]++;
            }
        int threshold = Math.Max(110, KthOfCounts(levels, ink.Length / 2) + 60);
        Span<int> columns = w <= 512 ? stackalloc int[w] : new int[w];   // the inked pixels per column
        int on = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (ink[y * w + x] > threshold) { columns[x]++; on++; }
        if (on < MinLine * s) return null;
        // Centred across on the ink's median (a speck doesn't move it, a crop a few pixels off does), the box's size kept.
        int medianX = on % 2 == 1 ? KthOfCounts(columns, on / 2) : (KthOfCounts(columns, on / 2 - 1) + KthOfCounts(columns, on / 2)) / 2;
        int dx = medianX - w / 2;   // across only (down, the caller tries a few offsets)
        float At(int i)
        {
            int x = i % w + dx, y = i / w;
            return x < 0 || y < 0 || x >= w || y >= h || ink[y * w + x] <= threshold ? 0 : ink[y * w + x];
        }
        return BossDigits.Resample(At, w, 0, w, 0, h, TitleW, TitleH);
    }

    /// <summary>An amount as the panel writes it ("28.57억", "5,985.77만", "–" for none yet), or null.</summary>
    public static long? Amount(string text)
    {
        if (text == "–") return 0;
        if (AmountText.Match(text) is not { Success: true } m) return null;
        decimal whole = decimal.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
        if (!m.Groups[3].Success) return (long)whole;
        decimal value = whole + decimal.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100;
        return (long)(value * (m.Groups[3].Value == "억" ? 100_000_000m : 10_000m));
    }

    /// <summary><see cref="Amount"/>, else the digits and the unit with the '.' and ',' the format puts there ("221109억" →
    /// "2,211.09억"): those marks are a few pixels and fade first.</summary>
    internal static long? SoftAmount(string text)
    {
        if (Amount(text) is { } read) return read;
        string digits = new([.. text.Where(char.IsAsciiDigit)]);
        string unit = text.EndsWith('억') ? "억" : text.EndsWith('만') ? "만" : "";
        if (digits.Length == 0 || text.Any(c => !char.IsAsciiDigit(c) && c is not (',' or '.') && $"{c}" != unit)) return null;
        if (unit.Length > 0 && digits.Length < 3) return null;
        string whole = unit.Length > 0 ? digits[..^2] : digits;
        string grouped = long.Parse(whole).ToString("N0", CultureInfo.InvariantCulture);
        return Amount(unit.Length > 0 ? $"{grouped}.{digits[^2..]}{unit}" : grouped);
    }

    static readonly (char Ch, float[] Grid)[] TimerDigits = [.. TimerTemplates.Where(t => char.IsAsciiDigit(t.Ch))];

    /// <summary>강화: the clock from its digits alone. Blur breaks the colon into a low mark, a dot or nothing and thins a '1'
    /// into a colon's width, so marks are dropped, of five glyphs the middle is the colon, and each digit is scored against
    /// every digit. Null unless four digits read.</summary>
    static string? SoftClock(Image panel)
    {
        if (TimerGlyphs(panel) is not { } g) return null;
        var grids = g.Where(x => x.Grid is not null).ToList();
        if (grids.Count == 5) grids.RemoveAt(2);
        if (grids.Count != 4) return null;
        var digits = grids.Select(x => Best(x.Grid!, TimerDigits, x.Narrow, any: true)).ToList();
        if (digits.Any(d => d.Score < SoftGlyph)) return null;
        var t = new string([.. digits.Select(d => d.Ch)]);
        return $"{t[..2]}:{t[2..]}";
    }

    /// <summary>The timer as shown ("02:21"), or null.</summary>
    public static string? TimerText(Image panel) =>
        TimerGlyphs(panel) is { } g ? Text(g, TimerTemplates, marks: false) : null;

    /// <summary>My row's four cells as shown, each null when not read whole.</summary>
    public static string?[] Cells(Image panel) =>
        [.. Enumerable.Range(0, 4).Select(c => CellGlyphs(panel, c) is { } g ? Text(g, ValueTemplates, marks: true) : null)];

    /// <summary>테스트 모드's 전분 search: whether <paramref name="panel"/> is the panel with my row at <paramref name="row"/>, read
    /// from its frame, not its figures: the timer (강화), a 피해량 or DPS title and my green row. A stream's blur can lose one
    /// digit of an amount (the green cell's '8' in discord_전분_sample.png) while the panel is plainly there.</summary>
    public static bool Shows(Image panel, int row)
    {
        bool was = _soft;
        _soft = true;
        try
        {
            return MyRow(panel) == row && SoftClock(panel) is not null
                && Columns(panel).Any(k => k is JeonbunColumn.피해량 or JeonbunColumn.조력피해량 or JeonbunColumn.DPS);
        }
        finally { _soft = was; }
    }

    static double Scale(Image panel) => panel.Width / RefW;

    internal static List<Glyph>? TimerGlyphs(Image panel)
    {
        double s = Scale(panel);
        return Glyphs(panel, Box(TimerBox.X0, TimerBox.X1, TimerBox.Y0, TimerBox.Y1, s), YellowInk, floor: _soft ? SoftYellowFloor : 60, margin: 40, s);
    }

    internal static List<Glyph>? CellGlyphs(Image panel, int cell)
    {
        double s = Scale(panel);
        if (MyRow(panel, s) is not { } row) return null;
        double y0 = CellY0 + row * RowPitch;
        return Glyphs(panel, Box(CellX[cell] + CellInset, CellX[cell + 1] - CellInset, y0, y0 + CellY1 - CellY0, s), WhiteInk, floor: 150, margin: 70, s);
    }

    /// <summary>The row whose first cell is green (mine; a saved record's is grey), 0 or 1, or null: none in the crop.</summary>
    internal static int? MyRow(Image panel) => MyRow(panel, Scale(panel));

    static int? MyRow(Image panel, double s)
    {
        for (int row = 0; row < 2; row++)
        {
            var (x0, x1, y0, y1) = Box(CellX[0] + CellInset, CellX[1] - CellInset, CellY0 + row * RowPitch, CellY1 + row * RowPitch, s);
            if (y1 > panel.Height) break;
            long r = 0, g = 0, b = 0, n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var (pb, pg, pr) = panel.Pixel(x, y);
                    if (Math.Min(pb, Math.Min(pg, pr)) > 120) continue;   // the text, not the fill
                    (r, g, b, n) = (r + pr, g + pg, b + pb, n + 1);
                }
            if (n > 0 && g > r + 25 * n && g > b) return row;
        }
        return null;
    }

    static (int X0, int X1, int Y0, int Y1) Box(double x0, double x1, double y0, double y1, double s) =>
        ((int)Math.Round(x0 * s), (int)Math.Round(x1 * s), (int)Math.Round(y0 * s), (int)Math.Round(y1 * s));

    // White text raises every channel; a cell's fill keeps one low, and a flash behind lifts the whole cell (the threshold
    // follows the cell's own level), so the lowest channel is the ink.
    static int WhiteInk(byte b, byte g, byte r) => Math.Min(b, Math.Min(g, r));

    static int YellowInk(byte b, byte g, byte r) => Math.Max(0, Math.Min(g, r) - b);

    /// <summary>The text in the box as glyphs, or null: no line of text's height, or a speck that is no mark.</summary>
    static List<Glyph>? Glyphs(Image img, (int X0, int X1, int Y0, int Y1) box, Func<byte, byte, byte, int> inkOf, int floor, int margin, double s)
    {
        int x0 = Math.Max(0, box.X0), x1 = Math.Min(img.Width, box.X1), y0 = Math.Max(0, box.Y0), y1 = Math.Min(img.Height, box.Y1);
        int w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0) return null;
        var ink = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (b, g, r) = img.Pixel(x0 + x, y0 + y);
                ink[y * w + x] = inkOf(b, g, r);
            }
        var sorted = ink.Order().ToArray();
        int threshold = Math.Max(floor, sorted[sorted.Length / 2] + margin);   // the box's own background, whatever shows through
        bool On(int x, int y) => ink[y * w + x] > threshold;

        var runs = new List<(int X0, int X1, int Top, int Bottom)>();
        for (int x = 0, start = -1; x <= w; x++)
        {
            bool any = x < w && Enumerable.Range(0, h).Any(y => On(x, y));
            if (any && start < 0) start = x;
            if (any || start < 0) continue;
            var rows = Enumerable.Range(0, h).Where(y => Enumerable.Range(start, x - start).Any(c => On(c, y))).ToList();
            runs.Add((start, x, rows[0], rows[^1] + 1));
            start = -1;
        }
        if (runs.Count == 0) return null;
        int tallest = runs.Max(r => r.Bottom - r.Top);
        if (runs is [var only] && tallest < MinLine * s / 2 && only.X1 - only.X0 >= 3 * tallest)
            return [new('–', null)];   // a lone dash: nothing dealt yet
        var tall = runs.Where(r => r.Bottom - r.Top >= 0.6 * tallest).ToList();
        int top = Median(tall.Select(r => r.Top)), bottom = Median(tall.Select(r => r.Bottom)), line = bottom - top;
        if (line < MinLine * s || line > MaxLine * s) return null;
        runs = [.. runs.SelectMany(r => Split(r, line, On))];

        var glyphs = new List<Glyph>(runs.Count);
        foreach (var r in runs)
        {
            if (r.Bottom - r.Top < 0.5 * line)
            {
                double mid = (r.Top + r.Bottom) / 2.0;
                if (mid > top + 0.65 * line) glyphs.Add(new('m', null));
                else if (mid > top + 0.3 * line && r.X1 - r.X0 >= 0.3 * line) glyphs.Add(new('–', null));
                else return null;   // a speck, not part of the text
                continue;
            }
            double cx = (r.X0 + r.X1) / 2.0, half = Math.Max(r.X1 - r.X0, line * 0.64) / 2;
            int gx0 = (int)Math.Round(Math.Max(0, cx - half)), gx1 = (int)Math.Round(Math.Min(w, cx + half));
            int gt = Math.Min(top, r.Top), gb = Math.Max(bottom, r.Bottom);
            glyphs.Add(new(null, BossDigits.Resample(i => ink[i], w, gx0, gx1, gt, gb, BossDigits.GridW, BossDigits.GridH),
                r.X1 - r.X0 < 0.5 * line));
        }
        return glyphs;
    }

    /// <summary>The glyphs as text: grids by their best template, a dash as '–', and (with <paramref name="marks"/>) the low
    /// marks as ',' except a last one followed by exactly two digits, which is the decimal point. Null when any grid scores low.</summary>
    static string? Text(List<Glyph> glyphs, (char Ch, float[] Grid)[] templates, bool marks)
    {
        var sb = new StringBuilder();
        foreach (var g in glyphs)
        {
            if (g.Mark is { } mark)
            {
                if (!marks) return null;
                sb.Append(mark);
                continue;
            }
            var (ch, score) = Best(g.Grid!, templates, g.Narrow, any: _soft);
            if (score < (_soft ? SoftGlyph : MinGlyph)) return null;
            sb.Append(ch);
        }
        var text = sb.ToString();
        int last = text.LastIndexOf('m');
        if (last >= 0 && last + 2 < text.Length && char.IsAsciiDigit(text[last + 1]) && char.IsAsciiDigit(text[last + 2])
            && (last + 3 == text.Length || !char.IsAsciiDigit(text[last + 3])))
            text = text[..last] + '.' + text[(last + 1)..];
        return text.Replace('m', ',');
    }

    /// <summary>The best template; a narrow glyph is only '1' or ':' and a wide one never, unless <paramref name="any"/> (강화:
    /// blur fades a '7''s slant, leaving its stem narrow).</summary>
    static (char Ch, double Score) Best(float[] grid, (char Ch, float[] Grid)[] templates, bool narrow, bool any = false)
    {
        (char, double) best = ('?', double.NegativeInfinity);
        foreach (var (ch, t) in templates)
            if ((any || ch is '1' or ':' == narrow) && BossDigits.Ncc(grid, t) is var s && s > best.Item2) best = (ch, s);
        return best;
    }

    /// <summary>A run too wide for one glyph (touching digits, "38") cut at its faintest column, until each part fits.</summary>
    static IEnumerable<(int X0, int X1, int Top, int Bottom)> Split((int X0, int X1, int Top, int Bottom) r, int line, Func<int, int, bool> on)
    {
        if (r.X1 - r.X0 <= MaxGlyphWidth * line) { yield return r; yield break; }
        int lo = r.X0 + (r.X1 - r.X0) / 3, hi = r.X1 - (r.X1 - r.X0) / 3;
        int cut = Enumerable.Range(lo, Math.Max(1, hi - lo)).MinBy(x => Enumerable.Range(r.Top, r.Bottom - r.Top).Count(y => on(x, y)));
        foreach (var part in new[] { (r.X0, cut), (cut, r.X1) })
        {
            var rows = Enumerable.Range(r.Top, r.Bottom - r.Top).Where(y => Enumerable.Range(part.Item1, part.Item2 - part.Item1).Any(x => on(x, y))).ToList();
            if (rows.Count == 0) continue;
            foreach (var p in Split((part.Item1, part.Item2, rows[0], rows[^1] + 1), line, on)) yield return p;
        }
    }

    /// <summary>The <paramref name="k"/>-th smallest (0-based) of the values counted in <paramref name="counts"/> (counts[v] = how many are v).</summary>
    internal static int KthOfCounts(ReadOnlySpan<int> counts, int k)
    {
        for (int v = 0, seen = 0; v < counts.Length; v++)
            if ((seen += counts[v]) > k) return v;
        return counts.Length - 1;
    }

    static int Median(IEnumerable<int> values)
    {
        var v = values.Order().ToArray();
        return v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
    }
}
