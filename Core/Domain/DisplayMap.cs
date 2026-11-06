namespace GaugeOverlay.Core.Domain;

/// <summary>A display, in the units of the windows' Left and Top (DIP).</summary>
public sealed record DisplayArea(string Device, double Left, double Top, double Width, double Height, bool Primary)
{
    public bool Holds(double x, double y) => x >= Left && x < Left + Width && y >= Top && y < Top + Height;
}

/// <summary>표시 화면. Stateless: a window off the target moves onto it wherever it was, so nothing about the last move is saved.</summary>
public static class DisplayMap
{
    public const string GameDisplay = "game";

    /// <summary>The display the windows belong on; null = they stay where they are.</summary>
    public static DisplayArea? Target(string setting, IReadOnlyList<DisplayArea> displays, string? gameDevice)
    {
        if (setting.Length == 0) return null;
        var game = displays.FirstOrDefault(d => d.Device == gameDevice);
        if (setting == GameDisplay) return game;
        return displays.FirstOrDefault(d => d.Device == setting) ?? game ?? displays.FirstOrDefault(d => d.Primary);
    }

    /// <summary>A window's spot on <paramref name="target"/>: kept if its middle is there, else the same share of the display,
    /// inside it. A default spot (NaN) stays default.</summary>
    public static (double Left, double Top) Into(double left, double top, double width, double height, IReadOnlyList<DisplayArea> displays, DisplayArea target)
    {
        if (double.IsNaN(left) || double.IsNaN(top)) return (left, top);
        var (x, y) = (left + width / 2, top + height / 2);
        if (target.Holds(x, y)) return (left, top);
        var from = displays.FirstOrDefault(d => d.Holds(x, y)) ?? displays.FirstOrDefault(d => d.Primary) ?? target;
        double l = target.Left + (left - from.Left) / from.Width * target.Width;
        double t = target.Top + (top - from.Top) / from.Height * target.Height;
        return (Inside(l, target.Left, target.Width, width), Inside(t, target.Top, target.Height, height));
    }

    /// <summary>Math.Clamp throws when the window is larger than the display.</summary>
    static double Inside(double at, double start, double length, double size) => Math.Max(start, Math.Min(at, start + length - size));
}
