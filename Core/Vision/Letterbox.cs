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
    public static Viewport Viewport(PixelRect client, Image column)
    {
        int top = 0, bottom = 0;
        while (top < column.Height && IsBlack(column, top)) top++;
        while (bottom < column.Height && IsBlack(column, column.Height - 1 - bottom)) bottom++;
        int bar = Math.Min(top, bottom);
        if (bar < client.Height / 100 || bar * 2 >= client.Height) bar = 0;
        return new(client.X, client.Y + bar, client.Width, client.Height - 2 * bar);
    }

    private static bool IsBlack(Image img, int y)
    {
        var row = img.Row(y);
        for (int i = 0; i < row.Length; i += 4)
            if ((row[i] | row[i + 1] | row[i + 2]) != 0) return false;
        return true;
    }
}
