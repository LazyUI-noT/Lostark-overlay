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
    const int CountDx = 17, CountDy = -14, CountSearch = 3;
    const int GlyphW = 8, GlyphH = 11;
    // A digit's own glyph scores 0.9+ (DebuffReaderTests); a look-alike (8 against 6) less. 7, the one digit without a
    // sample yet, must not pass as a known one, so the bar is high.
    internal const double MinCount = 0.9;

    static readonly (int Digit, float[] Luma)[] Digits = [.. DebuffTemplates.Digits.Select(d =>
    {
        var img = Image.FromBytes(Convert.FromBase64String(d.Glyph))!;
        return (d.Digit - '0', BuffReader.Luma(img, new PixelRect(0, 0, img.Width, img.Height)));
    })];

    /// <summary>The boss's red and blue counts in <paramref name="row"/> (<see cref="Geometry.BossRow"/>, captured at
    /// <paramref name="scale"/> screen pixels per reference pixel); null for a sigil not found or a count not read.</summary>
    public static (int? Red, int? Blue) BossCounts(Image row, double scale)
    {
        var r = Math.Abs(scale - 1) < 0.01 ? row : BuffReader.Resample(row, new PixelRect(0, 0, row.Width, row.Height), 1 / scale);
        return (Count(r, DebuffKind.빨강), Count(r, DebuffKind.파랑));
    }

    static int? Count(Image row, DebuffKind sigil)
    {
        int k = (int)sigil;
        double best = BuffReader.MinStructure;
        PixelRect? found = null;
        for (int x = 0; x + BossIcon <= row.Width; x += BossStep)
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
        return BuffReader.Luma(img, new PixelRect(0, 0, img.Width, img.Height));
    }

    /// <summary>How much the spot of a count's "X" over <paramref name="icon"/> looks like one, best over the search.</summary>
    internal static double XScore(Image row, PixelRect icon)
    {
        double best = -2;
        for (int y = icon.Y + CountDy - CountSearch; y <= icon.Y + CountDy + CountSearch; y++)
            for (int x = icon.X + XDx - CountSearch; x <= icon.X + XDx + CountSearch; x++)
                if (x >= 0 && y >= 0 && x + XW <= row.Width && y + GlyphH <= row.Height)
                    best = Math.Max(best, BuffReader.Correlate(BuffReader.Luma(row, new PixelRect(x, y, XW, GlyphH)), XLuma));
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
                var luma = BuffReader.Luma(row, new PixelRect(x, y, GlyphW, GlyphH));
                foreach (var (d, g) in Digits)
                    if (BuffReader.Correlate(luma, g) is var s && s > best.Score) best = (d, s);
            }
        return (best.Score >= MinCount ? best.Digit : null, best.Score);
    }
}
