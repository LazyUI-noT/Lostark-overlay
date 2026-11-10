using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The adjust layer's green for a still picture (a stored moment, a paused Live, 화면 찍기): one read of a part at a fit
/// through the same readers the engine uses. The picture is only looked at, never kept as a reference (detection.md).</summary>
public static class PictureReads
{
    /// <summary>Whether <paramref name="part"/> reads at <paramref name="fit"/> in <paramref name="picture"/> (taken at screen rect
    /// <paramref name="at"/>): true it read; false nothing seen (drawn plain, not wrong); null the picture doesn't hold the part's area.</summary>
    /// <param name="panel">The 전분 box, which no fit places.</param>
    public static bool? Of(AdjustPart part, Image picture, PixelRect at, Viewport vp, Fit fit, int buffSize, int buffCount, int bossSize, PixelRect? panel = null) => part switch
    {
        AdjustPart.스킬창 => Holds(at, FitSolver.CueArea(vp, fit)) ? FitSolver.ReadsWell(FitSolver.Score(picture, at, vp, fit)) : null,
        AdjustPart.버프 => Row(picture, at, Geometry.BuffArea(vp, fit, buffSize, buffCount), Geometry.BuffBoxes(vp, fit, buffSize, buffCount), (row, icon) => BuffReader.Classify(row, icon) is not null),
        AdjustPart.디버프 => Row(picture, at, Geometry.DebuffArea(vp, fit, buffSize), Geometry.DebuffBoxes(vp, fit, buffSize), (row, icon) => DebuffReader.Classify(row, icon) is not null),
        AdjustPart.전분 => panel is { } r && Holds(at, r) ? Panel(picture.Crop(r with { X = r.X - at.X, Y = r.Y - at.Y })) : null,
        _ => Boss(picture, at, vp, fit, bossSize),
    };

    /// <summary>Whether <paramref name="crop"/>, the 전분 box's pixels, holds the panel: it reads, or its green cell is there before the
    /// first hit. The scale comes from the box's width, as the reader's.</summary>
    public static bool Panel(Image crop) => JeonbunReader.Read(crop) is not null || JeonbunFinder.Present(crop, crop.Width / JeonbunReader.RefW);

    static bool Holds(PixelRect at, PixelRect area) => area.Width > 0 && area.Height > 0 && at.Intersect(area) == area;

    /// <summary>A row of icon boxes: any box with a known icon.</summary>
    static bool? Row(Image picture, PixelRect at, PixelRect area, Geometry.BuffBox[] boxes, Func<Image, PixelRect, bool> known)
    {
        if (!Holds(at, area)) return null;
        var row = picture.Crop(area with { X = area.X - at.X, Y = area.Y - at.Y });
        return boxes.Any(b => known(row, b.Icon with { X = b.Icon.X - area.X, Y = b.Icon.Y - area.Y }));
    }

    static bool? Boss(Image picture, PixelRect at, Viewport vp, Fit fit, int bossSize)
    {
        var area = Geometry.BossRow(vp, fit, bossSize);
        if (!Holds(at, area)) return null;
        var (red, blue) = DebuffReader.BossCounts(picture.Crop(area with { X = area.X - at.X, Y = area.Y - at.Y }), Geometry.BossScale(vp, fit, bossSize));
        return red is not null || blue is not null;
    }
}
