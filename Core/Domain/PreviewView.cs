namespace GaugeOverlay.Core.Domain;

/// <summary>A rect in screen DIP (the canvas's world) or in minimap pixels.</summary>
public readonly record struct ViewRect(double X, double Y, double Width, double Height);

/// <summary>A 설정 page's 게임 화면 preview (preview-images.plan.md §3): the picture at actual size (zoom 1 = one game pixel per
/// screen pixel), cropped to the preview box around a world point. Zoom and pan are the view only: never saved, no undo step.
/// Near the picture's edge the crop stops <see cref="EdgeRoom"/> past it, so its border shows and the preview is never blank.</summary>
public sealed class PreviewView
{
    public const double MinZoom = 0.25, MaxZoom = 4, ZoomStep = 1.25;
    public const double EdgeRoom = 24;   // view px the crop may run past the picture's edge

    public PreviewView(ViewRect picture, double viewWidth, double viewHeight)
    {
        Picture = picture;
        (ViewWidth, ViewHeight) = (viewWidth, viewHeight);
        (CentreX, CentreY) = (picture.X + picture.Width / 2, picture.Y + picture.Height / 2);
        Clamp();
    }

    public ViewRect Picture { get; }
    public double ViewWidth { get; private set; }
    public double ViewHeight { get; private set; }
    public double Zoom { get; private set; } = 1;

    /// <summary>The world point at the middle of the preview.</summary>
    public double CentreX { get; private set; }
    public double CentreY { get; private set; }

    /// <summary>The part of the world shown.</summary>
    public ViewRect Crop => new(CentreX - ViewWidth / 2 / Zoom, CentreY - ViewHeight / 2 / Zoom, ViewWidth / Zoom, ViewHeight / Zoom);

    /// <summary>view = world × Scale + Offset.</summary>
    public (double Scale, double OffsetX, double OffsetY) Transform => (Zoom, ViewWidth / 2 - CentreX * Zoom, ViewHeight / 2 - CentreY * Zoom);

    public void Resize(double viewWidth, double viewHeight)
    {
        (ViewWidth, ViewHeight) = (viewWidth, viewHeight);
        Clamp();
    }

    /// <summary>The page's window in the middle (on opening, 창으로 돌아가기, and while 창 위치 옮기기 drags it).</summary>
    public void CentreOn(ViewRect window)
    {
        (CentreX, CentreY) = (window.X + window.Width / 2, window.Y + window.Height / 2);
        Clamp();
    }

    /// <summary>Zooms about the middle (the window stays centred).</summary>
    public void ZoomBy(double factor)
    {
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        Clamp();
    }

    /// <summary>A drag of (<paramref name="dx"/>, <paramref name="dy"/>) view px: the picture follows the pointer.</summary>
    public void Pan(double dx, double dy)
    {
        CentreX -= dx / Zoom;
        CentreY -= dy / Zoom;
        Clamp();
    }

    /// <summary>A drag in view px as world px: how far 창 위치 옮기기 moves the window.</summary>
    public (double X, double Y) ToWorld(double dx, double dy) => (dx / Zoom, dy / Zoom);

    /// <summary>A click on the minimap at (<paramref name="fx"/>, <paramref name="fy"/>), fractions of the picture: the view goes there.</summary>
    public void JumpTo(double fx, double fy)
    {
        (CentreX, CentreY) = (Picture.X + fx * Picture.Width, Picture.Y + fy * Picture.Height);
        Clamp();
    }

    /// <summary>The minimap's box dragged by (<paramref name="dfx"/>, <paramref name="dfy"/>), fractions of the picture.</summary>
    public void PanMinimap(double dfx, double dfy)
    {
        CentreX += dfx * Picture.Width;
        CentreY += dfy * Picture.Height;
        Clamp();
    }

    /// <summary>The crop on a <paramref name="mapWidth"/> × <paramref name="mapHeight"/> minimap of the picture.</summary>
    public ViewRect MinimapBox(double mapWidth, double mapHeight)
    {
        var c = Crop;
        double sx = mapWidth / Picture.Width, sy = mapHeight / Picture.Height;
        return new((c.X - Picture.X) * sx, (c.Y - Picture.Y) * sy, c.Width * sx, c.Height * sy);
    }

