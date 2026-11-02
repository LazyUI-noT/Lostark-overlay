using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>전분 위치 찾기 (spec § C): finds the 전분 panel anywhere on the game screen. Its first cell is a green block of a known
/// size (112×23 at 1080p, times the panel's scale: it follows the screen's height, not the HUD); from a green block's top-left
/// the panel's spot follows, for the first row or the second (a saved record compared above mine). A spot counts only when
/// JeonbunReader reads it, so before a fight starts (no green row) nothing is found. Pure.</summary>
public static class JeonbunFinder
{
    const double CellW = 112, CellH = 23, CellLeft = 2, CellTop = 63, RowPitch = 26;   // the first cell in the 1080p crop
    const double EdgeW = 8;   // the green strip checked at the cell's left

    /// <summary>The panel's crop (470 × up to 114, times <paramref name="scale"/>) in <paramref name="screen"/>'s pixels, or null.</summary>
    public static PixelRect? Find(Image screen, double scale)
    {
        int step = Math.Max(2, (int)(CellH * scale / 3));
        var tried = new HashSet<(int, int)>();
        for (int y = 0; y < screen.Height; y += step)
            for (int x = 0; x < screen.Width; x += step)
            {
                if (!Green(screen, x, y)) continue;
                var (left, top) = Corner(screen, x, y);
                if (!tried.Add((left, top)) || !Block(screen, left, top, scale)) continue;
                for (int row = 0; row < 2; row++)
                {
                    int px = (int)Math.Round(left - CellLeft * scale), py = (int)Math.Round(top - (CellTop + row * RowPitch) * scale);
                    if (Spot(screen, px, py, scale) is not { } r) continue;
                    var crop = ImageOps.Crop(screen, r);
                    if (JeonbunReader.MyRow(crop) == row && JeonbunReader.Read(crop) is not null) return r;
                }
            }
        return null;
    }

    /// <summary>Whether the panel is in <paramref name="crop"/> (its saved spot): the green first cell in the first row, or in
    /// the second (before the first hit the "이전 기록을 선택하여…" line takes the first row and the row shows dashes). No digits
    /// are read: true from timer 0, where <see cref="JeonbunReader.Read"/> has nothing yet.</summary>
    public static bool Present(Image crop, double scale)
    {
        for (int row = 0; row < 2; row++)
        {
            int x = (int)Math.Round((CellLeft + EdgeW / 2) * scale), y = (int)Math.Round((CellTop + row * RowPitch + CellH / 2) * scale);
            if (x >= crop.Width || y >= crop.Height || !Green(crop, x, y)) continue;
            var (left, top) = Corner(crop, x, y);
            if (Block(crop, left, top, scale)) return true;
        }
        return false;
    }

    /// <summary>The panel's crop at (x, y), as tall as fits (one row needs 88, two 114), or null when it runs off the screen.</summary>
    static PixelRect? Spot(Image screen, int x, int y, double scale)
    {
        int w = (int)Math.Round(JeonbunReader.RefW * scale), h = Math.Min((int)Math.Round(JeonbunReader.RefH * scale), screen.Height - y);
        return x < 0 || y < 0 || x + w > screen.Width || h < 88 * scale ? null : new PixelRect(x, y, w, h);
    }

    // The cell's fill: green well above red and above blue (the dealer's 피해량 and the support's 조력 피해량 alike).
    static bool Green(Image img, int x, int y)
    {
        var (b, g, r) = img.Pixel(x, y);
        return g > r + 40 && g > b + 5 && g > 80 && Math.Min(b, Math.Min(g, r)) < 140;
    }

    /// <summary>The top-left of the green block holding (x, y): up while green, then left along that top row.</summary>
    static (int Left, int Top) Corner(Image img, int x, int y)
    {
        int top = y;
        while (top > 0 && Green(img, x, top - 1)) top--;
        int left = x;
        while (left > 0 && Green(img, left - 1, top)) left--;
        return (left, top);
    }

    /// <summary>The cell's left edge is green its full height. The green is a bar of my damage against the top row's, so it
    /// fills only part of the cell when a saved record above dealt more; only a strip at its left is sure to be green.</summary>
    static bool Block(Image img, int left, int top, double scale)
    {
        int w = Math.Max(4, (int)(EdgeW * scale)), h = (int)(CellH * scale);
        if (left + (int)(CellW * scale) > img.Width || top + h > img.Height) return false;
        int green = 0, n = 0;
        for (int y = top + 2; y < top + h - 2; y += 2)
            for (int x = left; x < left + w; x++, n++)
                if (Green(img, x, y)) green++;
        return n > 0 && green > 0.6 * n;
    }
}
