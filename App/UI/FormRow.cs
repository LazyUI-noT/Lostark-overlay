using System.Windows;
using System.Windows.Controls;

namespace GaugeOverlay.App.UI;

/// <summary>A settings row: the label in a fixed column beside the controls, or above them when the form is narrower
/// than <see cref="SideBySide"/> (520 px window, larger text).</summary>
public sealed class FormRow : Panel
{
    public const double LabelWidth = 180, SideBySide = 460;

    bool _side = true;

    public FormRow(UIElement label, UIElement controls)
    {
        Margin = new(0, 4, 0, 4);
        Children.Add(label);
        Children.Add(controls);
    }

    UIElement LabelPart => InternalChildren[0];
    UIElement Controls => InternalChildren[1];

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? LabelWidth * 3 : available.Width;
        _side = width >= SideBySide;
        if (_side)
        {
            LabelPart.Measure(new(LabelWidth - 8, available.Height));
            Controls.Measure(new(Math.Max(0, width - LabelWidth), available.Height));
            return new(width, Math.Max(LabelPart.DesiredSize.Height, Controls.DesiredSize.Height));
        }
        LabelPart.Measure(new(width, available.Height));
        Controls.Measure(new(width, available.Height));
        return new(width, LabelPart.DesiredSize.Height + 2 + Controls.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (_side)
        {
            double h = final.Height;
            LabelPart.Arrange(new(0, (h - LabelPart.DesiredSize.Height) / 2, LabelWidth - 8, LabelPart.DesiredSize.Height));
            Controls.Arrange(new(LabelWidth, 0, Math.Max(0, final.Width - LabelWidth), h));
        }
        else
        {
            LabelPart.Arrange(new(0, 0, final.Width, LabelPart.DesiredSize.Height));
            Controls.Arrange(new(0, LabelPart.DesiredSize.Height + 2, final.Width, Controls.DesiredSize.Height));
        }
        return final;
    }
}
