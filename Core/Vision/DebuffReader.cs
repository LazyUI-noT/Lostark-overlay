using System.Globalization;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Finds 모르둠's debuffs (PLAN §4 디버프) in the debuff row, and the boss's red and blue sigil counts under its
/// HP bar. The icons are matched as the buffs are (<see cref="BuffReader"/>: structure and hue, frame left out), so the
/// red debuff frame and the boss's green frame don't matter; the timers are the buff row's font.</summary>
public static class DebuffReader
{
    static readonly Image[] Templates = [.. DebuffTemplates.Icons.Select(s => Image.FromBytes(Convert.FromBase64String(s))!)];
    static readonly float[][] Thumbs = [.. Templates.Select(BuffReader.Thumb)];
    static readonly double[] Hues = [.. Templates.Select(t => SlotClassifier.ColourStats(t).MeanHue)];
    // The red sigil is the 결투자's closest look-alike (0.64–0.71 of it, hue 0°, at every size tried; DuelistTests). A glare
    // that drops a 결투자 under its bar would then read as the red sigil, so 결투자 is a look-alike of 빨강: a box that
    // resembles it as much as the sigil is neither.
    static readonly (int Of, float[] Thumb)[] LookAlikes = [((int)DebuffKind.빨강, Thumbs[(int)DebuffKind.결투자])];
    const double MinSwap = 0.85, MinDuel = 0.85;

    /// <summary>The icon of <paramref name="kind"/> as the game draws it, frame left out (25×25 at 1080p, buff 120%).</summary>
    public static Image Icon(DebuffKind kind) => Templates[(int)kind];

    /// <summary>The debuff in the icon at <paramref name="icon"/> (relative to <paramref name="area"/>), or null.</summary>
    public static DebuffKind? Classify(Image area, PixelRect icon) => Classify(area, icon, out _);

    /// <summary><see cref="Classify(Image, PixelRect)"/>, and the 결투자 template's structure score in the box (computed by
    /// the match anyway), for the debug window's near misses (<see cref="DebugText"/>).</summary>
    public static DebuffKind? Classify(Image area, PixelRect icon, out double duelScore)
    {
        var (structure, hue, lookAlike) = BuffReader.Match(area, icon, Thumbs, Hues, LookAlikes);
        // The swap's red and blue halves average to no stable hue (40–60° off its own template in 18-22.mp4), so its
        // structure, per colour channel, tells it alone, at a bar of its own: the swap scores 0.98+, other icons and
        // the background 0.62 at most (18-22.mp4, 480 frames).
        int swap = (int)DebuffKind.교체;
        hue[swap] = structure[swap] >= MinSwap ? 0 : double.MaxValue;
        // The 결투자's own icon scores 0.96+ at every size tried (reference/결투자.png rescaled 0.7–2×), what is not it 0.76 at
        // most (18-22.mp4, 16640 boxes; the lower half of the mordum shots 0.71): a bar between, since one false reading
        // starts a 30 s countdown.
        int duel = (int)DebuffKind.결투자;
        duelScore = structure[duel];
        if (duelScore < MinDuel) hue[duel] = double.MaxValue;
        return BuffReader.Best((structure, hue, lookAlike)) is { } k ? (DebuffKind)k : null;
    }

    /// <summary>Whether the box holds a debuff icon of any kind, known or not: its frame is red (every debuff's is). The engine
    /// reads a box only when every box left of it is filled (debuffs fill the row from the left).</summary>
    public static bool Filled(Image area, PixelRect icon) => RedFrame(area, icon) >= MinFrame;

    /// <summary>The top (strip px) of the boss's icon row in <paramref name="strip"/> (Geometry.BossRowSearch, captured at
    /// <paramref name="scale"/> screen px per reference px): the line where the most icon-sized windows have a strongly coloured frame
    /// (<see cref="WarmFrame"/>); null when fewer than <see cref="MinRowIcons"/> do. Blue-led colour is left out: the scene's swirls
    /// behind the row are blue and won over the row at 90% and 100% once the strip reached below it (bitam_90, bitam_100).</summary>
    public static int? FindRowY(Image strip, double scale) =>
        RowLines(ToReference(strip, scale)).FirstOrDefault(l => l.Count >= MinRowIcons) is { Count: > 0 } l ? (int)Math.Round(l.Y * scale) : null;

    /// <summary>Up to <paramref name="max"/> row lines (strip px) best first, one coloured frame enough, each
    /// <see cref="LineApart"/> reference px from the others: where to look when the best line alone misleads (a single debuff
    /// beside dim buff frames at 90%: bitam_90).</summary>
    public static IReadOnlyList<int> FindRowYs(Image strip, double scale, int max = 3) => RowYs(ToReference(strip, scale), scale, max);

