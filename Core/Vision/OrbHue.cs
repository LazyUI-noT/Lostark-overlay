using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>브레이커's orb liquid tells the 직업각인 (breaker-aden.plan.md phase 4b, measured on the user's clips): from about 20 %
/// full the median hue of its saturated, bright pixels is 21.6–35.8° for 권왕파천무 and 4.5–12.8° for 수라의길; below that the
/// rim dominates and both read 12–38°. 권왕's teal state after Z (141–164°) is no liquid. Pure.</summary>
public static class OrbHue
{
    public const double MinFill = 0.2;    // the orb at least this full before its hue counts
    public const double Split = 17;       // ° between the two liquids
    public const double MaxLiquid = 60;   // ° above this is no liquid (the teal, a flash)
    const int MinChroma = 60, MinBright = 120;

    /// <summary>The 직업각인 under and over <see cref="Split"/> for a class whose orb tells them, else null.</summary>
    /// <remarks>ponytail: one class, so a constant; classes.json gets an "orbHue" entry when a second class reads this way.</remarks>
    public static (string Below, string Above)? For(string? className) => className == "브레이커" ? ("수라의길", "권왕파천무") : null;

    /// <summary>The median hue (°) over the gauge's pixels on <paramref name="capture"/> (its <see cref="GaugeDef.Ref"/>, any size),
    /// or null when too few are coloured or the hue is no liquid's.</summary>
    public static double? Median(Image capture, GaugeDef def)
    {
        var s = def.Shape;
        var img = capture.Width == s.Width && capture.Height == s.Height ? capture : GaugeDef.Scale(capture, s.Width, s.Height);
        var hues = new List<double>(s.Pixels.Length);
        foreach (int p in s.Pixels)
        {
            var (b, g, r) = img.Pixel(p % s.Width, p / s.Width);
            int max = Math.Max(r, Math.Max(g, b)), c = max - Math.Min(r, Math.Min(g, b));
            if (c < MinChroma || max < MinBright) continue;
            hues.Add(((max == r ? 60.0 * (g - b) / c : max == g ? 120 + 60.0 * (b - r) / c : 240 + 60.0 * (r - g) / c) + 360) % 360);
        }
        if (hues.Count < s.Pixels.Length / 10) return null;   // mostly dark: nothing to judge by
        hues.Sort();
        double median = hues[hues.Count / 2];
        return median <= MaxLiquid ? median : null;
    }
}

/// <summary>The last <see cref="Needed"/> liquid hues; a 직업각인 only when every one falls on the same side of the split, since
/// single frames under effects stray (수라 reaches ~33°). <see cref="At"/>: when the last hue was added. Immutable.</summary>
public sealed record OrbVote(IReadOnlyList<double> Hues, TimeSpan At = default)
{
    public const int Needed = 5;
    /// <summary>Longer than this since the last hue (a loading screen, the orb under <see cref="OrbHue.MinFill"/>): the vote starts over.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(3);
    public static OrbVote Empty { get; } = new([]);

    /// <summary>With <paramref name="hue"/> added at <paramref name="at"/> (oldest dropped past <see cref="Needed"/>, all of them after
    /// <see cref="MaxGap"/>); a null read is skipped.</summary>
    public OrbVote Add(double? hue, TimeSpan at = default) => hue is not { } h ? this
        : Hues.Count > 0 && at - At > MaxGap ? new([h], at)
        : new([.. Hues.Skip(Math.Max(0, Hues.Count - Needed + 1)), h], at);

    public string? Pick((string Below, string Above) names) =>
        Hues.Count < Needed ? null : Hues.All(h => h < OrbHue.Split) ? names.Below : Hues.All(h => h > OrbHue.Split) ? names.Above : null;
}