    /// <summary>Where the picture lies on screen, in DIP: a saved shot over its <paramref name="area"/> (physical px); the sample
    /// screen or 단색 over the screen. Null: a saved picture with no area (not the monitor's shape), a backdrop only, shown whole
    /// with no crop, minimap or pan.</summary>
    public static ViewRect? PictureRect(PixelRect? area, bool saved, double dpiX, double dpiY, double screenWidth, double screenHeight) =>
        area is { Width: > 0, Height: > 0 } a ? new(a.X / dpiX, a.Y / dpiY, a.Width / dpiX, a.Height / dpiY)
        : saved ? null
        : new(0, 0, screenWidth, screenHeight);

    void Clamp()
    {
        CentreX = Axis(CentreX, Picture.X, Picture.Width, ViewWidth);
        CentreY = Axis(CentreY, Picture.Y, Picture.Height, ViewHeight);
    }

    double Axis(double centre, double start, double size, double view)
    {
        double half = view / 2 / Zoom, room = EdgeRoom / Zoom;
        double lo = start + half - room, hi = start + size - half + room;
        return lo > hi ? start + size / 2 : Math.Clamp(centre, lo, hi);
    }
}

/// <summary>A window's saved spot (its top-left, the label strip included; NaN: never placed) by the part a 설정 page sets.
/// Buttons drawn in my window (buff bars not 따로, the gauge not apart) move my window, as the canvas does.</summary>
public static class WindowSpot
{
    public static (double Left, double Top) Get(Settings s, OverlayPart part) => part switch
    {
        OverlayPart.Buffs when s.Buffs.Detached => (s.Buffs.Left, s.Buffs.Top),
        OverlayPart.Gauge when s.Apart => (s.Gauge.Left, s.Gauge.Top),
        OverlayPart.Debuffs => (s.Debuffs.Left, s.Debuffs.Top),
        OverlayPart.Judgment => (s.Judgment.Left, s.Judgment.Top),
        OverlayPart.Amsu => (s.Amsu.Left, s.Amsu.Top),
        OverlayPart.BarCount => (s.Raids.Left, s.Raids.Top),
        OverlayPart.Meter => (s.Meter.Left, s.Meter.Top),
        OverlayPart.Party when s.Party.Together => (s.Party.Left, s.Party.Top),
        OverlayPart.Party => (s.Party.ShowOthersBySlot[0].Left, s.Party.ShowOthersBySlot[0].Top),
        _ => (s.Overlay.Left, s.Overlay.Top),
    };

    public static void Set(Settings s, OverlayPart part, double left, double top)
    {
        switch (part)
        {
            case OverlayPart.Buffs when s.Buffs.Detached: (s.Buffs.Left, s.Buffs.Top) = (left, top); break;
            case OverlayPart.Gauge when s.Apart: (s.Gauge.Left, s.Gauge.Top) = (left, top); break;
            case OverlayPart.Debuffs: (s.Debuffs.Left, s.Debuffs.Top) = (left, top); break;
            case OverlayPart.Judgment: (s.Judgment.Left, s.Judgment.Top) = (left, top); break;
            case OverlayPart.Amsu: (s.Amsu.Left, s.Amsu.Top) = (left, top); break;
            case OverlayPart.BarCount: (s.Raids.Left, s.Raids.Top) = (left, top); break;
            case OverlayPart.Meter: (s.Meter.Left, s.Meter.Top) = (left, top); break;
            case OverlayPart.Party when s.Party.Together: (s.Party.Left, s.Party.Top) = (left, top); break;
            case OverlayPart.Party: (s.Party.ShowOthersBySlot[0].Left, s.Party.ShowOthersBySlot[0].Top) = (left, top); break;
            default: (s.Overlay.Left, s.Overlay.Top) = (left, top); break;
        }
    }
}
