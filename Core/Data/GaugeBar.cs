using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>How your 아덴 게이지 bar is drawn for one 클래스·직업각인: the user's settings (<see cref="GaugeBarSettings"/>)
/// over the classes.json default (<see cref="GaugeRule"/>). <see cref="SegmentColors"/> has one colour per segment.</summary>
public sealed record GaugeBar(IReadOnlyList<double> Splits, double SplitThickness, string SplitColor, IReadOnlyList<string> SegmentColors,
    GlowRule? Glow, GlowStyle Style, double Period)
{
    public static string Key(string className, string subclass) => $"{className}/{subclass}";

    /// <summary>The settings to change for the 클래스·직업각인, added the first time.</summary>
    public static GaugeBarSettings Edit(Settings s, string className, string subclass) =>
        s.GaugeBars.TryGetValue(Key(className, subclass), out var b) ? b : s.GaugeBars[Key(className, subclass)] = new();

    public static GaugeBar Resolve(Settings s, string className, string subclass, GaugeRule? rule)
    {
        var b = s.GaugeBars.GetValueOrDefault(Key(className, subclass)) ?? new GaugeBarSettings();
        IReadOnlyList<double> splits = b.Splits ?? rule?.Splits ?? [];
        string fill = b.FullColor ?? rule?.Colors?.Full ?? s.Gauge.Fill;
        var colors = Enumerable.Range(0, splits.Count + 1)
            .Select(i => !b.UnifiedColor && i < b.SegmentColors.Count ? b.SegmentColors[i] : fill).ToList();
        var ramp = RampDefaults(fill);
        return new(splits, b.SplitThickness, b.SplitColor, colors, b.GlowEnabled ? rule?.Glow : null, b.GlowStyle, b.GlowPeriod)
        {
            Fill = fill,
            FillingColor = b.FillingColor ?? rule?.Colors?.Filling,
            Ramp = b.Ramp ? rule?.Ramp ?? [] : [],
            RampColors = [.. ramp.Select((c, i) => b.RampColors is { } mine && i < mine.Count ? mine[i] : c)],
        };
    }

    /// <summary>The class's full-section colour: the user's, else classes.json "colors", else the 게이지 fill colour.
    /// The ramp deepens from it.</summary>
    public string Fill { get; init; } = "";

    /// <summary>The colour of the section still filling (바드's gold); null = drawn like the others.</summary>
    public string? FillingColor { get; init; }

    /// <summary>색 단계 (<see cref="GaugeRule.Ramp"/>): levels 1 weak, 2 medium, 3 strong; empty = off.</summary>
    public IReadOnlyList<int> Ramp { get; init; } = [];

    /// <summary>The weak, medium and strong colours.</summary>
    public IReadOnlyList<string> RampColors { get; init; } = [];

    /// <summary>The ramp's default colours: <paramref name="fill"/> deepening, one hue.</summary>
    public static IReadOnlyList<string> RampDefaults(string fill) => [Deepen(fill, 0.15), Deepen(fill, 0.3), Deepen(fill, 0.45)];

    static string Deepen(string hex, double t)
    {
        if (hex.Length != 7 || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int rgb)) return hex;
        int Part(int shift) => (int)Math.Round(((rgb >> shift) & 0xFF) * (1 - t));
        return $"#{Part(16):X2}{Part(8):X2}{Part(0):X2}";
    }

    /// <summary>Each segment's colour at gauge <paramref name="value"/>: with a ramp and at least one full section, every
    /// segment in the colour for that many (past the rule's list, its last level); otherwise <see cref="SegmentColors"/>.
    /// With a <see cref="FillingColor"/>, the section still filling takes it instead.</summary>
    public IReadOnlyList<string> ColorsAt(double value)
    {
        int full = Segments.Count(seg => value >= seg.To - 1e-9);
        var colors = Ramp.Count == 0 || full == 0 ? [.. SegmentColors]
            : Enumerable.Repeat(RampColors[Ramp[Math.Min(full, Ramp.Count) - 1] - 1], SegmentColors.Count).ToList();
        if (FillingColor is { } filling && full < colors.Count) colors[full] = filling;
        return colors;
    }

    /// <summary>Sets the 클래스·직업각인's splits. Per-segment colours stay with their segments: a new segment takes the
    /// colour of the old one that started at the same place, else of the one its middle was in.</summary>
    public static void SetSplits(Settings s, string className, string subclass, GaugeRule? rule, IEnumerable<double> splits)
    {
        var old = Resolve(s, className, subclass, rule);
        var b = Edit(s, className, subclass);
        b.Splits = [.. splits.Distinct().Order()];
        if (b.UnifiedColor) return;
        var before = old.Segments.ToList();
        int Old(double from, double to)
        {
            int same = before.FindIndex(o => o.From == from);
            double mid = (from + to) / 2;
            return same >= 0 ? same : Math.Max(0, before.FindIndex(o => o.From <= mid && mid < o.To));
        }
        b.SegmentColors = [.. (old with { Splits = b.Splits }).Segments.Select(seg => old.SegmentColors[Old(seg.From, seg.To)])];
    }

    /// <summary>The segments between the splits, 0 → 1.</summary>
    public IReadOnlyList<(double From, double To)> Segments =>
        [.. Enumerable.Range(0, Splits.Count + 1).Select(i => (i == 0 ? 0 : Splits[i - 1], i == Splits.Count ? 1 : Splits[i]))];

    /// <summary>Which segments glow at gauge <paramref name="value"/>: each full one (sections), or every one with fill while
    /// the rule's slot is ready and the gauge is at least its minimum (slotReady).</summary>
    public bool[] Glowing(double value, bool slotReady) =>
    [
        .. Segments.Select(seg => Glow switch
        {
            { When: GlowWhen.Sections } => value >= seg.To - 1e-9,
            { When: GlowWhen.SlotReady } g => slotReady && value >= g.MinGauge && value > seg.From,
            _ => false,
        }),
    ];

    /// <summary>The glowing part as one span, 0 → the end of the last glowing segment cut to the fill, so the glow runs
    /// continuously across the splits; null when nothing glows. Glowing segments always start at 0 (full ones, or ones
    /// with fill), so one span covers them.</summary>
    public (double From, double To)? GlowSpan(double value, bool slotReady)
    {
        int last = Array.LastIndexOf(Glowing(value, slotReady), true);
        return last < 0 ? null : (0, Math.Min(value, Segments[last].To));
    }

    /// <summary>The glow's brightness, 0 → 1 → 0 over <paramref name="periodSeconds"/>, from one clock
    /// (<paramref name="ms"/>) so every glowing segment pulses together.</summary>
    public static double GlowLevel(double ms, double periodSeconds) =>
        (1 - Math.Cos(2 * Math.PI * ms / (Math.Max(0.1, periodSeconds) * 1000))) / 2;
}
