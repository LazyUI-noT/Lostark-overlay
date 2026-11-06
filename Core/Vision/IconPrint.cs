using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>A skill icon's fingerprint (skill-icon library, HANDOFF §2.1): the icon averaged into a 16×16 grid of luminance and
/// two colour differences, each channel normalised (so dimming, greying-out brightness and scale don't matter), without the
/// cells the game draws over a skill-bar icon and a reference picture lacks: the frame ring, the key-letter strip
/// (<see cref="SlotCalibration"/>'s band) and the badges in the top corners. 768 bytes. Pure.</summary>
public sealed class IconPrint
{
    public const int Grid = 16;
    const int Ring = 2;   // 초각스' and 각성기's frames are ~8 px of a 54 px slot
    const int Channels = 3, Cells = Grid * Grid;
    const double Scale = 32;   // a z-score of ±4 fills a signed byte

    // Measured 2026-11-05: saved slot icons of 워로드, 발키리, 도화가 (both 직업각인 each) and 바드 (from compressed video) against
    // the CDN icons of the same class, with Framings and References. Every top match right; clean captures 0.745–0.993,
    // 바드's blurred ones 0.52–0.92, the highest wrong 0.669 (0.319 under its right one). Across all 30 classes at once a
    // wrong one reached 0.742, so these hold only for one class's candidates (the gate Best's caller applies).
    public const double MinScore = 0.6, MinGap = 0.1;

    static readonly bool[] Masked = BuildMask();
    readonly sbyte[] _v;
    readonly long _norm;   // Σv², once: Similarity then needs only the dot product

    IconPrint(sbyte[] v)
    {
        _v = v;
        foreach (int a in v) _norm += a * a;
    }

    /// <summary>What the game draws over a skill-bar icon: the frame ring, the key letter's strip (the bottom third, a fifth of
    /// the width in from either side), the tripod / rune badge top left and a stack count top right.</summary>
    static bool[] BuildMask()
    {
        var m = new bool[Cells];
        for (int y = 0; y < Grid; y++)
            for (int x = 0; x < Grid; x++)
            {
                double cx = (x + 0.5) / Grid, cy = (y + 0.5) / Grid;
                bool ring = x < Ring || y < Ring || x >= Grid - Ring || y >= Grid - Ring;
                bool letter = cy > 0.68 && cx > 0.2 && cx < 0.8;
                bool badge = cy < 0.35 && (cx < 0.35 || cx > 0.7);   // tripod / rune mark top left, a stack count top right
                m[y * Grid + x] = ring || letter || badge;
            }
        return m;
    }

    /// <summary>The print of a whole icon picture (any size; the frame and key letter, if any, are left out by the mask).</summary>
    public static IconPrint Of(Image icon)
    {
        var sum = new double[Cells * Channels];
        var count = new int[Cells];
        for (int y = 0; y < icon.Height; y++)
            for (int x = 0; x < icon.Width; x++)
            {
                int cell = y * Grid / icon.Height * Grid + x * Grid / icon.Width, i = y * icon.Stride + x * 4;
                double b = icon.Bgra[i], g = icon.Bgra[i + 1], r = icon.Bgra[i + 2], luma = 0.114 * b + 0.587 * g + 0.299 * r;
                sum[cell * Channels] += luma; sum[cell * Channels + 1] += r - luma; sum[cell * Channels + 2] += b - luma;
                count[cell]++;
            }
        var v = new sbyte[Cells * Channels];
        for (int c = 0; c < Channels; c++)
        {
            double mean = 0, sq = 0; int n = 0;
            for (int k = 0; k < Cells; k++)
                if (!Masked[k] && count[k] > 0) { double a = sum[k * Channels + c] / count[k]; mean += a; sq += a * a; n++; }
            if (n == 0) continue;
            mean /= n;
            double sd = Math.Sqrt(Math.Max(0, sq / n - mean * mean));
            if (sd < 1) continue;   // a flat channel carries nothing
            for (int k = 0; k < Cells; k++)
                if (!Masked[k] && count[k] > 0)
                    v[k * Channels + c] = (sbyte)Math.Clamp(Math.Round((sum[k * Channels + c] / count[k] - mean) / sd * Scale), -127, 127);
        }
        return new(v);
    }

