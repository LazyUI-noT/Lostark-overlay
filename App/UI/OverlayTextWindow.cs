using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>A line of text over the game at a place in screen pixels, with a black outline like the game's text; topmost and
/// click-through, no settings of its own. Three of them (user, 2026-11-04 / 05): 그림자 레이드 기믹 체력 ("X 87  4,350억", cyan, centred
/// in the gimmick's fill bar), 대난투's damage and the boss's HP easy to read ("[1조 5,392억]", white, from the left of a fixed spot).</summary>
sealed class OverlayTextWindow : Window
{
    /// <summary>그림자 레이드 기믹 체력: <see cref="OutlinedText.InBarInk"/>, a little smaller than the bar so a gap shows ("억" and
    /// the outline still inside the window).</summary>
    public static OverlayTextWindow InBar() => new(OutlinedText.InBarInk, centred: true, textShare: 0.85);
    /// <summary>An easy-read label: white, the font about the game's name and number text.</summary>
    public static OverlayTextWindow Label(double textShare) => new(Colors.White, centred: false, textShare);

    readonly OutlinedText _line;
    PixelRect _at;

    OverlayTextWindow(Color ink, bool centred, double textShare)
    {
        (WindowStyle, AllowsTransparency, Background, Topmost, ShowInTaskbar, ShowActivated, ResizeMode) =
            (WindowStyle.None, true, Brushes.Transparent, true, false, false, ResizeMode.NoResize);
        (WindowStartupLocation, Left, Top, Width, Height) = (WindowStartupLocation.Manual, -32000, -32000, 1, 1);   // off screen until placed
        var brush = new SolidColorBrush(ink);
        brush.Freeze();
        Content = _line = new OutlinedText(brush, centred, textShare);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyles.Set(new WindowInteropHelper(this).Handle, ExStyle.ToolWindow | ExStyle.NoActivate | ExStyle.ClickThrough, true);
    }

    /// <summary>Shows <paramref name="text"/> at <paramref name="at"/> (screen pixels), or hides when null or the overlays are hidden.</summary>
    public void Update((string Text, PixelRect At)? show, bool allowed)
    {
        if (show is not var (text, at) || !allowed) { if (IsVisible) Hide(); return; }
        _line.Text = text;
        if (!IsVisible) Show();
        // In screen pixels (the game's monitor may have another DPI than the window's); moved only when the place moved.
        if (at != _at) WindowStyles.PlaceInPixels(new WindowInteropHelper(this).Handle, _at = at);
    }
}
