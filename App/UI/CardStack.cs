using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GaugeOverlay.App.UI;

/// <summary>A settings section laid out as Windows 11's setting cards: each run of rows sits in one rounded card, a hairline
/// between rows; headings (<see cref="SetBreak"/>) and nested card stacks stand between the cards, at full width.
/// Children are added as to a StackPanel, so the Fill methods build sections as before.</summary>
public sealed class CardStack : StackPanel
{
    const double PadX = 16, PadY = 8, Gap = 8, Radius = 6;

    public static readonly DependencyProperty CardBrushProperty = DependencyProperty.Register(nameof(CardBrush), typeof(Brush), typeof(CardStack),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(CardStack),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Marks a child that stands between the cards (a heading) rather than in one.</summary>
    public static readonly DependencyProperty BreakProperty = DependencyProperty.RegisterAttached("Break", typeof(bool), typeof(CardStack),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static void SetBreak(UIElement e, bool value) => e.SetValue(BreakProperty, value);

    public Brush? CardBrush { get => (Brush?)GetValue(CardBrushProperty); set => SetValue(CardBrushProperty, value); }
    public Brush? StrokeBrush { get => (Brush?)GetValue(StrokeBrushProperty); set => SetValue(StrokeBrushProperty, value); }

    // What the last arrange laid out, for OnRender: each card's rectangle and the lines between its rows.
    List<Rect> _cards = [];
    List<double> _lines = [];

    public CardStack()
    {
        SetResourceReference(CardBrushProperty, Themes.CardBrushKey);
        SetResourceReference(StrokeBrushProperty, Themes.StrokeBrushKey);
    }

    static bool InCard(UIElement e) => !(bool)e.GetValue(BreakProperty) && e is not CardStack;

    protected override Size MeasureOverride(Size available)
    {
        double width = available.Width, y = 0;
        bool inCard = false;
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) continue;
            if (InCard(c))
            {
                c.Measure(new Size(Math.Max(0, width - 2 * PadX), double.PositiveInfinity));
                y += c.DesiredSize.Height + 2 * PadY;
                inCard = true;
                continue;
            }
            if (inCard) (y, inCard) = (y + Gap, false);
            c.Measure(new Size(width, double.PositiveInfinity));
            y += c.DesiredSize.Height;
        }
        if (inCard) y += Gap;
        double w = double.IsInfinity(width) ? InternalChildren.Cast<UIElement>().Select(c => c.DesiredSize.Width + 2 * PadX).DefaultIfEmpty(0).Max() : width;
        return new Size(w, y);
    }

    protected override Size ArrangeOverride(Size final)
    {
        List<Rect> cards = [];
        List<double> lines = [];
        double y = 0, top = 0;
        bool inCard = false;
        void Close() { cards.Add(new Rect(0, top, final.Width, y - top)); y += Gap; inCard = false; }
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) continue;
            if (InCard(c))
            {
                if (!inCard) (top, inCard) = (y, true);
                else lines.Add(y);
                c.Arrange(new Rect(PadX, y + PadY, Math.Max(0, final.Width - 2 * PadX), c.DesiredSize.Height));
                y += c.DesiredSize.Height + 2 * PadY;
                continue;
            }
            if (inCard) Close();
            c.Arrange(new Rect(0, y, final.Width, c.DesiredSize.Height));
            y += c.DesiredSize.Height;
        }
        if (inCard) Close();
        if (!cards.SequenceEqual(_cards) || !lines.SequenceEqual(_lines))
        {
            (_cards, _lines) = (cards, lines);
            InvalidateVisual();
        }
        return final;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var stroke = StrokeBrush is { } s ? new Pen(s, 1) : null;
        foreach (var card in _cards)
            dc.DrawRoundedRectangle(CardBrush, stroke, new Rect(card.X + 0.5, card.Y + 0.5, Math.Max(0, card.Width - 1), Math.Max(0, card.Height - 1)), Radius, Radius);
        if (stroke is null) return;
        foreach (double y in _lines) dc.DrawLine(stroke, new Point(PadX, y + 0.5), new Point(RenderSize.Width - PadX, y + 0.5));
    }
}
