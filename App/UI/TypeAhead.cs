using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace GaugeOverlay.App.UI;

/// <summary>A plain text box with suggestions in a list under it that never takes focus, so typing (and IME 자모) goes on in
/// the box: an editable ComboBox rewrote its text mid-composition and lost the next 자모 (decisions: 전투 기록 편집). A click,
/// or ↑/↓ then Enter, picks; ↑/↓ only mark a row; Esc or leaving the box closes the list.</summary>
static class TypeAhead
{
    const double MaxListHeight = 240;

    /// <param name="suggest">The rows for what is typed (the text as is); none closes the list.</param>
    /// <param name="choose">A row picked.</param>
    /// <param name="enterTakesTop">Enter with no row marked picks the top one (a search); else it does nothing (a free name).</param>
    public static void Attach(TextBox box, Func<string, IReadOnlyList<string>> suggest, Action<string> choose, bool enterTakesTop = false)
    {
        var list = new ListBox { MaxHeight = MaxListHeight, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Focusable = false };
        list.ItemContainerStyle = new Style(typeof(ListBoxItem), box.TryFindResource(typeof(ListBoxItem)) as Style)
            { Setters = { new Setter(UIElement.FocusableProperty, false) } };
        var drop = new Border { Child = list, BorderThickness = new(1), CornerRadius = new(6), Padding = new(2) };
        drop.SetResourceReference(Border.BackgroundProperty, "ComboBoxDropDownBackground");   // the theme's dropdown look
        drop.SetResourceReference(Border.BorderBrushProperty, "ComboBoxDropDownBorderBrush");
        var popup = new Popup { PlacementTarget = box, Placement = PlacementMode.Bottom, AllowsTransparency = true, Child = drop };
        box.LostKeyboardFocus += (_, _) => popup.IsOpen = false;
        void Show()
        {
            var rows = suggest(box.Text);
            list.ItemsSource = rows;
            drop.MinWidth = box.ActualWidth;
            popup.IsOpen = box.IsKeyboardFocused && rows.Count > 0;
        }
        // The rows can't take focus, and a ListBoxItem selects on a click only when it can: the click is handled here.
        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not DependencyObject hit || ItemsControl.ContainerFromElement(list, hit) is not ListBoxItem { Content: string n }) return;
            popup.IsOpen = false;
            choose(n);
            e.Handled = true;
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (!popup.IsOpen) return;
            switch (e.Key)
            {
                case Key.Escape: popup.IsOpen = false; break;
                case Key.Down: list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1); break;
                case Key.Up: list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0); break;
                case Key.Enter when (list.SelectedItem ?? (enterTakesTop && list.Items.Count > 0 ? list.Items[0] : null)) is string n:
                    popup.IsOpen = false;
                    choose(n);
                    break;
                default: return;
            }
            e.Handled = true;
        };
        box.GotKeyboardFocus += (_, _) => Show();
        box.TextChanged += (_, _) => Show();
    }
}
