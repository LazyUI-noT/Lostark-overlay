using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>A slot's ready look, stored at the fit (PLAN §3 "Ready reference"), and the numbers derived from it
/// once: hue spread (so a tinted icon is never kept as a reference), grey gradients of the key label (for Unreadable) and the
/// share of white pixels in the text area (for flashes).</summary>
public sealed class SlotCalibration
{
    // The strip holding the key letter: the bottom third, inside a fifth of the width from either side. Measured on
    // live_test2 against icons saved on another character: 0.74–0.98 here, 0.33–0.62 over the whole icon.
    private const double LabelTop = 0.68, LabelMargin = 0.2;
    private readonly int[] _edges;   // grey gradients (dx, dy) of the ready crop

    /// <summary>The saved file of 수동 조정's fit ends in this, so 자동 and 직접 입력 keep theirs.</summary>
    public const string AdjustSuffix = "_adj";

    /// <summary>The file a slot's ready look is saved in. The key-label strip matches on any character for the eight keys,
    /// 각성기 and 초각스, but a 아덴 slot's frame is the class's own (워로드's shield against 도화가's hexagon: 0.01–0.04, all
    /// Unreadable): its file is per class (<paramref name="character"/> "클래스 · 직업각인"; both 직업각인 share it), and per
    /// place for a class that moves it (<paramref name="adenRect"/>, 발키리).</summary>
    public static string FileName(SlotId slot, PixelRect? adenRect, string? character, bool adjusted)
    {
        bool aden = slot is SlotId.아덴1 or SlotId.아덴2;
        var name = aden && adenRect is { Width: > 0 } r ? $"{slot}_{r.X}_{r.Y}" : slot.ToString();
        if (aden && character?.Split(" · ")[0] is { Length: > 0 } cls) name += $"_{cls}";
        return name + (adjusted ? AdjustSuffix : "") + ".bgra";
    }

    public SlotCalibration(Image ready)
    {
        Ready = ready;
        (_, HueSpread, _) = SlotClassifier.ColourStats(ready);
        WhiteShare = SlotClassifier.WhiteShare(ready);
        _edges = new int[ready.Width * ready.Height * 2];
        var p = ready.Bgra;
        for (int y = 0; y < ready.Height - 1; y++)
            for (int x = 0, i = y * ready.Stride; x < ready.Width - 1; x++, i += 4)
            {
                int c = p[i] + p[i + 1] + p[i + 2], e = (y * ready.Width + x) * 2;
                _edges[e] = p[i + 4] + p[i + 5] + p[i + 6] - c;
                _edges[e + 1] = p[i + ready.Stride] + p[i + ready.Stride + 1] + p[i + ready.Stride + 2] - c;
            }
    }

    /// <summary>The ready crop the numbers were taken from.</summary>
    public Image Ready { get; }
    /// <summary>Circular standard deviation of hue over coloured pixels, in degrees.</summary>
    public double HueSpread { get; }
    /// <summary>Share of near-white pixels in the text area.</summary>
    public double WhiteShare { get; }

    /// <summary>Structural similarity 0..1 of the key-label strip of <paramref name="crop"/> to the ready crop's:
    /// correlation of their grey gradients. The key letter and the dark notch under it are drawn by the game over
    /// every icon and depend only on the key binding, so they match on any character, while the icon art above them
    /// doesn't (the saved icon outlives the character it was taken on). Dimming, greying, tints and glows keep the
    /// letter; the game world behind a hidden HUD, or the taskbar over the bar, doesn't correlate with it.</summary>
    public double Similarity(Image crop)
    {
        int w = Math.Min(crop.Width, Ready.Width), h = Math.Min(crop.Height, Ready.Height), st = crop.Stride;
        int x0 = (int)(w * LabelMargin), x1 = Math.Min(w - x0, w - 1), y0 = (int)(h * LabelTop);
        long sab = 0, saa = 0, sbb = 0;
        var p = crop.Bgra;
        for (int y = y0; y < h - 1; y++)   // every pixel: the letter's strokes are a pixel or two wide
            for (int x = x0, i = y * st + x0 * 4; x < x1; x++, i += 4)
            {
                int c = p[i] + p[i + 1] + p[i + 2], e = (y * Ready.Width + x) * 2;
                int dx = p[i + 4] + p[i + 5] + p[i + 6] - c, dy = p[i + st] + p[i + st + 1] + p[i + st + 2] - c;
                int rx = _edges[e], ry = _edges[e + 1];
                sab += dx * rx + dy * ry; saa += dx * dx + dy * dy; sbb += rx * rx + ry * ry;
            }
        return saa == 0 || sbb == 0 ? 0 : Math.Max(0, sab / Math.Sqrt((double)saa * sbb));
    }
}

