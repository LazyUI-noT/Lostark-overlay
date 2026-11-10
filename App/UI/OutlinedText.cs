using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GaugeOverlay.App.UI;

/// <summary>A line of bold text with a black outline like the game's, sized to its height: what <see cref="OverlayTextWindow"/>
/// draws over the game and what 설정 → 레이드's examples (<see cref="BossBarExample"/>) draw over a boss-bar picture.</summary>
sealed class OutlinedText(Brush ink, bool centred, double textShare) : FrameworkElement
{
    static readonly Typeface Face = new(new FontFamily("Malgun Gothic"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    const double OutlineShare = 0.2;   // the outline pen × the font size (half of it shows round the glyphs)

    /// <summary>그림자 레이드 기믹 체력's ink: cyan, apart from the game's white, yellow and red number and the orange fill.</summary>
    public static readonly Color InBarInk = Color.FromRgb(120, 230, 255);

    string _text = "";
    public string Text { get => _text; set { if (value == _text) return; _text = value; InvalidateVisual(); } }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Max(8, ActualHeight * textShare);
        var geo = new FormattedText(_text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, ink, 1.0)
            .BuildGeometry(new Point(0, 0));
        var b = geo.Bounds;
        if (b.IsEmpty) return;
        double pen = size * OutlineShare, x = centred ? (ActualWidth - b.Width) / 2 - b.X : pen / 2 - b.X;
        dc.PushTransform(new TranslateTransform(x, (ActualHeight - b.Height) / 2 - b.Y));
        dc.DrawGeometry(null, new Pen(Brushes.Black, pen) { LineJoin = PenLineJoin.Round }, geo);
        dc.DrawGeometry(ink, null, geo);
        dc.Pop();
    }
}
