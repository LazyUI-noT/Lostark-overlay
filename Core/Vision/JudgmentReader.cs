using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>심판's icon in a buff box (단심, the player's own rune buff). One template, matched as BuffReader matches the
/// party buffs (structure and hue), at a bar of its own: one false reading starts a 30 s cycle.</summary>
public static class JudgmentReader
{
    static readonly Image Template = Image.FromBytes(Convert.FromBase64String(JudgmentTemplates.Icon))!;
    static readonly float[][] Thumbs = [BuffReader.Thumb(Template)];
    static readonly double[] Hues = [SlotClassifier.ColourStats(Template).MeanHue];
    static readonly (int Of, float[] Thumb)[] LookAlikes =
        [.. JudgmentTemplates.LookAlikes.Select(s => (0, BuffReader.Thumb(Image.FromBytes(Convert.FromBase64String(s))!)))];
    internal const double MinIcon = 0.85;

    /// <summary>The icon as the game draws it, frame left out (25×25 at 1080p, buff 120%): the 준비 아이콘.</summary>
    public static Image Icon => Template;

    /// <summary>Structure score against 심판, hue distance (degrees) and the best look-alike's score.</summary>
    internal static (double Structure, double Hue, double LookAlike) Scores(Image area, PixelRect icon)
    {
        var (structure, hue, lookAlike) = BuffReader.Match(area, icon, Thumbs, Hues, LookAlikes);
        return (structure[0], hue[0], lookAlike[0]);
    }

    /// <summary>The box at <paramref name="icon"/> (relative to <paramref name="area"/>) holds 심판's icon.</summary>
    public static bool IsIcon(Image area, PixelRect icon)
    {
        var (structure, hue, lookAlike) = Scores(area, icon);
        return structure >= MinIcon && hue <= BuffReader.MaxHueDistance && lookAlike < structure;
    }
}
