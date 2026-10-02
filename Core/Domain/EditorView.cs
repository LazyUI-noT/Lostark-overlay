namespace GaugeOverlay.Core.Domain;

/// <summary>레이아웃 편집기: how the screenshot (world = screen DIP) is shown in the editor's view: view = world × Scale +
/// Offset. The wheel zooms around the cursor, a drag pans.</summary>
public sealed class EditorView(double scale, double offsetX, double offsetY)
{
    public const double MinScale = 0.1, MaxScale = 8;
    const double Side = 0.33;   // the first view leaves out this much of the screen on the left, right and top

    public double Scale { get; private set; } = scale;
    public double OffsetX { get; private set; } = offsetX;
    public double OffsetY { get; private set; } = offsetY;

    public (double X, double Y) ToView(double x, double y) => (x * Scale + OffsetX, y * Scale + OffsetY);
    public (double X, double Y) ToWorld(double x, double y) => ((x - OffsetX) / Scale, (y - OffsetY) / Scale);

    public void ZoomAt(double x, double y, double factor)
    {
        var (wx, wy) = ToWorld(x, y);
        Scale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        (OffsetX, OffsetY) = (x - wx * Scale, y - wy * Scale);
    }

    public void Pan(double dx, double dy) => (OffsetX, OffsetY) = (OffsetX + dx, OffsetY + dy);

    /// <summary>원래 크기: 100%, the screen at the game's own size, around view point (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void ActualSize(double x, double y) => ZoomAt(x, y, 1 / Scale);

    /// <summary>전체 화면: a view that is the screen itself, so everything lies exactly where the game draws it.</summary>
    public static EditorView FullScreen() => new(1, 0, 0);

    /// <summary>The lower middle of a <paramref name="worldW"/> × <paramref name="worldH"/> screen, where the skill bar
    /// is, fitted and centred in a <paramref name="viewW"/> × <paramref name="viewH"/> view.</summary>
    public static EditorView Initial(double viewW, double viewH, double worldW, double worldH)
    {
        double x = worldW * Side, y = worldH * Side, w = worldW - 2 * x, h = worldH - y;
        double scale = Math.Clamp(Math.Min(viewW / w, viewH / h), MinScale, MaxScale);
        return new(scale, viewW / 2 - (x + w / 2) * scale, viewH / 2 - (y + h / 2) * scale);
    }
}

/// <summary>Where an overlay window goes while its spot is unset (NaN): yours centred above the skill bar, the party
/// windows right of the centre one under another, the buff window just under yours. In DIP, from the work area.</summary>
public static class WindowDefaults
{
    public const double AboveBottom = 260;          // the window's bottom this far above the work area's
    public const double PeerOffsetX = 160, PeerRow = 96;   // party slot i: left edge right of the centre, stacked
    public const double BuffsBelow = 40;            // the buff window's top this far under your default spot's bottom
    public const double DebuffsRight = 260;         // the debuff window this far right of the buff window's default spot

    /// <param name="width">The window's width (yours: to centre it).</param>
    /// <param name="height">The window's height (yours: its bottom sits <see cref="AboveBottom"/> up).</param>
    public static (double Left, double Top) Place(double areaLeft, double areaWidth, double areaBottom, double width, double height,
        int peer, bool buffs)
    {
        if (peer >= 0)
            return (areaLeft + areaWidth / 2 + PeerOffsetX, areaBottom - AboveBottom - (Net.Membership.MaxMembers - 1 - peer) * PeerRow);
        double left = areaLeft + (areaWidth - width) / 2;
        return buffs ? (left, areaBottom - AboveBottom + BuffsBelow) : (left, areaBottom - height - AboveBottom);
    }
}
