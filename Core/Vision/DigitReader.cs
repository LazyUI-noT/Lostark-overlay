using System.Numerics;
using System.Runtime.CompilerServices;
using GaugeOverlay.Core.Domain;

[assembly: InternalsVisibleTo("GaugeOverlay.Tests")]

namespace GaugeOverlay.Core.Vision;

/// <summary>Reads the game's white cooldown text ("14s", "3m") in a slot crop by normalised cross-correlation
/// against glyph templates resized to the fitted scale. Works on the min of R,G,B, so coloured pixels (the
/// yellow "99+" counts, orange icons) look dark and never form glyphs. The unit ('s' or 'm') is found first,
/// right of the slot centre; the digits are then read leftwards from it.
/// Not thread-safe (it reuses its buffers): one instance per reading thread.</summary>
public sealed class DigitReader
{
    private const double SmallFont = 10.0 / 13;   // 아덴 slots draw their text smaller
    private const double Baseline = 0.615;        // bottom of the glyph box, as a share of the crop height
    private const float MinUnit = 0.6f, MinDigit = 0.6f, MinContrast = 25;
    // Stack count: 0.65 of the timer font. Four recordings: counts score ≥ 0.84, icon art beside a timer ≤ 0.73.
    private const double CountFont = 0.65;
    // A tens digit is the same outlined white text as the ones digit, so its Stroke is about as strong: 0.82+ of the
    // ones digit's on every frame of the four recordings. NCC ignores brightness, and the grey edge of the cooldown
    // sweep beside a single digit matches the '1' template near MinDigit with only 0.35–0.62 of it.
    private const double MinTensStroke = 0.7;
    // The tens digit is as white as the ones digit: its strokes' chroma is at most 16 above the ones digit's on every
    // two-digit fixture. A light-cyan streak of icon art is as bright in min(R, G, B) but 76+ above (sBug.mp4, S's crystal
    // edge read as a tens '1': "1s" → 11).
    private const double MaxTensTint = 40;
    private const float MinCount = 0.8f;
    // A count is white with a dark outline: its Stroke is 124–136 on the four count fixtures. A bright streak of icon art
    // uncovered by the sweep can match the '1' at 0.83–0.85 but only reaches 69–91 (bugEskill.mp4, D's lightning).
    private const double MinCountStroke = 108;
    /// <summary>The characters the generated templates (DigitTemplates, FrameTemplate) use for grey 0..255 in 10 steps.</summary>
    internal const string GreyRamp = " .:-=+*#%@";
    private static readonly int Lanes = Vector<float>.Count;

    private readonly Glyph[] _main, _small;
    private readonly Glyph[] _count;                        // stack counts, bottom-right of the icon
    private float[] _grey = [];                   // min(R,G,B), rows of _gs floats
    private float[] _chroma = [];                 // max(R,G,B) − min(R,G,B), same layout
    private int[] _sum = [];                      // integral images of grey and grey², (_w+1)×(_h+1)
    private long[] _sq = [];
    private int _w, _h, _gs;

    /// <summary>A reader with every template resized once to <paramref name="scale"/> (screen pixels per
    /// reference pixel, <see cref="Geometry.Scale"/>).</summary>
    public DigitReader(double scale)
    {
        Scale = scale;
        _main = Templates(scale);
        _small = Templates(scale * SmallFont);
        _count = [.. DigitTemplates.Main.Where(t => t.Ch is >= '1' and <= '9').Select(t => Glyph.FromRows(t.Ch, t.Rows, scale * CountFont)),
                  .. DigitTemplates.Count.Select(t => Glyph.FromRows(t.Ch, t.Rows, scale))];
    }

    private static Glyph[] Templates(double scale) =>
        DigitTemplates.Main.Select(t => Glyph.FromRows(t.Ch, t.Rows, scale)).ToArray();

    /// <summary>Screen pixels per reference pixel (<see cref="Geometry.Scale"/>).</summary>
    public double Scale { get; }

    /// <summary>Scores (NCC, −1..1) of the last <see cref="Read"/>'s unit and digits, for diagnostics.</summary>
    public (float Unit, float Digit0, float Digit1) LastScores { get; private set; }

