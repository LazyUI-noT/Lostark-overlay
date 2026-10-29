using System.Windows.Media;
using GaugeOverlay.Core.Domain;
using Forms = System.Windows.Forms;

namespace GaugeOverlay.App.UI;

/// <summary>The settings' "#RRGGBB" colours in WPF (read by <see cref="HexColor"/>), and the Windows colour dialog.</summary>
static class UiColor
{
    /// <summary>The colour of <paramref name="hex"/>, or null when it isn't a colour code.</summary>
    public static Color? TryParse(string? hex) => HexColor.TryParse(hex, out var c) ? Color.FromRgb(c.R, c.G, c.B) : null;

    /// <summary>The colour of <paramref name="hex"/>; a malformed one (a hand edit, a party member's) gives <paramref name="fallback"/>.</summary>
    public static Color Parse(string? hex, Color fallback) => TryParse(hex) ?? fallback;

    public static string ToHex(Color c) => HexColor.Format(new Rgb(c.R, c.G, c.B));

    /// <summary>The Windows colour dialog opened at <paramref name="current"/>; the picked colour as "#RRGGBB", or null.</summary>
    public static string? Pick(string? current)
    {
        var c = Parse(current, Colors.Gray);
        using var d = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) };
        return d.ShowDialog() == Forms.DialogResult.OK ? ToHex(Color.FromRgb(d.Color.R, d.Color.G, d.Color.B)) : null;
    }
}
