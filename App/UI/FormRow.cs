using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace GaugeOverlay.App.UI;

/// <summary>A settings row: the label in a fixed column beside the controls, or above them when the form is narrower
/// than <see cref="SideBySide"/> (520 px window, larger text).</summary>
public sealed class FormRow : Panel
{
    public const double LabelWidth = 140, SideBySide = 380;

    bool _side = true;

    public FormRow(UIElement label, UIElement controls)
    {
        Margin = new(0, 4, 0, 4);
        Children.Add(label);
        Children.Add(controls);
        // Screen readers name the row's first control by its label ("각성기", not just "V").
        if (FirstControl(controls) is { } first && AutomationProperties.GetLabeledBy(first) is null) AutomationProperties.SetLabeledBy(first, label);
    }

    static Control? FirstControl(UIElement e) => e switch
    {
        Control c => c,
        Panel p => p.Children.OfType<UIElement>().Select(FirstControl).FirstOrDefault(c => c is not null),
        _ => null,
    };

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
            // The controls at the right edge, as Windows 11's setting cards; a row wider than the space keeps it all.
            double w = Math.Max(0, final.Width - LabelWidth), cw = Math.Min(w, Controls.DesiredSize.Width);
            Controls.Arrange(new(final.Width - cw, 0, cw, h));
        }
        else
        {
            LabelPart.Arrange(new(0, 0, final.Width, LabelPart.DesiredSize.Height));
            Controls.Arrange(new(0, LabelPart.DesiredSize.Height + 2, final.Width, Controls.DesiredSize.Height));
        }
        return final;
    }
}