    /// <summary><see cref="FindRowYs"/> on the strip already at reference size.</summary>
    internal static IReadOnlyList<int> RowYs(Image r, double scale, int max = 3)
    {
        var picked = new List<int>();
        foreach (var l in RowLines(r))
        {
            if (picked.Count == max) break;
            if (picked.All(y => Math.Abs(y - l.Y) >= LineApart)) picked.Add(l.Y);
        }
        return [.. picked.Select(y => (int)Math.Round(y * scale))];
    }

    const int LineApart = 6;

    /// <summary>Each line (reference px) with its count of coloured frames and their total share, most first.</summary>
    static IEnumerable<(int Y, int Count, double Sum)> RowLines(Image r)
    {
        // WarmFrame for every window at once: the warm pixels' summed-area table, each band = its box less the inner box (it ran
        // 350 ms per strip window by window, every icon read; replay 2026-11-01).
        int w = r.Width, h = r.Height, band = Math.Max(1, (int)Math.Round(BossIcon * 2 / 31.0)), inner = BossIcon - 2 * band;
        var sat = new int[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
            for (int x = 0, row = 0; x < w; x++)
            {
                int i = y * r.Stride + x * 4, b = r.Bgra[i], g = r.Bgra[i + 1], rr = r.Bgra[i + 2];
                int hi = Math.Max(rr, Math.Max(g, b)), lo = Math.Min(rr, Math.Min(g, b));
                row += hi >= 90 && hi - lo >= 50 && b < hi ? 1 : 0;
                sat[(y + 1) * (w + 1) + x + 1] = sat[y * (w + 1) + x + 1] + row;
            }
        int Sum(int x, int y, int side) => sat[(y + side) * (w + 1) + x + side] - sat[y * (w + 1) + x + side] - sat[(y + side) * (w + 1) + x] + sat[y * (w + 1) + x];
        double area = BossIcon * BossIcon - inner * inner;
        var lines = new List<(int Y, int Count, double Sum)>();
        for (int y = 0; y + BossIcon <= h; y++)
        {
            int n = 0;
            double sum = 0;
            for (int x = 0; x + BossIcon <= w; x += BossStep)
                if ((Sum(x, y, BossIcon) - Sum(x + band, y + band, inner)) / area is var f && f >= MinColourFrame) (n, sum) = (n + 1, sum + f);
            if (n > 0) lines.Add((y, n, sum));
        }
        return lines.OrderByDescending(l => l.Count).ThenByDescending(l => l.Sum);
    }

    const double MinColourFrame = 0.5;
    const int MinRowIcons = 2;

    /// <summary>The strip resampled to reference pixels (the boss icon <see cref="BossIcon"/> px).</summary>
    internal static Image ToReference(Image strip, double scale) =>
        Math.Abs(scale - 1) < 0.01 ? strip : ImageOps.Resample(strip, new PixelRect(0, 0, strip.Width, strip.Height), 1 / scale);

    /// <summary>The share of an icon's frame band that is strongly coloured and not blue-led: the boss row's frames and the icons'
    /// edges are; the scene's blue swirls behind the row are not.</summary>
    internal static double WarmFrame(Image area, PixelRect icon)
    {
        int band = Math.Max(1, (int)Math.Round(icon.Width * 2 / 31.0)), hit = 0, all = 0;
        for (int y = icon.Y; y < icon.Bottom; y++)
            for (int x = icon.X; x < icon.Right; x++)
            {
                if (x - icon.X >= band && icon.Right - 1 - x >= band && y - icon.Y >= band && icon.Bottom - 1 - y >= band) continue;
                var (b, g, rr) = area.Pixel(x, y);
                int hi = Math.Max(rr, Math.Max(g, b)), lo = Math.Min(rr, Math.Min(g, b));
                all++;
                if (hi >= 90 && hi - lo >= 50 && b < hi) hit++;
            }
        return all == 0 ? 0 : hit / (double)all;
    }

    /// <summary>The shares of an icon's frame band that are red (a debuff on the boss) and green (the boss's own buff).</summary>
    internal static (double Red, double Green) FrameColours(Image area, PixelRect icon)
    {
        int band = Math.Max(1, (int)Math.Round(icon.Width * 2 / 31.0)), red = 0, green = 0, all = 0;
        for (int y = icon.Y; y < icon.Bottom; y++)
            for (int x = icon.X; x < icon.Right; x++)
            {
                if (x - icon.X >= band && icon.Right - 1 - x >= band && y - icon.Y >= band && icon.Bottom - 1 - y >= band) continue;
                if (x < 0 || y < 0 || x >= area.Width || y >= area.Height) continue;
                var (b, g, rr) = area.Pixel(x, y);
                all++;
                if (rr >= FrameMin && rr - Math.Max(g, b) >= FrameLead) red++;
                else if (g >= FrameMin && g - Math.Max(rr, b) >= FrameLead) green++;
            }
        return all == 0 ? (0, 0) : (red / (double)all, green / (double)all);
    }

    // A frame pixel's own channel at least this, and this far over the other two: red (169,51,42), (124,6,0); green (114,174,88)
    // (bitam_80). Blue scenery fails both.
    const int FrameMin = 90, FrameLead = 40;

    // The six debuffrow shots: a filled box 0.22–0.78 (the frame's top-left is silver), an empty one 0.03 at most.
    const double MinFrame = 0.12;

    /// <summary>The share of the icon's outer ring (2 px, scaled) that is frame red: R well over G and B.</summary>
    internal static double RedFrame(Image area, PixelRect icon)
    {
        int band = Math.Max(1, (int)Math.Round(icon.Width * 2 / 31.0)), red = 0, all = 0;
        for (int y = icon.Y; y < icon.Bottom; y++)
            for (int x = icon.X; x < icon.Right; x++)
            {
                if (x - icon.X >= band && icon.Right - 1 - x >= band && y - icon.Y >= band && icon.Bottom - 1 - y >= band) continue;
                if (x < 0 || y < 0 || x >= area.Width || y >= area.Height) continue;
                var (b, g, r) = area.Pixel(x, y);
                all++;
                if (r >= 90 && r - Math.Max(g, b) >= 50) red++;
            }
        return all == 0 ? 0 : red / (double)all;
    }

    /// <summary>What the debug window's row says of a box: the kind or "없음", with the 결투자 score when nothing or the red
    /// sigil was read and the score is over half (a near miss of the 결투자's bar, to see it in game).</summary>
    public static string DebugText(DebuffKind? kind, double duelScore)
    {
        string text = kind?.ToString() ?? "없음";
        return kind is null or DebuffKind.빨강 && duelScore > NearMiss ? $"{text} (결투자 {duelScore.ToString("0.00", CultureInfo.InvariantCulture)})" : text;
    }

    const double NearMiss = 0.5;

    // ── Boss counts ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reference pixels inside Geometry.RefBossRow: the icons' frames (32 px) start 29 px down; where along the row
    // depends on the boss's other effects, so the row is searched in steps the match's own ±2 px search covers. The
    // "X n" count's digit sits 17–18 px right of its icon's left edge and 14 px above it.
    const int BossIconY = 29, BossIcon = 32, BossStep = 4;
    /// <summary>Where the sigils' frames start inside <see cref="Geometry.RefBossRow"/>, reference pixels from its top.</summary>
    public const int BossIconTop = BossIconY;
    /// <summary>The boss's effect icon's side in reference pixels (<see cref="Geometry.BossScale"/> × this on screen).</summary>
    public const int BossIconSize = BossIcon;
    /// <summary>Reference px from one boss row icon's left edge to the next (bossrow_*, bitam_*).</summary>
    public const int BossSlotPitch = 34;
    const int CountDx = 17, CountDy = -14, CountSearch = 3;
    const int GlyphW = 8, GlyphH = 11;
    // A digit's own glyph scores 0.9+ (DebuffReaderTests); a look-alike (8 against 6) less. 7, the one digit without a
    // sample yet, must not pass as a known one, so the bar is high.
    internal const double MinCount = 0.9;

    static readonly (int Digit, float[] Luma)[] Digits = [.. DebuffTemplates.Digits.Select(d =>
    {
        var img = Image.FromBytes(Convert.FromBase64String(d.Glyph))!;
        return (d.Digit - '0', ImageOps.LumaFast(img, new PixelRect(0, 0, img.Width, img.Height)));
    })];

    /// <summary>The boss's red and blue counts in <paramref name="row"/> (<see cref="Geometry.BossRow"/>, captured at
    /// <paramref name="scale"/> screen pixels per reference pixel); null for a sigil not found or a count not read.</summary>
    public static (int? Red, int? Blue) BossCounts(Image row, double scale)
    {
        var r = ToReference(row, scale);
        var zone = BuffZone(r, BossIconY).TakeLast(SigilSlots).ToList();
        return (Count(r, DebuffKind.빨강, zone), Count(r, DebuffKind.파랑, zone));
    }

    /// <summary>The boss's sigils sit in the last two slots of the boss buff zone (user, reference/빛암/mordum fix.PNG): never
    /// in the debuff zone after it, where 암수 is read.</summary>
    const int SigilSlots = 2;

    /// <summary>The boss buff zone on line <paramref name="y"/> of <paramref name="r"/> (reference px): the x of each green-framed
    /// icon, left to right. Green frame share: the sigils 0.30–0.45 (bossrow*), 0.10–0.31 shrunk to HUD 80 and back; any other
    /// window 0.00 (the debuffs' frames are red).</summary>
    internal static IReadOnlyList<int> BuffZone(Image r, int y)
    {
        var green = new List<(int X, double Share)>();
        for (int x = 0; x + BossIcon <= r.Width; x++)
            if (FrameColours(r, new PixelRect(x, y, BossIcon, BossIcon)).Green is var g && g >= MinBuffFrame) green.Add((x, g));
        // One slot per icon width: the slots are 34 px apart, while a window 30 px past an icon sees its right frame line in its
        // own left band (0.13) and would pass as a slot of its own, pushing the red sigil out of the last two (review, 2026-11-01).
        return [.. green.Where(a => !green.Any(b => Math.Abs(b.X - a.X) <= BossIcon && (b.Share > a.Share || b.Share == a.Share && b.X < a.X)))
            .Select(a => a.X)];
    }

    const double MinBuffFrame = 0.05;
    const int SlotSlack = 4;

    static int? Count(Image row, DebuffKind sigil, IReadOnlyList<int> zone)
    {
        int k = (int)sigil;
        double best = BuffReader.MinStructure;
        PixelRect? found = null;
        // Around each slot, as the whole-row slide did: a blurred frame puts the slot a few px off, and the count is read from the
        // sigil's own spot (bossrow_62 at HUD 80 read 1 for 2 from the slot itself).
        foreach (int x in zone.SelectMany(z => Enumerable.Range(-SlotSlack, 2 * SlotSlack + 1).Select(d => z + d)).Where(x => x >= 0 && x + BossIcon <= row.Width))
        {
            var icon = new PixelRect(x, BossIconY, BossIcon, BossIcon);
            var (s, h, _) = BuffReader.Match(row, icon, [Thumbs[k]], [Hues[k]], []);
            if (h[0] <= BuffReader.MaxHueDistance && s[0] > best) (best, found) = (s[0], icon);
        }
        if (found is not { } at) return null;
        // One stack shows no "X n" (as a buff's doesn't); an "X" whose digit can't be read is unknown.
        return ReadCount(row, at).Digit ?? (XScore(row, at) < MinX ? 1 : null);
    }

    // The "X" sits 13 px left of the digit. An "X" scores MinX+ where a count shows (18-22.mp4), bare background less.
    const int XDx = CountDx - 13, XW = 10;
    internal const double MinX = 0.6;
    static readonly float[] XLuma = XGlyph();

    static float[] XGlyph()
    {
        var img = Image.FromBytes(Convert.FromBase64String(DebuffTemplates.CountX))!;
        return ImageOps.LumaFast(img, new PixelRect(0, 0, img.Width, img.Height));
    }

    /// <summary>How much the spot of a count's "X" over <paramref name="icon"/> looks like one, best over the search.</summary>
    internal static double XScore(Image row, PixelRect icon)
    {
        double best = -2;
        for (int y = icon.Y + CountDy - CountSearch; y <= icon.Y + CountDy + CountSearch; y++)
            for (int x = icon.X + XDx - CountSearch; x <= icon.X + XDx + CountSearch; x++)
                if (x >= 0 && y >= 0 && x + XW <= row.Width && y + GlyphH <= row.Height)
                    best = Math.Max(best, ImageOps.Correlate(ImageOps.LumaFast(row, new PixelRect(x, y, XW, GlyphH)), XLuma));
        return best;
    }

    /// <summary>The count over <paramref name="icon"/>: the best digit glyph and its score, the digit null below
    /// <see cref="MinCount"/>.</summary>
    internal static (int? Digit, double Score) ReadCount(Image row, PixelRect icon)
    {
        (int Digit, double Score) best = (-1, -2);
        for (int y = icon.Y + CountDy - CountSearch; y <= icon.Y + CountDy + CountSearch; y++)
            for (int x = icon.X + CountDx - CountSearch; x <= icon.X + CountDx + CountSearch; x++)
            {
                if (x < 0 || y < 0 || x + GlyphW > row.Width || y + GlyphH > row.Height) continue;
                var luma = ImageOps.LumaFast(row, new PixelRect(x, y, GlyphW, GlyphH));
                foreach (var (d, g) in Digits)
                    if (ImageOps.Correlate(luma, g) is var s && s > best.Score) best = (d, s);
            }
        return (best.Score >= MinCount ? best.Digit : null, best.Score);
    }
}
