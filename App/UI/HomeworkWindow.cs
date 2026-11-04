using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>숙제 tags over the 다른 캐릭터 접속 grid (homework.plan.md, goal.png): under each visible card with picks, one small
/// tag per raid ("세하"), in its difficulty's colour, crossed out and dimmed once cleared this week, ↓ / ↑ when cleared below /
/// above the pick. Covers the game area, topmost and click-through; shown only while the grid is read.</summary>
sealed class HomeworkWindow : Window
{
    static readonly Typeface Face = new(new FontFamily("Malgun Gothic"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    static readonly Brush Back = Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0x14, 0x14, 0x18)));
    static readonly Dictionary<HomeworkColor, Brush> Ink = new()
    {
        [HomeworkColor.White] = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2))),
        [HomeworkColor.Orange] = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0x3C))),
        [HomeworkColor.Purple] = Frozen(new SolidColorBrush(Color.FromRgb(0xC7, 0x7D, 0xFF))),
    };
    const double TagHeight = 17, TextSize = 12, Gap = 3, Pad = 4, DoneOpacity = 0.55;   // at 1080p

    readonly Tags _tags = new();
    PixelRect _at;

    public HomeworkWindow()
    {
        (WindowStyle, AllowsTransparency, Background, Topmost, ShowInTaskbar, ShowActivated, ResizeMode) =
            (WindowStyle.None, true, Brushes.Transparent, true, false, false, ResizeMode.NoResize);
        (WindowStartupLocation, Left, Top, Width, Height) = (WindowStartupLocation.Manual, -32000, -32000, 1, 1);   // off screen until placed
        Content = _tags;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowStyles.Set(hwnd, ExStyle.ToolWindow | ExStyle.NoActivate | ExStyle.ClickThrough, true);
        WindowStyles.SetExcludeFromCapture(hwnd, true);   // the menu is read from the screen: its own tags would look like a change (Desktop Duplication sees them)
    }

    /// <summary>Shows the tags under <paramref name="cards"/> (screen pixels, the card from its level line down to its bottom)
    /// over the game area <paramref name="vp"/>; hides when there are none or the overlays are hidden.</summary>
    public void Update(Viewport vp, IReadOnlyList<(PixelRect Card, IReadOnlyList<HomeworkTag> Tags)> cards, bool allowed)
    {
        if (!allowed || cards.Count == 0) { if (IsVisible) Hide(); return; }
        var area = new PixelRect(vp.X, vp.Y, vp.Width, vp.Height);
        _tags.Set([.. cards.Select(c => (c.Card with { X = c.Card.X - vp.X, Y = c.Card.Y - vp.Y }, c.Tags))], vp.Height / 1080.0);
        if (!IsVisible) Show();
        if (area != _at) WindowStyles.PlaceInPixels(new WindowInteropHelper(this).Handle, _at = area);
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Draws in the game area's pixels, scaled to the window's units.</summary>
    internal sealed class Tags : FrameworkElement
    {
        IReadOnlyList<(PixelRect Card, IReadOnlyList<HomeworkTag> Tags)> _cards = [];
        double _scale = 1;

        public void Set(IReadOnlyList<(PixelRect Card, IReadOnlyList<HomeworkTag> Tags)> cards, double scale)
        {
            (_cards, _scale) = (cards, scale);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX, k = _scale / dpi;   // reference px → window units
            foreach (var (card, tags) in _cards)
            {
                double x = card.X / dpi, y = (card.Y + card.Height) / dpi;
                foreach (var tag in tags)
                {
                    string text = tag.Text + tag.Arrow switch { < 0 => "↓", > 0 => "↑", _ => "" };
                    var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, TextSize * k, Ink[tag.Color], dpi);
                    double w = ft.Width + 2 * Pad * k, h = TagHeight * k;
                    dc.PushOpacity(tag.Done ? DoneOpacity : 1);
                    dc.DrawRoundedRectangle(Back, null, new Rect(x, y, w, h), 3 * k, 3 * k);
                    dc.DrawText(ft, new Point(x + Pad * k, y + (h - ft.Height) / 2));
                    if (tag.Done) dc.DrawLine(new Pen(Ink[tag.Color], 1.5 * k), new Point(x + 2 * k, y + h / 2), new Point(x + w - 2 * k, y + h / 2));
                    dc.Pop();
                    x += w + Gap * k;
                }
            }
        }
    }
}