    /// <summary>Reads the text in a slot crop. Kind is <see cref="ReadKind.Seconds"/> or <see cref="ReadKind.Minutes"/>
    /// with the number, <see cref="ReadKind.CooldownIllegible"/> when the unit is there but no digit is, or
    /// <see cref="ReadKind.Ready"/> when there is no text.</summary>
    /// <remarks>The templates are fixed. Learning the user's glyphs from confirmed readings (PLAN §7 item 10) was
    /// removed: a confirmation can rest on an earlier misread, so it made misreads self-reinforcing ("19s" → 49) and
    /// added phantom leading 1s, while the built-in templates read live_test2 correctly on their own.</remarks>
    public (ReadKind Kind, int Value) Read(Image crop, SlotId slot)
    {
        Load(crop);
        var set = Set(slot);
        var unit = FindUnit(set);
        if (unit.Score < MinUnit) { LastScores = (unit.Score, 0, 0); return (ReadKind.Ready, 0); }

        var d0 = FindDigit(set, unit.X, unit.Bottom, (int)Math.Ceiling(3 * Scale));
        var d1 = d0.Score >= MinDigit ? FindDigit(set, d0.X, unit.Bottom, 0) : default;
        LastScores = (unit.Score, d0.Score, d1.Score);
        if (d0.Score < MinDigit) return (ReadKind.CooldownIllegible, 0);
        bool tens = d1.Score >= MinDigit && Stroke(set[d1.Index], d1) >= MinTensStroke * Stroke(set[d0.Index], d0)
            && Chroma(set[d1.Index], d1) - Chroma(set[d0.Index], d0) < MaxTensTint;
        int value = tens ? d1.Index * 10 + d0.Index : d0.Index;
        return (set[unit.Index].Ch == 'm' ? ReadKind.Minutes : ReadKind.Seconds, value);
    }

    /// <summary>True when a stack count (1..9) sits in the bottom-right corner of <paramref name="crop"/>, as on a
    /// stack skill recharging with a use left (live_test2 E "1", live test Q "1"). The count's font is smaller than
    /// the timer's; its '2' is drawn differently and has a template of its own (Q's "2" in live_test2).</summary>
    public bool ShowsCount(Image crop)
    {
        Load(crop);
        foreach (var g in _count)
        {
            for (int b = Math.Max(g.H, (int)(_h * 0.75)); b <= Math.Min(_h, (int)(_h * 0.95)); b++)
                for (int x = (int)(_w * 0.62); x <= _w - g.W; x++)
                    if (Ncc(g, x, b - g.H) is var s && s >= MinCount && Stroke(g, new(0, x, b, s)) >= MinCountStroke) return true;
        }
        return false;
    }

    private Glyph[] Set(SlotId slot) => slot is SlotId.아덴1 or SlotId.아덴2 ? _small : _main;

    private void Load(Image crop)
    {
        int w = _w = crop.Width, h = _h = crop.Height, gs = _gs = w + Lanes, w1 = w + 1;
        if (_grey.Length < gs * h) (_grey, _chroma) = (new float[gs * h], new float[gs * h]);
        if (_sum.Length < w1 * (h + 1)) { _sum = new int[w1 * (h + 1)]; _sq = new long[w1 * (h + 1)]; }
        Array.Clear(_sum, 0, w1);
        Array.Clear(_sq, 0, w1);
        var px = crop.Bgra;
        for (int y = 0; y < h; y++)
        {
            int s = y * crop.Stride, above = y * w1, here = above + w1, row = 0;
            long rowSq = 0;
            _sum[here] = 0; _sq[here] = 0;
            for (int x = 0; x < w; x++, s += 4)
            {
                int v = Math.Min(px[s], Math.Min(px[s + 1], px[s + 2]));
                _grey[y * gs + x] = v;
                _chroma[y * gs + x] = Math.Max(px[s], Math.Max(px[s + 1], px[s + 2])) - v;
                row += v; rowSq += v * v;
                _sum[here + x + 1] = _sum[above + x + 1] + row;
                _sq[here + x + 1] = _sq[above + x + 1] + rowSq;
            }
        }
    }

    private readonly record struct Match(int Index, int X, int Bottom, float Score);

    /// <summary>The unit sits right of centre: from half its width left of centre (one digit) to about one
    /// digit right of it (two digits). Its bottom is near <see cref="Baseline"/>.</summary>
    private Match FindUnit(Glyph[] set)
    {
        int bottom = (int)Math.Round(_h * Baseline), tol = Math.Max(2, (int)Math.Round(2 * Scale));
        var best = new Match(-1, 0, 0, -1);
        for (int i = 10; i <= 11; i++)
        {
            var g = set[i];
            int xLo = Math.Max(0, _w / 2 - g.W / 2 - 2), xHi = Math.Min(_w - g.W, _w / 2 + set[0].W + 2);
            for (int b = Math.Max(g.H, bottom - tol); b <= Math.Min(_h, bottom + tol); b++)
                for (int x = xLo; x <= xHi; x++)
                {
                    float s = Ncc(g, x, b - g.H);
                    if (s > best.Score) best = new(i, x, b, s);
                }
        }
        return best;
    }

