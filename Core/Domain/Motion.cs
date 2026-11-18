namespace GaugeOverlay.Core.Domain;

/// <summary>Whether 설정 animates (ui-motion-tokens.plan.md phase 3, <c>.interface-design/system.md</c> "Motion"): off when
/// Windows' 애니메이션 효과 is off, in a remote session, or by 일반 → 애니메이션 (<see cref="Settings.Animations"/>). Off means
/// the end state at once, never a shortened version.</summary>
public static class Motion
{
    public static bool Runs(bool windowsAnimates, bool remoteSession, bool appSwitch) =>
        windowsAnimates && !remoteSession && appSwitch;

    /// <summary>system.md's <c>Out</c>, cubic-bezier(0.23, 1, 0.32, 1), at time <paramref name="t"/> (0–1, clamped): for what
    /// can't use WPF's KeySpline (the WinForms tray menu's fade). Both control points have y = 1, so y = 1 − (1 − s)³; x(s) is
    /// solved for s by bisection (x rises with s, 30 halvings is far below a pixel or an opacity step).</summary>
    public static double Out(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        double lo = 0, hi = 1;
        for (int i = 0; i < 30; i++)
        {
            var s = (lo + hi) / 2;
            if (X(s) < t) lo = s; else hi = s;
        }
        var u = 1 - (lo + hi) / 2;
        return 1 - u * u * u;

        static double X(double s) => 3 * (1 - s) * (1 - s) * s * 0.23 + 3 * (1 - s) * s * s * 0.32 + s * s * s;
    }
}
