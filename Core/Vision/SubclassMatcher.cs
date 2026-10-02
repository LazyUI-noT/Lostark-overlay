using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Tells a class's 직업각인 apart by the 초각스 slot: its icon differs per 직업각인 (Profiles/subclass, the art
/// inside the frame cut from the skill window). Only between one class's own icons: the class comes from the emblem.</summary>
public static class SubclassMatcher
{
    // The art is compared as a coarse colour grid over its top 70%: the key letter and its notch cover the bottom of
    // the slot. Where the art sits in the slot is searched: 0.78 of the slot's side at 1080p, 0.65 in 4k_down's blurred
    // 37 px slot.
    const int Cols = 8, Rows = 6;
    const double Top = 0.7, MinSide = 0.6, MaxSide = 0.82;
    // Measured (SubclassMatcherTests): a ready slot scores 0.99 against its own icon at 1080p and 0.65 in 4k_down, the
    // class's other icon 0.69 (발키리) or 0.61 (바드). 바드's two differ in hue (in game 250°; 아리아 235°, 비바체 201°),
    // 발키리's in structure (해방자 42°, 빛의 기사 29°).
    const double MinScore = 0.6, MinLead = 0.1, MaxHueDistance = 25;
    // The game greys the whole skill bar out where skills can't be used (a town): the icons keep their shapes but lose their hue.
    // Measured chroma: greyed out 19–23 (greyed out.png), a ready 초각스 41 and up, a ready 일반 slot 48 and up; a cooling slot is
    // as grey, but cooldowns never grey every 일반 slot at once (SubclassMatcherTests).
    const double GreyedChroma = 30;

    /// <summary>The name of the icon in <paramref name="icons"/> that the 초각스 slot crop <paramref name="slot"/>
    /// shows, or null when none clearly does (on cooldown, covered, or another class).</summary>
    public static string? Pick(Image slot, IReadOnlyDictionary<string, Image> icons, bool greyed = false) => Match(slot, icons, greyed).Name;

    /// <summary>How much colour a slot crop has (mean chroma), for <see cref="Greyed"/>.</summary>
    public static double Colour(Image slot) => SlotClassifier.ColourStats(slot).Chroma;

    /// <summary>The skill bar is greyed out (a town): every 일반 slot's last <see cref="Colour"/> in <paramref name="skills"/> is grey
    /// (NaN: not read yet, so not told). The 각성기 keeps its gold frame greyed out, and the 아덴 slots are nearly grey anyway.</summary>
    public static bool Greyed(IReadOnlyList<double> skills) => skills.Count > 0 && skills.All(c => c < GreyedChroma);

    /// <summary><see cref="Pick"/>, and whether the crop is unlike every icon (none scores <see
    /// cref="MinScore"/>): a ready 초각스 of another class. Two icons scoring alike is no pick but not unlike.</summary>
    /// <param name="greyed">The bar is greyed out (<see cref="Greyed"/>): the icons are compared by their shapes alone, as grey.</param>
    public static (string? Name, bool Unlike) Match(Image slot, IReadOnlyDictionary<string, Image> icons, bool greyed = false) => greyed
        ? Compare(Grey(slot), icons.ToDictionary(i => i.Key, i => Grey(i.Value)), byHue: false)
        : Compare(slot, icons, byHue: true);

    static (string? Name, bool Unlike) Compare(Image slot, IReadOnlyDictionary<string, Image> icons, bool byHue)
    {
        var squares = Squares(slot);
        var scored = new List<(string Name, double Score)>();
        foreach (var (name, icon) in icons)
        {
            var whole = new PixelRect(0, 0, icon.Width, icon.Height);
            var (at, score) = Best(squares, Thumb(icon, whole));
            if (!byHue || HueDistance(Hue(slot, at), Hue(icon, whole)) <= MaxHueDistance) scored.Add((name, score));
        }
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (scored.Count == 0 || scored[0].Score < MinScore) return (null, true);
        return (scored.Count > 1 && scored[0].Score - scored[1].Score < MinLead ? null : scored[0].Name, false);
    }

    /// <summary>How alike two 일반 slot crops look greyed out: the correlation of their greyed thumbnails, both whole (-1…1).</summary>
    public static double SkillScore(Image a, Image b) =>
        BuffReader.Correlate(Thumb(Grey(a), new PixelRect(0, 0, a.Width, a.Height)), Thumb(Grey(b), new PixelRect(0, 0, b.Width, b.Height)));

    /// <summary>Every square of the slot the art may fill, with its thumbnail.</summary>
    static List<(PixelRect At, float[] Thumb)> Squares(Image slot)
    {
        var list = new List<(PixelRect, float[])>();
        int w = Math.Min(slot.Width, slot.Height);
        for (int s = (int)(w * MinSide); s <= (int)(w * MaxSide); s++)
            for (int y = 0; y + s <= slot.Height; y++)
                for (int x = 0; x + s <= slot.Width; x++)
                {
                    var r = new PixelRect(x, y, s, s);
                    list.Add((r, Thumb(slot, r)));
                }
        return list;
    }

    static (PixelRect At, double Score) Best(List<(PixelRect At, float[] Thumb)> squares, float[] icon)
    {
        var best = (At: squares[0].At, Score: -2.0);
        foreach (var (at, thumb) in squares)
        {
            double s = BuffReader.Correlate(thumb, icon);
            if (s > best.Score) best = (at, s);
        }
        return best;
    }

    /// <summary>Cols × Rows block means of B, G and R over the top of <paramref name="r"/>.</summary>
    static float[] Thumb(Image img, PixelRect r)
    {
        var t = new float[Cols * Rows * 3];
        int h = (int)(r.Height * Top);
        for (int gy = 0; gy < Rows; gy++)
        {
            int y0 = r.Y + gy * h / Rows, y1 = Math.Max(y0 + 1, r.Y + (gy + 1) * h / Rows);
            for (int gx = 0; gx < Cols; gx++)
            {
                int x0 = r.X + gx * r.Width / Cols, x1 = Math.Max(x0 + 1, r.X + (gx + 1) * r.Width / Cols), b = 0, g = 0, red = 0;
                for (int y = y0; y < y1; y++)
                    for (int i = y * img.Stride + x0 * 4, e = y * img.Stride + x1 * 4; i < e; i += 4)
                    { b += img.Bgra[i]; g += img.Bgra[i + 1]; red += img.Bgra[i + 2]; }
                float n = (x1 - x0) * (y1 - y0);
                int c = (gy * Cols + gx) * 3;
                (t[c], t[c + 1], t[c + 2]) = (b / n, g / n, red / n);
            }
        }
        return t;
    }

    /// <summary><paramref name="img"/> as the game greys it out: each pixel its luma.</summary>
    static Image Grey(Image img)
    {
        var grey = new Image(img.Width, img.Height);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int s = y * img.Stride + x * 4, d = y * grey.Stride + x * 4;
                byte l = (byte)Math.Round(ClassEmblemMatcher.Luma(img.Bgra, s));
                (grey.Bgra[d], grey.Bgra[d + 1], grey.Bgra[d + 2], grey.Bgra[d + 3]) = (l, l, l, 255);
            }
        return grey;
    }

    static double Hue(Image img, PixelRect r) =>
        SlotClassifier.ColourStats(BuffReader.Crop(img, r with { Height = (int)(r.Height * Top) })).MeanHue;

    static double HueDistance(double a, double b) { double d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }
}
