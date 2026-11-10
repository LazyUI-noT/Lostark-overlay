namespace GaugeOverlay.Core.Domain;

/// <summary>설정 → 초당 그리기 횟수 (user, 2026-11-10): how often an overlay window redraws while something on it moves (a
/// draining bar, a shine, a flash). One rate for every part; a window with nothing moving doesn't redraw. Pure.</summary>
public static class DrawSteps
{
    public const int Min = 10, Max = 60, Default = 30;
    /// <summary>A slow fade alone (단심 ready) never needs more than this.</summary>
    public const int FadeMax = 15;

    public static int Clamp(int perSecond) => Math.Clamp(perSecond, Min, Max);

    /// <summary>Milliseconds between frames: the clamped rate, or at most <see cref="FadeMax"/> when only a slow fade moves.</summary>
    public static double FrameMs(int perSecond, bool fadeOnly) =>
        1000.0 / (fadeOnly ? Math.Min(Clamp(perSecond), FadeMax) : Clamp(perSecond));
}
