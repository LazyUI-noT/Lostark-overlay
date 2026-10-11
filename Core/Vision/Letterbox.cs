using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>Finds the game's drawn area inside its window: the client area minus pure-black letterbox bars (forced 21:9,
/// PLAN §1a), so the user doesn't have to set a flag.</summary>
public static class Letterbox
{
    /// <summary>The thin column to capture for <see cref="Viewport"/>: full client height, 5% in from the left edge
    /// (the centre can carry notices at the very top, forced_219.png row 0).</summary>
    public static PixelRect Probe(PixelRect client) => new(client.X + client.Width / 20, client.Y, 2, client.Height);

    /// <summary>Client area minus black bars, from a capture of <see cref="Probe"/>. The bars are symmetric, so the
    /// thinner of the top and bottom black runs is used for both; runs under 1% of the height are ignored.</summary>
    /// <param name="noise">Highest channel value still counted as black. The game draws 0; a streamed or recorded
    /// video (테스트 모드) leaves its bars at 1–3 after compression (Discord stream, 2026-10-11).</param>
    public static Viewport Viewport(PixelRect client, Image column, int noise = 0)
    {
        int top = 0, bottom = 0;
        while (top < column.Height && IsBlack(column, top, noise)) top++;
        while (bottom < column.Height && IsBlack(column, column.Height - 1 - bottom, noise)) bottom++;
        int bar = Math.Min(top, bottom);
        if (bar < client.Height / 100 || bar * 2 >= client.Height) bar = 0;
        // A video blurs the picture a few rows into its bars (816 high for 810): near the game's forced 21:9 area, take it exactly.
        int wide = (int)Math.Round(client.Width * ForcedWide);
        if (noise > 0 && bar > 0 && Math.Abs(client.Height - 2 * bar - wide) <= client.Height / 50) bar = (client.Height - wide) / 2;
        return new(client.X, client.Y + bar, client.Width, client.Height - 2 * bar);
    }

    /// <summary>Bar noise allowed for a video in 테스트 모드.</summary>
    public const int VideoNoise = 8;

    /// <summary>Height over width of the game's forced 21:9 area (forced_219.png: 1920 × 810).</summary>
    private const double ForcedWide = 27.0 / 64;

    private static bool IsBlack(Image img, int y, int noise)
    {
        var row = img.Row(y);
        for (int i = 0; i < row.Length; i += 4)
            if (Math.Max(row[i], Math.Max(row[i + 1], row[i + 2])) > noise) return false;
        return true;
    }
}
