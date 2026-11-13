using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>The gauge's colours and 두께, and (under 따로) its own window: <see cref="Left"/>/<see cref="Top"/> NaN = never placed
/// (drawn where the plate had it), <see cref="Width"/> the length scale, <see cref="Height"/> the thickness scale,
/// <see cref="Upright"/> filling from the bottom (class-profiles.plan.md "합쳐서 / 따로").</summary>
public sealed class GaugeStyle : IWindowPlace
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;
    public bool Upright { get; set; }
    public string Fill { get; set; } = "#1E9BE6";
    public string Empty { get; set; } = "#3A3A3A";
    public double Thickness { get; set; } = 6;
    public double Opacity { get; set; } = 1.0;
    public GaugeReadMode ReadMode { get; set; }
}

/// <summary>When a gauge read is shown (GaugeFilter): <see cref="Agreed"/>, once 5 reads agree; <see cref="SteadyRise"/>, a small
/// rise already after 2; <see cref="Predicted"/> (beta), a rise the casts before it predict after 2 (GaugeGains).</summary>
public enum GaugeReadMode { Agreed, SteadyRise, Predicted }

/// <summary>How the gauge bar glows: <see cref="Pulse"/>, brightness breathing; <see cref="Shine"/>, a white sweep across;
/// <see cref="Outline"/>, a bright border that breathes.</summary>
public enum GlowStyle { Pulse, Shine, Outline }

/// <summary>How a ready box shows 쿨 돈 동안 계속 빛남: a band sweeping top-left → bottom-right (사선 띠) or left → right
/// (가로 띠), the whole box breathing (숨쉬기), or a border that breathes (테두리).</summary>
public enum ReadyGlowStyle { Diagonal, Horizontal, Breathe, Outline }

/// <summary>One 클래스·직업각인's gauge bar as the user set it (게이지 tab). <see cref="Splits"/> null = the classes.json
/// default. When the bar glows is class data (adenGauge), not a setting.</summary>
public sealed class GaugeBarSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double>? Splits { get; set; }
    public double SplitThickness { get; set; } = 2;   // DIP
    public string SplitColor { get; set; } = "#101010";
    /// <summary>모든 구간 같은 색: every segment in the 게이지 fill colour; off, <see cref="SegmentColors"/> left to right.</summary>
    public bool UnifiedColor { get; set; } = true;
    public List<string> SegmentColors { get; set; } = [];
    /// <summary>The class's full-section colour, over classes.json "colors" (and, without those, the 게이지 fill colour);
    /// null = the default.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FullColor { get; set; }
    /// <summary>The colour of the section still filling (바드's gold), over classes.json "colors"; null = the default.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FillingColor { get; set; }
    /// <summary>색 단계: the whole fill deepens as sections fill (classes.json "ramp"); <see cref="RampColors"/> are weak,
    /// medium, strong, null = the fill colour deepening (<see cref="Data.GaugeBar.RampDefaults"/>).</summary>
    public bool Ramp { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? RampColors { get; set; }
    public bool GlowEnabled { get; set; } = true;
    public GlowStyle GlowStyle { get; set; } = GlowStyle.Pulse;
    public double GlowPeriod { get; set; } = 1.2;     // seconds per pulse
}
