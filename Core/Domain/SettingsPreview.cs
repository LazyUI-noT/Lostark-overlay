namespace GaugeOverlay.Core.Domain;

/// <summary>설정's preview pane (settings-design.plan.md §1): which overlay windows a page sets, and whether the preview fits
/// beside the form or goes above the rows.</summary>
public static class SettingsPreview
{
    /// <summary><see cref="FormWidth"/>: the narrowest a form gets beside the preview. <see cref="FormWidest"/>: the content
    /// column's widest, on every page (<c>.interface-design/system.md</c>, "Page frame").</summary>
    public const double FormWidth = 620, FormWidest = 720, Gap = 24, PreviewWidth = 360;

    /// <summary>The windows a 설정 area changes the look of; empty for pages without one (the preview isn't shown).
    /// 파티 공유 only for its 파티원 모양 section: the rest of the page is the connection.</summary>
    public static IReadOnlyList<OverlayPart> PartsOf(string area, string? section) => area switch
    {
        "스킬창" => [OverlayPart.Skills, OverlayPart.Gauge],
        "아덴 게이지" => [OverlayPart.Gauge],
        "버프 줄" => [OverlayPart.Buffs],
        "단심" => [OverlayPart.Judgment],
        "암수" => [OverlayPart.Amsu],
        "DPS 미터기" => [OverlayPart.Meter],
        "레이드" => [OverlayPart.BarCount, OverlayPart.Debuffs],
        "파티 공유" when section == "파티원 모양" => [OverlayPart.Party],
        _ => [],
    };

    /// <summary>True when the page is wide enough for the form, a gap and the preview side by side (NaN and negative: false).</summary>
    public static bool Beside(double pageWidth) => pageWidth >= FormWidth + Gap + PreviewWidth;

    /// <summary>The form's width beside the preview: what the preview and the gap leave, from <see cref="FormWidth"/> to
    /// <see cref="FormWidest"/>, so a wider form never pushes the preview off the page.</summary>
    public static double FormBeside(double pageWidth) => Math.Clamp(pageWidth - Gap - PreviewWidth, FormWidth, FormWidest);
}
