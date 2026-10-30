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
