using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using GaugeOverlay.Core.Domain;
using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>The row rule (settings-design.plan.md §2): a row whose value differs from a fresh <see cref="Settings"/> shows a dot
/// by its label and a ↺ at its end that puts the default back. Pages opt in row by row with <see cref="Defaulted{T}"/>.</summary>
public partial class SettingsWindow
{
    readonly List<(FormRow Row, Action Refresh)> _rowMarks = [];

    /// <summary>Marks <paramref name="row"/> (a <see cref="FormRow"/> from Row, SliderRow, ColorRow …) with the row rule.
    /// <paramref name="get"/> reads the one value the row sets; <paramref name="set"/> writes it.</summary>
    UIElement Defaulted<T>(UIElement row, Func<Settings, T> get, Action<Settings, T> set)
    {
        if (row is not FormRow form) return row;
        var dot = new Run();
        dot.SetResourceReference(TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        var reset = new Button { Content = "↺", Padding = new(6, 0, 6, 0), Margin = new(4, 2, 0, 2), ToolTip = "기본값으로 되돌립니다." };
        AutomationProperties.SetName(reset, $"{form.LabelText} 기본값으로");
        reset.Click += (_, _) =>
        {
            RecordStep();   // BuildAll moves the baseline past the reset: the state before it must be a step now
            RowDefault.Reset(_s, get, set);
            BuildAll();
            Changed();
        };
        form.AddMark(dot, reset);

        void Refresh()
        {
            bool differs = RowDefault.Differs(_s, get);
            dot.Text = differs ? " ●" : "";
            reset.Visibility = differs ? Visibility.Visible : Visibility.Hidden;   // Hidden, not Collapsed: the row doesn't jump
        }
        Refresh();
        _rowMarks.Add((form, Refresh));
        return row;
    }

    /// <summary>A check box as its own row under the row rule: no label, the dot after its words, the ↺ at the row's end.</summary>
    UIElement DefaultedCheck(CheckBox check, Func<Settings, bool> get, Action<Settings, bool> set) => Defaulted(Row("", check), get, set);

    /// <summary>After any change: every row's mark follows its value, on hidden pages too (ShowBody detaches those, so
    /// "still in the window" can't tell a dropped row).</summary>
    void RefreshRowMarks()
    {
        foreach (var (_, refresh) in _rowMarks) refresh();
    }

    /// <summary>Refill is about to clear <paramref name="page"/>: its rows' marks go, nested ones (in a card) too.</summary>
    // ponytail: rows built outside Refill (the inspector) stay listed until 설정 closes; a few refreshes, no harm.
    void ForgetRowMarks(Panel page) => _rowMarks.RemoveAll(m => Inside(m.Row, page));

    static bool Inside(DependencyObject e, DependencyObject page)
    {
        for (var x = LogicalTreeHelper.GetParent(e); x is not null; x = LogicalTreeHelper.GetParent(x))
            if (x == page) return true;
        return false;
    }
}
