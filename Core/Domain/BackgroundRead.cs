namespace GaugeOverlay.Core.Domain;

/// <summary>What the capture would show of the game with another app in front.</summary>
public enum GameView { Clear, Covered, Minimized }

/// <summary>The status line under 창 밖에서도 읽기.</summary>
public readonly record struct BehindStatus(bool Reading, GameView View, string? CoveredBy)
{
    public string Text => Reading ? "읽고 있어요"
        : View == GameView.Minimized ? "게임이 최소화되어 멈췄어요"
        : View == GameView.Covered ? $"{CoveredBy} 창이 게임을 덮고 있어 멈췄어요"
        : "다른 앱이 앞에 있어 멈췄어요";
}

/// <summary>창 밖에서도 읽기: Desktop Duplication shows whatever is on top of the game's monitor, so reads with another app in
/// front need proof that the game is what's there.</summary>
public static class BackgroundRead
{
    /// <summary>Overlap ignored on either axis: a maximised window's frame overhangs its monitor by this much.</summary>
    const int Overhang = 8;

    public static bool Covers(PixelRect game, PixelRect above)
    {
        int wide = Math.Min(game.X + game.Width, above.X + above.Width) - Math.Max(game.X, above.X);
        int tall = Math.Min(game.Y + game.Height, above.Y + above.Height) - Math.Max(game.Y, above.Y);
        return wide > Overhang && tall > Overhang;
    }

    /// <summary>The game is on screen for the capture while the switch is on: overlays stay, reads count as seen.</summary>
    public static bool ShowsBehind(bool readBehind, bool seesBehind, GameView view) =>
        readBehind && view != GameView.Minimized && (seesBehind || view == GameView.Clear);

    /// <summary>The engine's one gate. Test mode with the switch off keeps today's: it reads whatever the window shows.</summary>
    public static bool MayRead(bool readBehind, bool otherAppFront, bool testMode, bool seesBehind, GameView view) =>
        !otherAppFront || (testMode && !readBehind) || ShowsBehind(readBehind, seesBehind, view);
}
