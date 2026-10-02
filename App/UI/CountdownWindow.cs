using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GaugeOverlay.App.Platform;

namespace GaugeOverlay.App.UI;

/// <summary>게임 화면 찍기's 3, 2, 1: big white digits with a black outline in the middle of the game's monitor. Topmost,
/// click-through and kept out of screen capture; raised again every second so it stays in front if another window
/// comes up. No settings, no edit mode.</summary>
sealed class CountdownWindow : Window
{
    const double DigitSize = 220, Outline = 10;
    static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    static readonly Pen OutlinePen = new(Brushes.Black, Outline) { LineJoin = PenLineJoin.Round };

    readonly Digit _digit = new();
    readonly bool _capturable;

    CountdownWindow(Rect monitor, bool capturable)
    {
        _capturable = capturable;
        (WindowStyle, AllowsTransparency, Background, Topmost, ShowInTaskbar, ShowActivated, ResizeMode) =
            (WindowStyle.None, true, Brushes.Transparent, true, false, false, ResizeMode.NoResize);
        (Width, Height) = (DigitSize * 1.5, DigitSize * 1.5);
        (Left, Top) = (monitor.Left + (monitor.Width - Width) / 2, monitor.Top + (monitor.Height - Height) / 2);
        Content = _digit;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowStyles.Set(hwnd, ExStyle.ToolWindow | ExStyle.NoActivate | ExStyle.ClickThrough, true);
        WindowStyles.SetExcludeFromCapture(hwnd, !_capturable);
    }

    /// <summary>Counts <paramref name="from"/> … 1 a second apart over <paramref name="monitor"/> (DIP), then closes.</summary>
    /// <param name="capturable">--demo --countdown only: in screenshots, so it can be checked.</param>
    public static async Task Run(Rect monitor, int from = 3, bool capturable = false)
    {
        var w = new CountdownWindow(monitor, capturable);
        w.Show();
        try
        {
            for (int n = from; n >= 1; n--)
            {
                w._digit.Text = n.ToString(CultureInfo.InvariantCulture);
                w.Topmost = false;   // back in front of whatever came up
                w.Topmost = true;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
        finally { w.Close(); }
    }

    sealed class Digit : FrameworkElement
    {
        string _text = "";
        public string Text { set { _text = value; InvalidateVisual(); } }

        protected override void OnRender(DrawingContext dc)
        {
            var geo = new FormattedText(_text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, DigitSize, Brushes.White, 1.0)
                .BuildGeometry(new Point(0, 0));
            var b = geo.Bounds;
            if (b.IsEmpty) return;
            dc.PushTransform(new TranslateTransform((ActualWidth - b.Width) / 2 - b.X, (ActualHeight - b.Height) / 2 - b.Y));
            dc.DrawGeometry(null, OutlinePen, geo);   // the outline under the fill: half of it shows outside the glyph
            dc.DrawGeometry(Brushes.White, null, geo);
            dc.Pop();
        }
    }
}