    /// <summary>Best digit whose box ends just left of <paramref name="rightX"/>, sharing the unit's bottom ±1. A '1'
    /// is cut narrower than its cell, so its box can end further left by the width it lacks — otherwise the tens '1'
    /// of "11" falls outside the window and a wider digit's stem ('4') is matched onto it instead. The box may overlap
    /// <paramref name="rightX"/> by <paramref name="after"/> px: a few for the ones digit beside the unit, none for the
    /// tens digit, which never overlaps the ones digit in the recordings, whereas a bright sweep line right after a
    /// cast matched a '1' 3 px into it (gameplay.mp4 일반6 "8s" → 18).</summary>
    private Match FindDigit(Glyph[] set, int rightX, int bottom, int after)
    {
        int before = (int)Math.Ceiling(5 * Scale);
        var best = new Match(-1, 0, 0, -1);
        for (int i = 0; i <= 9; i++)
        {
            var g = set[i];
            int narrow = Math.Max(0, set[0].W - g.W);
            int xLo = Math.Max(0, rightX - g.W - before - narrow), xHi = Math.Min(_w - g.W, rightX - g.W + after);
            for (int b = Math.Max(g.H, bottom - 1); b <= Math.Min(_h, bottom + 1); b++)
                for (int x = xLo; x <= xHi; x++)
                {
                    float s = Ncc(g, x, b - g.H);
                    if (s > best.Score) best = new(i, x, b, s);
                }
        }
        return best;
    }

    /// <summary>Mean chroma (max − min of R, G, B) under <paramref name="g"/>'s strokes at match <paramref name="m"/>.</summary>
    private double Chroma(Glyph g, Match m)
    {
        double on = 0;
        int nOn = 0, y0 = m.Bottom - g.H;
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
                if (g.Padded[y * g.Wp + x] > 0) { on += _chroma[(y0 + y) * _gs + m.X + x]; nOn++; }
        return on / nOn;
    }

    /// <summary>Mean grey under <paramref name="g"/>'s strokes minus the mean around them, at match <paramref name="m"/>.</summary>
    private double Stroke(Glyph g, Match m)
    {
        double on = 0, off = 0;
        int nOn = 0, y0 = m.Bottom - g.H;
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                float v = _grey[(y0 + y) * _gs + m.X + x];
                if (g.Padded[y * g.Wp + x] > 0) { on += v; nOn++; } else off += v;
            }
        return on / nOn - off / (g.W * g.H - nOn);
    }

    /// <summary>NCC of <paramref name="g"/> with the grey patch at (x0, y0); 0 for low-contrast patches.</summary>
    private float Ncc(Glyph g, int x0, int y0)
    {
        int w1 = _w + 1, a = y0 * w1 + x0, b = (y0 + g.H) * w1 + x0;
        float n = g.W * g.H;
        float s1 = _sum[b + g.W] - _sum[b] - _sum[a + g.W] + _sum[a];
        float s2 = _sq[b + g.W] - _sq[b] - _sq[a + g.W] + _sq[a];
        float var = s2 - s1 * s1 / n;
        if (var < MinContrast * MinContrast * n) return 0;
        var acc = Vector<float>.Zero;
        var t = g.Padded;
        for (int y = 0, ti = 0; y < g.H; y++)
            for (int k = 0, gi = (y0 + y) * _gs + x0; k < g.Wp; k += Lanes, ti += Lanes)
                acc += new Vector<float>(_grey, gi + k) * new Vector<float>(t, ti);
        return Vector.Sum(acc) / MathF.Sqrt(var);
    }

    internal sealed class Glyph
    {
        public readonly char Ch;
        public readonly int W, H, Wp;           // Wp: W rounded up to whole vectors
        public readonly float[] Padded;         // zero-mean, unit-norm template, rows of Wp (zero padded)
        private readonly float[] _raw;          // grey 0..255, rows of W

        private Glyph(char ch, int w, int h, float[] raw)
        {
            (Ch, W, H, _raw) = (ch, w, h, raw);
            Wp = (w + Lanes - 1) / Lanes * Lanes;
            Padded = new float[Wp * h];
            Normalise();
        }

        public static Glyph FromRows(char ch, string[] rows, double scale)
        {
            int sw = rows[0].Length, sh = rows.Length;
            var src = new float[sw * sh];
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++) src[y * sw + x] = GreyRamp.IndexOf(rows[y][x]) * 28.3f;
            int w = Math.Max(3, (int)Math.Round(sw * scale)), h = Math.Max(3, (int)Math.Round(sh * scale));
            var dst = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    dst[y * w + x] = Bilinear(src, sw, sh, (x + 0.5) * sw / w - 0.5, (y + 0.5) * sh / h - 0.5);
            return new Glyph(ch, w, h, dst);
        }

        private void Normalise()
        {
            float mean = _raw.Average(), norm = 0;
            foreach (float v in _raw) norm += (v - mean) * (v - mean);
            norm = MathF.Sqrt(norm);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++) Padded[y * Wp + x] = (_raw[y * W + x] - mean) / norm;
        }

        internal static float Bilinear(float[] g, int w, int h, double fx, double fy)
        {
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            float ax = (float)(fx - x0), ay = (float)(fy - y0);
            float At(int x, int y) => g[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
            return (At(x0, y0) * (1 - ax) + At(x0 + 1, y0) * ax) * (1 - ay) + (At(x0, y0 + 1) * (1 - ax) + At(x0 + 1, y0 + 1) * ax) * ay;
        }
    }
}