/// <summary>Turns one slot crop into a <see cref="Reading"/>: the text decides cooldown; without text the slot is
/// Ready, Unreadable (not the slot any more, e.g. HUD hidden) or CooldownIllegible (text area washed out by a
/// flash, so "no digits" can't be trusted), plus whether the game's gold frame is on. Whether a skill is usable
/// (마나 부족, 침묵) isn't read: cooldowns only.</summary>
public static class SlotClassifier
{
    private const double MinSimilarity = 0.4;       // below this the crop isn't the slot (scenery ≤ 0.30, a slot ≥ 0.64)
    private const double WashedOut = 0.2;           // extra white share over the reference that hides text
    private const double WashedOutNoRef = 0.35;
    private static readonly float[] Cos = Enumerable.Range(0, 360).Select(d => MathF.Cos(d * MathF.PI / 180)).ToArray();
    private static readonly float[] Sin = Enumerable.Range(0, 360).Select(d => MathF.Sin(d * MathF.PI / 180)).ToArray();

    // A case-2 아덴 skill without enough resource is drawn dark. The middle of 발키리's Z and C (buff_gameplay.mp4,
    // buff_wrong_detect.mp4) averages luminance 150–160 lit and 57–60 dark, with nothing between but a fade frame.
    private const double DarkLuma = 100;
    private const double MidLeft = 0.2, MidTop = 0.12, MidBottom = 0.68;   // above the key label
    // 워로드 전투태세's C (DarkCue.Label; war_C, war_bug_C): its grey shield's middle is 85–97 lit and 44–60 dark, and the
    // key label's share of white (luma ≥ 200) pixels 0.022–0.18 lit and 0 dark, but for the mouse cursor over it (0.07–0.11).
    // Both must say lit: the label, with the middle ruling out the cursor.
    private const double LabelDarkLuma = 70, LabelWhite = 0.012, LabelTop = 0.68;

    /// <summary>Reads <paramref name="crop"/> (the slot rect from <see cref="Geometry.Slot"/>). Without a
    /// calibration there is nothing to match the slot against, so Unreadable is never reported. For a case-2 아덴
    /// skill (<paramref name="dark"/>), a dark one without digits is Unavailable, by the class's own cue.</summary>
    public static Reading Read(SlotId slot, Image crop, TimeSpan at, SlotCalibration? cal, DigitReader digits, DarkCue dark = DarkCue.None)
    {
        var (kind, value) = digits.Read(crop, slot);
        bool stacked = kind == ReadKind.Seconds && digits.ShowsCount(crop);
        if (kind is not (ReadKind.Seconds or ReadKind.Minutes))
        {
            if (cal != null && cal.Similarity(crop) < MinSimilarity) return new(slot, ReadKind.Unreadable, 0, at);
            bool washedOut = WhiteShare(crop) - (cal?.WhiteShare ?? 0) > (cal == null ? WashedOutNoRef : WashedOut);
            (kind, value) = (kind == ReadKind.CooldownIllegible || washedOut ? ReadKind.CooldownIllegible : ReadKind.Ready, 0);
            if (kind == ReadKind.Ready && IsDark(crop, dark)) kind = ReadKind.Unavailable;
        }
        // Only the eight keys and 초각스 get the gold frame (각성기's is gold all the time, 아덴 skills never light).
        return new(slot, kind, value, at, stacked, slot is not (SlotId.각성기 or SlotId.아덴1 or SlotId.아덴2) && GoldFrame(crop),
            RingWhite(crop) >= FlashRing);
    }

