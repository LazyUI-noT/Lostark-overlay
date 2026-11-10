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

    /// <summary>The label's words, for naming what belongs to the row (its ↺).</summary>
    internal string LabelText => NoLabel && FirstControl(Controls) is CheckBox c ? AutomationProperties.GetName(c).Trim()
        : LabelPart is TextBlock t ? (AutomationProperties.GetName(t) is { Length: > 0 } n ? n : t.Text).Trim() : "";

    /// <summary>The row rule's marks (SettingsWindow.Defaults.cs): a dot after the label's words (a check-box row: after the
    /// last check box's), and the ↺ as the row's own child at its right edge, the same column on every row.</summary>
    internal void AddMark(System.Windows.Documents.Run dot, UIElement reset)
    {
        if (NoLabel)
        {
            // After the last check box: a row of several (one ↺ for all) reads as one line ending in the dot.
            if (Controls is Panel cp && cp.Children.OfType<CheckBox>().LastOrDefault() is { Content: TextBlock words } check)
            {
                if (AutomationProperties.GetName(check).Length == 0) AutomationProperties.SetName(check, words.Text);   // without the dot
                words.Inlines.Add(dot);
            }
        }
        else if (LabelPart is TextBlock t)
        {
            AutomationProperties.SetName(t, LabelText);   // screen readers say the words, not the dot
            t.Inlines.Add(dot);
        }
        Children.Add(reset);   // InternalChildren[2]
    }

    /// <summary>Row("", …): nothing to pair with, so the controls start at the left edge, as a card's own check boxes do.</summary>
    // A label with a ? keeps its words in Inlines, so Text is empty there: its automation name tells it apart.
    bool OnlyChecks => Controls is Panel { Children.Count: > 0 } p && p.Children.OfType<UIElement>().All(c => c is CheckBox);
    UIElement? Reset => InternalChildren.Count > 2 ? InternalChildren[2] : null;
    bool NoLabel => LabelPart is TextBlock { Text.Length: 0 } t && AutomationProperties.GetName(t).Length == 0;

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? LabelWidth * 3 : available.Width;
        _side = width >= SideBySide;
        Reset?.Measure(available);
        double rw = Reset?.DesiredSize.Width ?? 0, rh = Reset?.DesiredSize.Height ?? 0;
        if (NoLabel)
        {
            Controls.Measure(new(Math.Max(0, width - rw), available.Height));
            return new(width, Math.Max(Controls.DesiredSize.Height, rh));
        }
        if (_side)
        {
            LabelPart.Measure(new(LabelWidth - 8, available.Height));
            Controls.Measure(new(Math.Max(0, width - LabelWidth - rw), available.Height));
            return new(width, Math.Max(Math.Max(LabelPart.DesiredSize.Height, Controls.DesiredSize.Height), rh));
        }
        LabelPart.Measure(new(width, available.Height));
        Controls.Measure(new(Math.Max(0, width - rw), available.Height));
        return new(width, LabelPart.DesiredSize.Height + 2 + Math.Max(Controls.DesiredSize.Height, rh));
    }

    protected override Size ArrangeOverride(Size final)
    {
        double rw = Reset?.DesiredSize.Width ?? 0;
        if (NoLabel)
        {
            Controls.Arrange(new(0, 0, Math.Max(0, final.Width - rw), final.Height));
            PlaceReset(final.Width, 0, final.Height);
        }
        else if (_side)
        {
            double h = final.Height;
            LabelPart.Arrange(new(0, (h - LabelPart.DesiredSize.Height) / 2, LabelWidth - 8, LabelPart.DesiredSize.Height));
            // The controls at the right edge (before the ↺), as Windows 11's setting cards; a row wider than the space keeps it all.
            double w = Math.Max(0, final.Width - LabelWidth - rw), cw = Math.Min(w, Controls.DesiredSize.Width);
            // Check boxes differ in width, so at the right edge their boxes wander: they line up beside the label instead.
            Controls.Arrange(new(OnlyChecks ? LabelWidth : final.Width - rw - cw, 0, cw, h));
            PlaceReset(final.Width, 0, h);
        }
        else
        {
            double top = LabelPart.DesiredSize.Height + 2, h = Math.Max(Controls.DesiredSize.Height, Reset?.DesiredSize.Height ?? 0);
            LabelPart.Arrange(new(0, 0, final.Width, LabelPart.DesiredSize.Height));
            Controls.Arrange(new(0, top, Math.Max(0, final.Width - rw), Controls.DesiredSize.Height));
            PlaceReset(final.Width, top, h);
        }
        return final;
    }

    /// <summary>The ↺ at the right edge, centred in the band from <paramref name="top"/> over <paramref name="height"/>.</summary>
    void PlaceReset(double width, double top, double height)
    {
        if (Reset is not { } r) return;
        r.Arrange(new(width - r.DesiredSize.Width, top + (height - r.DesiredSize.Height) / 2, r.DesiredSize.Width, r.DesiredSize.Height));
    }
}
