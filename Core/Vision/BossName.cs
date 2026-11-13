using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Recognises the gate from the boss's red name above the HP bar (Geometry.BossName, spec § B 자동 인식). A gate's
/// template is its name's red ink at HUD 110 1080p (Profiles/bossnames/&lt;gate id&gt;.bgra, Image.ToBytes). The strip's red ink
/// must be as wide as the template's at this scale, and its redness (R − max(G, B)) on a 32×6 grid must correlate with it.</summary>
public static class BossName
{
    const int GridW = 32, GridH = 6;
    // On the bosstop shots the same name scores 0.74 (21:9, 0.75 scale) to 0.85 (another 16:9 frame); 루메루스 scores ~0.
    const double MinScore = 0.6;
    const double MaxWidthOff = 0.15;   // ink width / scale against the template's: 167–170 px for 모르둠 on the three shots

    /// <summary>The gates with a name file, in order; a missing or unreadable file leaves its gate out.</summary>
    public static IReadOnlyList<(string GateId, Image Template)> LoadTemplates(string profilesDir, IEnumerable<RaidGate> gates)
    {
        var list = new List<(string, Image)>();
        foreach (var g in gates)
        {
            var file = Path.Combine(profilesDir, "bossnames", g.Id + ".bgra");
            try
            {
                if (File.Exists(file) && Image.FromBytes(File.ReadAllBytes(file)) is { } img) list.Add((g.Id, img));
            }
            catch (IOException) { }   // a locked or vanished file: that gate isn't recognised, the rest are
        }
        return list;
    }

    /// <summary>The gate whose name is in <paramref name="strip"/>, or null.</summary>
    public static string? Match(Image strip, double scale, IReadOnlyList<(string GateId, Image Template)> templates)
    {
        if (templates.Count == 0 || Ink(strip) is not { } ink) return null;
        var grid = Grid(strip, ink);
        string? best = null;
        double bestScore = MinScore;
        foreach (var (id, t) in templates)
        {
            if (Math.Abs(ink.Width / scale / t.Width - 1) > MaxWidthOff) continue;
            double s = BossDigits.Ncc(grid, Grid(t, new(0, 0, t.Width, t.Height)));
            if (s >= bestScore) (best, bestScore) = (id, s);
        }
        return best;
    }

    // A name's widest gap is a word space (~13 px on the bosstop shots); red more than this past the name is something else.
    const double MaxGap = 24;
    // Red pixels a name has at least, at HUD 110 1080p: 2–3 letters (the dummy's 2 letters at HUD 80 hold ~330).
    const double MinInk = 60;

    /// <summary>What the name's OCR reads: the strip up to the red name's end (a few px past it), or null with no red name. The
    /// whole strip is never read: with 방송·캡처에서 숨김 off it holds the app's own labels ("[7,568억]", 대난투 2026-11-12).
    /// A new image, since the capture buffer is reused.</summary>
    public static Image? OcrStrip(Image strip, double scale) =>
        TextEnd(strip, scale) is { } end
            ? strip.Crop(new(0, 0, Math.Min(strip.Width, end + (int)Math.Ceiling(4 * scale)), strip.Height))
            : null;

    /// <summary>Where the name's red text ends in <paramref name="strip"/> (exclusive, strip pixels), or null when there is no name:
    /// the HP label is placed after it and the name's OCR stops at it (boss-label-name.plan.md). The text is the run of red columns
    /// around the reddest one, gaps up to a word space; a flash's specks are too few to be a name and too far to lengthen one.</summary>
    public static int? TextEnd(Image strip, double scale)
    {
        var cols = new int[strip.Width];
        for (int y = 0; y < strip.Height; y++)
            for (int x = 0; x < strip.Width; x++)
                if (Red(strip.Pixel(x, y))) cols[x]++;
        var runs = Runs(cols, (int)Math.Ceiling(MaxGap * scale));
        if (runs.Count == 0) return null;
        int best = 0;
        for (int i = 1; i < runs.Count; i++) if (runs[i].Ink > runs[best].Ink) best = i;
        return runs[best].Ink >= MinInk * scale * scale ? runs[best].End : null;
    }

    /// <summary>The strip's red columns grouped into runs whose gaps are at most <paramref name="gap"/> columns.</summary>
    static List<(int End, int Ink)> Runs(int[] cols, int gap)
    {
        var runs = new List<(int End, int Ink)>();
        int last = -1, ink = 0;
        for (int x = 0; x < cols.Length; x++)
        {
            if (cols[x] == 0) continue;
            if (last >= 0 && x - last - 1 > gap) { runs.Add((last + 1, ink)); ink = 0; }
            (last, ink) = (x, ink + cols[x]);
        }
        if (last >= 0) runs.Add((last + 1, ink));
        return runs;
    }

    static bool Red((byte B, byte G, byte R) p) => p.R > 170 && p.G < 110 && p.B < 110;

    /// <summary>The bounds of the strip's red text, or null when there is none.</summary>
    internal static PixelRect? Ink(Image strip)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < strip.Height; y++)
            for (int x = 0; x < strip.Width; x++)
                if (Red(strip.Pixel(x, y))) (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        return x1 < 0 ? null : new(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    static float[] Grid(Image img, PixelRect r) => BossDigits.Resample(i =>
    {
        int b = i * 4;
        return Math.Max(0, img.Bgra[b + 2] - Math.Max(img.Bgra[b], img.Bgra[b + 1]));
    }, img.Width, r.X, r.Right, r.Y, r.Bottom, GridW, GridH);
}