    // A use lights the icon's edge white for about 7 frames (chainSkill.mp4): 0.65–0.99 of the outer 3 px ring is near-white
    // then, against 0.25 at most on any ready or cooling icon of the fixtures. A bright scene behind a hidden HUD whitens it
    // too, but the slot is unreadable then (the tracker takes a flash only on a ready slot it has just read).
    private const int RingDepth = 3, RingMin = 150;
    private const double FlashRing = 0.6;

    /// <summary>Share of near-white pixels (min of R, G, B ≥ 150) in the outer 3 px ring of <paramref name="crop"/>.</summary>
    public static double RingWhite(Image crop)
    {
        int n = 0, white = 0;
        for (int y = 0; y < crop.Height; y++)
        {
            bool edgeRow = y < RingDepth || y >= crop.Height - RingDepth;
            for (int x = 0; x < crop.Width; x++)
            {
                if (!edgeRow && x == RingDepth) x = Math.Max(x, crop.Width - RingDepth);   // skip the inside
                if (x >= crop.Width) break;
                int i = y * crop.Stride + x * 4;
                n++;
                if (Math.Min(crop.Bgra[i], Math.Min(crop.Bgra[i + 1], crop.Bgra[i + 2])) >= RingMin) white++;
            }
        }
        return n == 0 ? 0 : white / (double)n;
    }

    // Gold frame, measured on 333.mp4, live test.mp4 and gameplay.mp4 (PLAN §3 "Gold frame"): the lit frame is a 1–2 px
    // line of about (195, 160, 80) on the icon's edge, where an unlit icon has a dark rim of about (30, 25, 25). Lit sides
    // score 0.7–1.0 and unlit ones 0–0.1, with nothing between. The rect can sit a pixel off the icon (the lower row), so
    // the line is looked for 4 px deep and one side may be mostly cut off.
    private const int FrameDepth = 4;
    private const double LitSides = 0.6, LitSide = 0.25;

    /// <summary>True when the game draws its gold frame around the icon in <paramref name="crop"/>: on average 60% of each
    /// side's length (corners left out) has a gold pixel within 4 px of the edge, and no side has under 25%.</summary>
    public static bool GoldFrame(Image crop)
    {
        double sum = 0, min = 1;
        for (int side = 0; side < 4; side++)
        {
            double share = GoldShare(crop, side);
            (sum, min) = (sum + share, Math.Min(min, share));
        }
        return min >= LitSide && sum / 4 >= LitSides;
    }

    /// <summary>Share of positions along one side (0 top, 1 bottom, 2 left, 3 right) with a gold pixel near the edge.</summary>
    private static double GoldShare(Image img, int side)
    {
        int len = side < 2 ? img.Width : img.Height, margin = len / 8, lit = 0;
        for (int a = margin; a < len - margin; a++)
            for (int d = 0; d < FrameDepth; d++)
            {
                var (x, y) = side switch { 0 => (a, d), 1 => (a, img.Height - 1 - d), 2 => (d, a), _ => (img.Width - 1 - d, a) };
                int i = y * img.Stride + x * 4, b = img.Bgra[i], g = img.Bgra[i + 1], r = img.Bgra[i + 2];
                if (r >= 100 && g * 10 >= r * 7 && g <= r + 10 && b * 100 <= g * 65) { lit++; break; }
            }
        return lit / (double)(len - 2 * margin);
    }

