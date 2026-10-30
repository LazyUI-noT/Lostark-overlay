namespace GaugeOverlay.Core.Domain;

/// <summary>The fit 위치 조정 opens on: the one found, or, while a preview is asked with none found (a game area never fitted, or a
/// video the fit didn't find), where HUD 100 puts the bar, for the user to move. Live and 테스트 모드 alike.</summary>
public static class PreviewStart
{
    public const double BareHud = 100;

    public static Fit? Fit(Fit? found, bool asked) => found ?? (asked ? new Fit(BareHud) : null);
}
