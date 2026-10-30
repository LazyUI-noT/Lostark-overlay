using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>테스트 모드: the 버프 크기 a video was recorded at (its game settings, not the user's). Each game size places the buff
/// row's boxes differently; the size whose boxes hold the most party buffs (BuffReader's own checks), then the best-matching
/// ones, is the one drawn. Needs a buff on screen: none gives null, and the caller tries a later frame. Pure.</summary>
public static class BuffSizeFinder
{
    /// <summary>The size found, how many boxes held a buff at it and their summed structure score.</summary>
    public sealed record Found(int Size, int Matched, double Score);

    /// <summary>The screen rect holding every size's buff row (one capture serves them all).</summary>
    public static PixelRect Area(Viewport vp, Fit fit, int count = Geometry.BuffBoxCount)
    {
        var boxes = Settings.BuffSizes.SelectMany(s => Geometry.BuffBoxes(vp, fit, s, count)).Select(b => b.Icon).ToList();
        int x = boxes.Min(b => b.X), y = boxes.Min(b => b.Y);
        return new(x, y, boxes.Max(b => b.Right) - x, boxes.Max(b => b.Bottom) - y);
    }

    /// <summary>The size whose boxes in <paramref name="area"/> (a capture of <paramref name="areaRect"/>) match the most buffs;
    /// null when no size matches any. Boxes outside the capture are skipped.</summary>
    public static Found? Find(Image area, PixelRect areaRect, Viewport vp, Fit fit, int count = Geometry.BuffBoxCount)
    {
        Found? best = null;
        foreach (int size in Settings.BuffSizes)
        {
            int matched = 0;
            double score = 0;
            foreach (var box in Geometry.BuffBoxes(vp, fit, size, count))
            {
                var icon = box.Icon with { X = box.Icon.X - areaRect.X, Y = box.Icon.Y - areaRect.Y };
                if (icon.X < 0 || icon.Y < 0 || icon.Right > area.Width || icon.Bottom > area.Height) continue;
                var scores = BuffReader.Scores(area, icon);
                if (BuffReader.Best(scores) is not { } k) continue;
                matched++;
                score += scores.Structure[k];
            }
            if (matched > 0 && (best is null || matched > best.Matched || matched == best.Matched && score > best.Score))
                best = new(size, matched, score);
        }
        return best;
    }
}
