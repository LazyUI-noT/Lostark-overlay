using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>그림자 레이드 기믹 체력 (user, 2026-11-04): "X 87  4,350억" over the game where the boss bar was, while the gimmick's fill
/// bar is up (ShadowView). White with a black outline like the game's text, the bar's height; topmost and click-through, no
/// settings but the per-raid switch in 설정 → 레이드.</summary>
sealed class ShadowHpWindow : Window
{
    static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    const double TextShare = 0.8, OutlineShare = 0.12;   // × the bar's height

    readonly Line _line = new();
    PixelRect _at;

    public ShadowHpWindow()
    {
        (WindowStyle, AllowsTransparency, Background, Topmost, ShowInTaskbar, ShowActivated, ResizeMode) =
            (WindowStyle.None, true, Brushes.Transparent, true, false, false, ResizeMode.NoResize);
        (WindowStartupLocation, Left, Top, Width, Height) = (WindowStartupLocation.Manual, -32000, -32000, 1, 1);   // off screen until placed
        Content = _line;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyles.Set(new WindowInteropHelper(this).Handle, ExStyle.ToolWindow | ExStyle.NoActivate | ExStyle.ClickThrough, true);
    }

    /// <summary>Shows <paramref name="v"/> at the bar's place (screen pixels), or hides when null or the overlays are hidden.</summary>
    public void Update(ShadowView? v, bool allowed)
    {
        if (v is null || !allowed) { if (IsVisible) Hide(); return; }
        _line.Text = Text(v);
        if (!IsVisible) Show();
        // In screen pixels (the game's monitor may have another DPI than the window's); moved only when the bar moved.
        if (v.Bar != _at) WindowStyles.PlaceInPixels(new WindowInteropHelper(this).Handle, _at = v.Bar);
    }

    /// <summary>"X 87  4,350억"; the HP alone while no "X n" was read.</summary>
    internal static string Text(ShadowView v)
    {
        string hp = $"{v.Remaining / 100_000_000.0:N0}억";
        return v.Lines is { } lines ? $"X {lines.ToString(CultureInfo.InvariantCulture)}  {hp}" : hp;
    }

    sealed class Line : FrameworkElement
    {
        string _text = "";
        public string Text { set { if (value == _text) return; _text = value; InvalidateVisual(); } }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Max(8, ActualHeight * TextShare);
            var geo = new FormattedText(_text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, Brushes.White, 1.0)
                .BuildGeometry(new Point(0, 0));
            var b = geo.Bounds;
            if (b.IsEmpty) return;
            dc.PushTransform(new TranslateTransform((ActualWidth - b.Width) / 2 - b.X, (ActualHeight - b.Height) / 2 - b.Y));
            dc.DrawGeometry(null, new Pen(Brushes.Black, size * OutlineShare) { LineJoin = PenLineJoin.Round }, geo);
            dc.DrawGeometry(Brushes.White, null, geo);
            dc.Pop();
        }
    }
}