    /// <summary>Prints of a skill-bar capture at a few framings: the slot crop's margin and frame differ by HUD size and slot from
    /// the reference art, which fills its picture. Insets 0..<paramref name="maxInset"/> px from each side, shifted ±1 px.</summary>
    public static IReadOnlyList<IconPrint> Framings(Image capture, int maxInset = 4)
    {
        var prints = new List<IconPrint>();
        for (int inset = 0; inset <= maxInset; inset++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (inset == 0 && (dx != 0 || dy != 0)) continue;
                    var r = new PixelRect(inset + dx, inset + dy, capture.Width - 2 * inset, capture.Height - 2 * inset);
                    if (r.X < 0 || r.Y < 0 || r.X + r.Width > capture.Width || r.Y + r.Height > capture.Height || Math.Min(r.Width, r.Height) < Grid) continue;
                    prints.Add(Of(capture.Crop(r)));
                }
        return prints;
    }

    /// <summary>A reference icon's prints: whole, and with its own frame trimmed (각성기' gold frame and notch, 초각스' silver
    /// one: a skill-bar capture may start inside it). Both go in the library under the icon's id.</summary>
    public static IReadOnlyList<IconPrint> References(Image reference)
    {
        int t = Math.Min(reference.Width, reference.Height) / 10;
        return [Of(reference), Of(reference.Crop(new PixelRect(t, t, reference.Width - 2 * t, reference.Height - 2 * t)))];
    }

    /// <summary>Correlation 0..1 of two prints over the cells they keep; 0 when either is flat.</summary>
    public double Similarity(IconPrint other)
    {
        if (_norm == 0 || other._norm == 0) return 0;
        long ab = 0;
        for (int i = 0; i < _v.Length; i++) ab += _v[i] * other._v[i];
        return Math.Max(0, ab / Math.Sqrt((double)_norm * other._norm));
    }

    /// <summary><see cref="Similarity"/> by brightness alone: a greyed-out bar has lost its colour differences, while a reference
    /// keeps them. 0 when either is flat.</summary>
    public double BrightnessSimilarity(IconPrint other)
    {
        long ab = 0, aa = 0, bb = 0;
        for (int i = 0; i < _v.Length; i += Channels) (ab, aa, bb) = (ab + _v[i] * other._v[i], aa + _v[i] * _v[i], bb + other._v[i] * other._v[i]);
        return aa == 0 || bb == 0 ? 0 : Math.Max(0, ab / Math.Sqrt((double)aa * bb));
    }

    /// <summary>The id whose print matches a capture's <see cref="Framings"/> best, when it scores <see cref="MinScore"/> and beats every other id by
    /// <see cref="MinGap"/>; null otherwise (unknown, never a guess). The caller passes only the candidates its gates allow
    /// (the fight's class, its 직업각인's skills).</summary>
    public static string? Best(IReadOnlyList<IconPrint> framings, IEnumerable<(string Id, IconPrint Print)> library)
    {
        if (framings.Count == 0) return null;
        string? best = null; double top = 0, second = 0;
        foreach (var (id, p) in library)
        {
            double s = framings.Max(f => f.Similarity(p));
            if (s > top) { if (best != id) second = top; (best, top) = (id, s); }
            else if (s > second && id != best) second = s;
        }
        return best != null && top >= MinScore && top - second >= MinGap ? best : null;
    }

    public byte[] ToBytes() => [.. _v.Select(s => (byte)s)];

    public static IconPrint FromBytes(byte[] bytes) =>
        bytes.Length == Cells * Channels ? new([.. bytes.Select(b => (sbyte)b)]) : throw new ArgumentException($"an icon print is {Cells * Channels} bytes, not {bytes.Length}");
}
