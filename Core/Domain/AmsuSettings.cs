namespace GaugeOverlay.Core.Domain;

/// <summary>How the 암수 창 shows the time left: a bar, a circle eaten away clockwise, the seconds as a number, or an upright
/// buff-style bar with no icon emptying top to bottom (세로막대, user 2026-11-04). Saved by number: add at the end.</summary>
public enum AmsuLook { 막대, 원, 숫자, 세로막대 }

/// <summary>Which way the 막대 empties toward.</summary>
public enum BarDirection { 오른쪽, 왼쪽, 위, 아래 }

/// <summary>설정 → 암흑수류탄 (amsu.design.md): the switch (off by default; any boss), the look, colours, opacity, the warning under
/// a few seconds, and the window's place and size (NaN = next to the overlay until first placed).</summary>
public sealed class AmsuSettings : IWindowPlace
{
    public const int MaxWarnSeconds = 10;
    public const double MinOpacity = 0.1;

    public bool On { get; set; }
    /// <summary>읽기 빈도: 낮음 reads far less, for frame drops (AmsuPace).</summary>
    public AmsuRate Rate { get; set; } = AmsuRate.높음;
    public AmsuLook Look { get; set; } = AmsuLook.막대;
    public BarDirection Direction { get; set; } = BarDirection.오른쪽;
    /// <summary>숫자: the seconds beside the icon instead of on it.</summary>
    public bool NumberBeside { get; set; }
    /// <summary>세로막대: the seconds left over the bar.</summary>
    public bool ShowSeconds { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public string Color { get; set; } = "#9B6BFF";
    public string WarnColor { get; set; } = "#E05252";
    /// <summary>Under this many seconds the 막대 / 원 / 숫자 turns <see cref="WarnColor"/> and blinks (0: never).</summary>
    public int WarnSeconds { get; set; } = 5;
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;

    /// <summary>A separate copy (오버레이 배치 keeps one each).</summary>
    public AmsuSettings Copy() => (AmsuSettings)MemberwiseClone();

    /// <summary>Hand edits and old files back into range.</summary>
    public void Clamp()
    {
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, MinOpacity, 1) : 1;
        WarnSeconds = Math.Clamp(WarnSeconds, 0, MaxWarnSeconds);
        Width = double.IsFinite(Width) ? Math.Clamp(Width, ClassProfiles.MinScale, ClassProfiles.MaxScale) : 1;
        Height = double.IsFinite(Height) ? Math.Clamp(Height, ClassProfiles.MinScale, ClassProfiles.MaxScale) : 1;
        if (!Enum.IsDefined(Look)) Look = AmsuLook.막대;
        if (!Enum.IsDefined(Rate)) Rate = AmsuRate.높음;
        if (!Enum.IsDefined(Direction)) Direction = BarDirection.오른쪽;
        Color ??= "#9B6BFF";
        WarnColor ??= "#E05252";
    }
}
