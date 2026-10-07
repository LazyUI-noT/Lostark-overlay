namespace GaugeOverlay.Core.Domain;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    /// <summary>The part both rects cover; null when they don't overlap.</summary>
    public PixelRect? Intersect(PixelRect other)
    {
        int x = Math.Max(X, other.X), y = Math.Max(Y, other.Y);
        int w = Math.Min(Right, other.Right) - x, h = Math.Min(Bottom, other.Bottom) - y;
        return w > 0 && h > 0 ? new(x, y, w, h) : null;
    }
}

/// <summary>The game's drawn area, in screen pixels (black bars already excluded).</summary>
public readonly record struct Viewport(int X, int Y, int Width, int Height);

/// <summary>Result of the skill-bar fit. <see cref="Hud"/> is the game's HUD 크기 in percent (80–110, or a
/// refined value); the nudge is a small correction in reference pixels. Converted to screen pixels only by
/// GaugeOverlay.Core.Vision.Geometry.</summary>
public sealed record Fit(double Hud, double NudgeX = 0, double NudgeY = 0);

/// <summary>직접 입력: the game's 해상도 and whether its forced 21:9 is on (<see cref="Wide"/>), as the user typed them or as the
/// last 스킬창 맞추기 found them (Settings.DetectedScreen).</summary>
public sealed record ManualScreen(int Width, int Height, bool Wide);
