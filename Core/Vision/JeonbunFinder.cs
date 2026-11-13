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
        foreach (var (left, top) in Blocks(screen, scale))
            if (Block(screen, left, top, scale) && At(screen, left, top, scale, Reads) is { } r) return r.Rect;
        return null;
    }

    const double MinScale = 0.5, MaxScale = 2.5;   // 540p to past 4K's 2.0: a zoomed video's panel
    // The green run down a cell's left edge at 1080p: about 25 px for one row, about 51 when the row below is green too
    // (no dark line between them), measured on jeonbun_2/15/19/22.
    const double OneRunH = 25, TwoRunH = 51;
    // The run is a pixel or two off the cell's height, mostly long (26 on a 1080p-sized panel in the 2560×1440 Bossbar_bug
    // clips: 1.04, where the panel at the screen's right edge fits only up to 1.00), so each guess is tried as is and smaller.
    static readonly double[] GuessSteps = [1, 0.97, 0.94, 1.03];
    const double Slack = 0.10, SlackStep = 0.01;   // the run's soft ends (blurred by the video's scaling): ±10 % around a guess, in 1 % steps

    /// <summary>테스트 모드 (user, 2026-11-12): a cropped or zoomed video shows the panel at a size the screen's height doesn't
    /// give. Each green block is tried at the size its height gives (one row, or two touching), its spot as found (the frame
    /// check allows a few pixels), and taken when the panel's frame shows there (<see cref="JeonbunReader.Shows"/>: a stream's
    /// blur can lose a digit). Only then is its size measured, within ±10 %, which keeps a busy frame's green effects cheap:
    /// the largest that reads in full (sizes up to about 4 % under the panel's read too, none over: serka-g1-90s,
    /// belgardin-party, also zoomed ×1.33), else the middle of the sizes whose frame shows (discord_전분_sample.png: 0.96–1.05
    /// around 1.0; 강화 reads measure nothing, they took a stray 1.04 there). A panel at the screen's edge loses the sizes that
    /// don't fit, so its middle can be a few % small (the 1440to1080 serka copy: 0.97, 455 px for 469); it still reads.</summary>
    public static PixelRect? FindAnySize(Image screen)
    {
        PixelRect? candidate = null;
        foreach (var (left, top) in Blocks(screen, MinScale))
        {
            int h = GreenRun(screen, Math.Min(left + 1, screen.Width - 1), top);
            foreach (double guess in new[] { h / OneRunH, h / TwoRunH }.SelectMany(g => GuessSteps.Select(k => g * k)))
            {
                if (guess is < MinScale or > MaxScale || !Block(screen, left, top, guess)) continue;
                if (At(screen, left, top, guess, JeonbunReader.Shows, nudge: false) is not { } hit) continue;
                int n = (int)Math.Round(Slack / SlackStep);
                var sizes = Enumerable.Range(-n, 2 * n + 1).Select(i => guess * (1 + i * SlackStep)).Where(s => s is >= MinScale and <= MaxScale).ToList();
                if (sizes.OrderDescending().Select(s => At(screen, left, top, s, Reads, hit.Row)).FirstOrDefault(r => r is not null) is { } read)
                    return read.Rect;
                // Sizes running past the screen's right edge are looked at too (padded black), so a panel at the edge isn't
                // measured small; the one returned must fit.
                var shown = sizes.Select(s => At(screen, left, top, s, JeonbunReader.Shows, hit.Row, nudge: false, pastRight: true))
                    .OfType<(PixelRect Rect, int Row)>().Select(r => r.Rect).ToList();
                // Only the frame shows: kept, but a panel further on that reads in full wins over it.
                candidate ??= shown.Count > 0 && shown[shown.Count / 2] is var mid && mid.Right <= screen.Width ? mid
                    : shown.LastOrDefault(r => r.Right <= screen.Width, hit.Rect);
                break;
            }
        }
        return candidate;
    }

    static bool Reads(Image crop, int row) => JeonbunReader.MyRow(crop) == row && JeonbunReader.Read(crop) is not null;

    // The cell's top edge is blurred in video: in serka-g1-90s it starts 1 px above where the text puts it, and the crop 1 px
    // too high doesn't read. So a spot is tried as found, then 1 and 2 px down and up.
    static readonly int[] Nudges = [1, -1, 2, -2];

    /// <summary>The top-left corners of the screen's green blocks, each once, walking a grid fine enough for
    /// <paramref name="smallest"/>.</summary>
    static IEnumerable<(int Left, int Top)> Blocks(Image screen, double smallest)
    {
        int step = Math.Max(2, (int)(CellH * smallest / 3));
        var tried = new HashSet<(int, int)>();
        for (int y = 0; y < screen.Height; y += step)
            for (int x = 0; x < screen.Width; x += step)
                if (Green(screen, x, y) && Corner(screen, x, y) is var c && tried.Add(c)) yield return c;
    }

    /// <summary>The panel whose first cell (my row: the first, or the second) has its top-left at (left, top) at
    /// <paramref name="scale"/>, when <paramref name="isPanel"/> takes its crop; <paramref name="onlyRow"/> tries one row.</summary>
    static (PixelRect Rect, int Row)? At(Image screen, int left, int top, double scale, Func<Image, int, bool> isPanel, int? onlyRow = null,
        bool nudge = true, bool pastRight = false)
    {
        for (int row = onlyRow ?? 0; row <= (onlyRow ?? 1); row++)
        {
            int px = (int)Math.Round(left - CellLeft * scale), py = (int)Math.Round(top - (CellTop + row * RowPitch) * scale);
            // A crop whose green row isn't this one isn't the panel at any nudge: one check spares the reads.
            if (Spot(screen, px, py, scale, pastRight) is not { } at || Cut(screen, at) is var crop && JeonbunReader.MyRow(crop) != row) continue;
            if (isPanel(crop, row)) return (at, row);
            // Nudged only when the frame shows here: green effects that aren't the panel cost one more check, not four.
            if (!nudge || !JeonbunReader.Shows(crop, row)) continue;
            foreach (int dy in Nudges)
                if (Spot(screen, px, py + dy, scale, pastRight) is { } r && isPanel(Cut(screen, r), row)) return (r, row);
        }
        return null;
    }

    /// <summary>How many pixels down from (x, y) stay green.</summary>
    static int GreenRun(Image img, int x, int y)
    {
        int end = y;
        while (end < img.Height && Green(img, x, end)) end++;
        return end - y;
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
    static PixelRect? Spot(Image screen, int x, int y, double scale, bool pastRight = false)
    {
        int w = (int)Math.Round(JeonbunReader.RefW * scale), h = Math.Min((int)Math.Round(JeonbunReader.RefH * scale), screen.Height - y);
        return x < 0 || y < 0 || !pastRight && x + w > screen.Width || h < 88 * scale ? null : new PixelRect(x, y, w, h);
    }

    /// <summary><paramref name="r"/> of <paramref name="screen"/>, black past its right edge.</summary>
    static Image Cut(Image screen, PixelRect r)
    {
        if (r.Right <= screen.Width) return ImageOps.Crop(screen, r);
        var dst = new Image(r.Width, r.Height);
        int w = Math.Max(0, screen.Width - r.X);
        for (int y = 0; y < r.Height; y++) screen.Row(r.Y + y).Slice(r.X * 4, w * 4).CopyTo(dst.Bgra.AsSpan(y * dst.Stride));
        return dst;
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