    static bool IsDark(Image crop, DarkCue cue) => cue switch
    {
        DarkCue.Icon => MiddleLuma(crop) < DarkLuma,
        DarkCue.Label => MiddleLuma(crop) < LabelDarkLuma || LabelWhiteShare(crop) < LabelWhite,
        _ => false,
    };

    /// <summary>Mean luminance (BT.601) of the icon's middle: a fifth in from either side, between the top rim and the key label.</summary>
    static double MiddleLuma(Image img)
    {
        int x0 = (int)(img.Width * MidLeft), x1 = img.Width - x0, y0 = (int)(img.Height * MidTop), y1 = (int)(img.Height * MidBottom);
        double sum = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0, i = y * img.Stride + x0 * 4; x < x1; x++, i += 4)
                sum += Luma(img.Bgra, i);
        return sum / Math.Max(1, (x1 - x0) * (y1 - y0));
    }

    /// <summary>Share of white (luma ≥ 200) pixels in the key label's strip: the bottom third, a fifth in from either side.</summary>
    static double LabelWhiteShare(Image img)
    {
        int x0 = (int)(img.Width * MidLeft), x1 = img.Width - x0, y0 = (int)(img.Height * LabelTop), white = 0;
        for (int y = y0; y < img.Height; y++)
            for (int x = x0, i = y * img.Stride + x0 * 4; x < x1; x++, i += 4)
                if (Luma(img.Bgra, i) >= 200) white++;
        return white / (double)Math.Max(1, (x1 - x0) * (img.Height - y0));
    }

    static double Luma(byte[] bgra, int i) => (29 * bgra[i] + 150 * bgra[i + 1] + 77 * bgra[i + 2]) / 256.0;

    /// <summary>Mean chroma, and hue spread (circular std) and circular mean hue in degrees over pixels with chroma ≥ 24.</summary>
    internal static (double Chroma, double HueSpread, double MeanHue) ColourStats(Image img)
    {
        long chroma = 0;
        double sx = 0, sy = 0;
        int coloured = 0;
        var px = img.Bgra;
        for (int y = 0; y < img.Height; y += 2)   // every other pixel: these are averages
            for (int i = y * img.Stride, e = i + img.Width * 4; i < e; i += 8)
            {
                int b = px[i], g = px[i + 1], r = px[i + 2];
                int max = Math.Max(r, Math.Max(g, b)), c = max - Math.Min(r, Math.Min(g, b));
                chroma += c;
                if (c < 24) continue;
                int deg = (max == r ? 60 * (g - b) / c : max == g ? 120 + 60 * (b - r) / c : 240 + 60 * (r - g) / c) + 360;
                sx += Cos[deg % 360]; sy += Sin[deg % 360];
                coloured++;
            }
        double spread = 0, hue = 0;
        if (coloured > 0)
        {
            hue = (Math.Atan2(sy, sx) * 180 / Math.PI + 360) % 360;
            double len = Math.Sqrt(sx * sx + sy * sy) / coloured;
            spread = Math.Sqrt(-2 * Math.Log(Math.Max(len, 1e-9))) * 180 / Math.PI;
        }
        return (chroma / (double)(((img.Width + 1) / 2) * ((img.Height + 1) / 2)), spread, hue);
    }

    /// <summary>Share of near-white pixels in the text area (centre 60% × 45%).</summary>
    internal static double WhiteShare(Image img)
    {
        int x0 = img.Width / 5, x1 = img.Width - x0, y0 = (int)(img.Height * 0.275), y1 = img.Height - y0;
        int white = 0;
        var px = img.Bgra;
        for (int y = y0; y < y1; y++)
            for (int i = y * img.Stride + x0 * 4, e = y * img.Stride + x1 * 4; i < e; i += 4)
            {
                int b = px[i], g = px[i + 1], r = px[i + 2];
                int min = Math.Min(r, Math.Min(g, b));
                if (min >= 200 && Math.Max(r, Math.Max(g, b)) - min <= 40) white++;
            }
        return white / (double)((x1 - x0) * (y1 - y0));
    }
}
