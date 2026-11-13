using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GaugeOverlay.App.Platform;

namespace GaugeOverlay.App.UI;

/// <summary>The settings forms' building blocks: headings, rows, check boxes, sliders, key fields, the ? help mark. Used by
/// 설정 and the first-run window (<c>using static</c>).</summary>
internal static class UiBuild
{
    /// <summary>Set by 설정 while it fills its sections: each setting row reports its label, help, element and, for rows that
    /// can be built again on their own (a check box, a slider), how (<see cref="SettingsRegistry"/>).</summary>
    internal static Action<string, string?, UIElement, Func<UIElement>?>? Report;

    /// <summary>A key field's Tag while it waits for a key: the window's own shortcuts (Ctrl+Z, Ctrl+F) leave the key to it.</summary>
    internal const string Capturing = "capturing";

    /// <summary>A real button drawn by its content alone, for parts drawn by hand (기능 맵's switches, 숙제's chips): Tab, Space and
    /// Narrator work and the look stays as drawn. A click that rebuilds the page gives focus back to the new control with the
    /// same <paramref name="key"/>, so a keyboard user stays where they were (audit 2026-11-12).</summary>
    internal static T Bare<T>(object look, string name, string key) where T : ButtonBase, new()
    {
        var b = new T { Template = BareLook, OverridesDefaultStyle = true, Content = look, Cursor = Cursors.Hand };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => { if (b.IsKeyboardFocused) FocusAfterRebuild(key); else _refocus = null; };   // added first: runs before the click's own rebuild
        if (key == _refocus)
            b.Loaded += (_, _) =>
            {
                if (_refocus != key) return;
                _refocus = null;
                b.Focus();
            };
        return b;
    }

    /// <summary>For a rebuild started by a key, not a click: the control made next with <paramref name="key"/> takes the focus.</summary>
    /// The key lasts until the app is idle: a rebuild's Loaded runs before that, and a click that rebuilds nothing (a lit
    /// segment, Space on ≡) doesn't leave it to steal focus from a later rebuild. So the rebuild must run above idle
    /// (synchronously or BeginInvoke at Normal, as the map and 숙제 do); one queued at Background or lower loses the focus.
    internal static void FocusAfterRebuild(string key)
    {
        _refocus = key;
        Application.Current.Dispatcher.BeginInvoke(
            () => { if (_refocus == key) _refocus = null; }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    // ponytail: one key for the whole app, as only the focused control can set it.
    static string? _refocus;
    static readonly ControlTemplate BareLook = Sealed(new ControlTemplate(typeof(ButtonBase)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) });

    static ControlTemplate Sealed(ControlTemplate t)
    {
        t.Seal();
        return t;
    }

    /// <summary>The focused element is a key field waiting for a key.</summary>
    internal static bool KeyFieldCapturing() => Keyboard.FocusedElement is Button { Tag: Capturing };

    /// <summary>A key field: click it, then press the key or combination (Esc cancels). A modifier alone shows
    /// "Ctrl+…" and is assigned only when released without another key, so Ctrl+Q can be entered.
    /// Shows friendly names ("1", "`") but stores the <see cref="KeyNames"/> format.</summary>
    internal static Button KeyField(Func<string> get, Action<string> set)
    {
        var f = new Button { MinWidth = 140 };
        bool capturing = false, modifierDown = false;
        void Show() { capturing = modifierDown = false; f.Tag = null; f.Content = get() == "" ? "(없음)" : KeyNames.Display(get()); }
        void Assign(string key) { set(key); Show(); }
        static Key Real(KeyEventArgs e) => e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        static string Held() => KeyNames.Combo(Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), "");

        f.Click += (_, _) => { capturing = true; f.Tag = Capturing; modifierDown = false; f.Content = "키를 누르세요…"; f.Focus(); };
        f.PreviewKeyDown += (_, e) =>
        {
            if (!capturing) return;
            e.Handled = true;
            var name = KeyNames.Of(Real(e));
            if (name == nameof(Key.Escape) && Keyboard.Modifiers == ModifierKeys.None) Show();
            else if (KeyNames.IsModifier(name)) { modifierDown = true; f.Content = Held() + "…"; }
            else Assign(Held() + name);
        };
        f.PreviewKeyUp += (_, e) =>
        {
            if (!capturing || !modifierDown) return;
            e.Handled = true;
            if (KeyNames.Of(Real(e)) is var name && KeyNames.IsModifier(name)) Assign(name);
        };
        f.LostKeyboardFocus += (_, _) => { if (capturing) Show(); };
        Show();
        return f;
    }

    internal static StackPanel Stack(params UIElement[] children)
    {
        var p = new StackPanel();
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    internal static TextBlock Heading(string text, bool first = false, string? help = null) =>
        Break(Helped(new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0, first ? 0 : 20, 0, 8) }, text, help));

    /// <summary>A heading stands between setting cards, not in one.</summary>
    internal static TextBlock Break(TextBlock t)
    {
        CardStack.SetBreak(t, true);
        return t;
    }

    /// <summary>A heading inside a part of a section (a buff, 칸 크기), a step under <see cref="Heading"/>.</summary>
    internal static TextBlock SubHeading(string text, string? help = null) =>
        Break(Helped(new() { FontWeight = FontWeights.SemiBold, Opacity = 0.85, Margin = new(0, 12, 0, 6) }, text, help));

    // ── 도움말 (?): the form shows the names; what each does is in the ? beside it ──────────────────

    /// <summary>A small ? whose tooltip explains the setting beside it, on hover or keyboard focus (Tab reaches it).</summary>
    internal static FrameworkElement HelpMark(string help)
    {
        var glyph = new TextBlock { Text = "?", FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mark = new Border
        {
            Width = 16, Height = 16, CornerRadius = new(8), BorderThickness = new(1), Margin = new(5, 0, 0, 0), Opacity = 0.75,
            Background = Brushes.Transparent, Child = glyph, Focusable = true, Cursor = Cursors.Help, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
        };
        mark.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(TextBlock.Foreground)) { Source = glyph });
        KeyboardNavigation.SetIsTabStop(mark, true);
        ToolTipService.SetInitialShowDelay(mark, 100);
        ToolTipService.SetShowDuration(mark, int.MaxValue);
        ToolTipService.SetShowsToolTipOnKeyboardFocus(mark, true);
        ToolTipService.SetShowOnDisabled(mark, true);   // rows greyed while a class follows 기본 still explain themselves
        // Inside a check box's label: clicking the ? doesn't tick the box.
        mark.PreviewMouseLeftButtonDown += (_, e) => { mark.Focus(); e.Handled = true; };
        System.Windows.Automation.AutomationProperties.SetName(mark, "도움말");
        System.Windows.Automation.AutomationProperties.SetHelpText(mark, help);
        return mark;
    }

    /// <summary><paramref name="t"/> showing <paramref name="text"/>, then the ? when there is <paramref name="help"/>; it
    /// follows the last word when the text wraps.</summary>
    internal static TextBlock Helped(TextBlock t, string text, string? help)
    {
        if (help is null) { t.Text = text; return t; }
        t.Inlines.Add(new System.Windows.Documents.Run(text));
        t.Inlines.Add(new System.Windows.Documents.InlineUIContainer(HelpMark(help)) { BaselineAlignment = BaselineAlignment.Center });
        System.Windows.Automation.AutomationProperties.SetName(t, text);   // read as the name, without the ?
        return t;
    }

    /// <summary>A row label with its ?.</summary>
    internal static TextBlock HelpLabel(string text, string? help) =>
        Helped(new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, text, help);

    internal static TextBlock Note(string text)
    {
        var note = Muted(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) });
        CardStack.SetBreak(note, true);   // a status line, not a setting: it stands between the cards
        return note;
    }

    /// <summary>Secondary text in the theme's muted colour (TextFillColorSecondaryBrush), not faded by opacity: its contrast then
    /// doesn't depend on the background (impeccable audit, 2026-11-12). Themes.cs sets the palette's Muted on that key.</summary>
    internal static TextBlock Muted(TextBlock t)
    {
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return t;
    }

    /// <summary>A row label; an <paramref name="opacity"/> below 1 means secondary text (<see cref="Muted"/>).</summary>
    internal static TextBlock Label(string text, double opacity = 1)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        return opacity < 1 ? Muted(label) : label;
    }

    /// <summary>A row label with its ? saying what the row does.</summary>
    internal static TextBlock Described(string label, string description) => HelpLabel(label, description);

    internal static ComboBox Combo() => new() { MinWidth = 200 };

    /// <summary>The accent when the window has no theme (위치 조정's layer, which doesn't set ThemeMode).</summary>
    static readonly Color FallbackAccent = Color.FromRgb(0x00, 0x67, 0xC0);

    /// <summary>The Fluent keys a button's template swaps in on mouse-over and press; set on the button itself they win over the theme's.</summary>
    static readonly string[] HoverKeys =
        ["ButtonBackgroundPointerOver", "ButtonBorderBrushPointerOver", "ButtonForegroundPointerOver",
         "ButtonBackgroundPressed", "ButtonBorderBrushPressed", "ButtonForegroundPressed"];

    /// <summary>A button in its own colour that keeps it under the mouse: a little lighter on mouse-over, a little darker
    /// pressed, instead of the theme's grey (user, 2026-11-10). <paramref name="border"/> defaults to the fill.</summary>
    internal static void Fill(Button b, Color back, Brush ink, Brush? border = null)
    {
        (b.Background, b.BorderBrush, b.Foreground) = (new SolidColorBrush(back), border ?? new SolidColorBrush(back), ink);
        var over = new SolidColorBrush(Shade(back, 0.15));
        var pressed = new SolidColorBrush(Shade(back, -0.15));
        b.Resources["ButtonBackgroundPointerOver"] = over;
        b.Resources["ButtonBorderBrushPointerOver"] = border ?? over;
        b.Resources["ButtonBackgroundPressed"] = pressed;
        b.Resources["ButtonBorderBrushPressed"] = border ?? pressed;
        b.Resources["ButtonForegroundPointerOver"] = b.Resources["ButtonForegroundPressed"] = ink;
    }

    /// <summary>Undoes <see cref="Fill"/>: the theme's colours again.</summary>
    internal static void Unfill(Button b)
    {
        b.ClearValue(Control.BackgroundProperty);
        b.ClearValue(Control.ForegroundProperty);
        b.ClearValue(Control.BorderBrushProperty);
        foreach (var key in HoverKeys) b.Resources.Remove(key);
    }

    /// <summary><paramref name="c"/> toward white (amount &gt; 0) or black (&lt; 0).</summary>
    static Color Shade(Color c, double amount)
    {
        byte To(byte v) => (byte)Math.Round(amount > 0 ? v + (255 - v) * amount : v * (1 + amount));
        return Color.FromArgb(c.A, To(c.R), To(c.G), To(c.B));
    }

    /// <summary>An 적용 button while its screen has a change not applied yet: the theme's accent fill, its text on accent and a soft glow in
    /// that accent. Plain again when nothing is left to apply (after 적용, 되돌리기, 그만두기). Shared by 위치 조정, 감지 위치 and 전투 기록 편집.
    /// The colours are set here, not by AccentButtonStyle: that style exists only in a window with a ThemeMode.</summary>
    internal static void MarkUnapplied(Button apply, bool unapplied)
    {
        if (unapplied == (apply.Effect is not null)) return;   // as it is: 위치 조정 calls this on every read (~5 a second)
        if (!unapplied)
        {
            Unfill(apply);
            apply.Effect = null;
            return;
        }
        // The accent as it is now: a theme change recolours a lit button when it is lit again, like its glow.
        var accent = (apply.TryFindResource("AccentFillColorDefaultBrush") as SolidColorBrush)?.Color ?? FallbackAccent;
        var text = apply.TryFindResource("TextOnAccentFillColorPrimaryBrush") as Brush ?? Brushes.White;
        Fill(apply, accent, text);
        apply.Effect = new DropShadowEffect { Color = accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 };
    }

    /// <summary>A row that only applies to some choices is hidden, not greyed, for the others.</summary>
    internal static Visibility Shown(bool applies) => applies ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Controls side by side, wrapping onto the next line when the form is narrow.</summary>
    internal static WrapPanel Wrap(params UIElement[] controls)
    {
        var w = new WrapPanel();
        foreach (var c in controls)
        {
            if (c is FrameworkElement f && f.Margin == default) f.Margin = new(0, 2, 8, 2);
            w.Children.Add(c);
        }
        return w;
    }

    internal static FormRow Row(string label, params UIElement[] controls) => Row(Label(label), controls);

    internal static FormRow HelpRow(string label, string help, params UIElement[] controls)
    {
        var row = new FormRow(HelpLabel(label, help), Wrap(controls));
        Report?.Invoke(label.Trim(), help, row, null);   // with its help, which search reads too
        return row;
    }

    internal static FormRow Row(UIElement label, params UIElement[] controls)
    {
        var row = new FormRow(label, Wrap(controls));
        if (Report is not null && label is TextBlock t && (System.Windows.Automation.AutomationProperties.GetName(t) is { Length: > 0 } named ? named : t.Text) is { Length: > 0 } text)
            Report(text.Trim(), null, row, null);
        return row;
    }

    internal static CheckBox Check(string text, bool value, Action<bool> set, Action changed, string? help = null)
    {
        // A TextBlock, not the string: a long label wraps under itself instead of running off the card.
        var c = new CheckBox { Content = Helped(new() { TextWrapping = TextWrapping.Wrap }, text, help), IsChecked = value, Margin = new(0, 2, 16, 2) };
        if (help is not null) System.Windows.Automation.AutomationProperties.SetName(c, text);
        c.Click += (_, _) => { set(c.IsChecked == true); changed(); };
        Report?.Invoke(text.Trim(), help, c, () => Check(text, value, set, changed, help));
        return c;
    }

    internal static UIElement SliderRow(string label, double min, double max, double value, double step,
        Func<double, string> format, Action<double> set, Action changed, string? help = null)
    {
        var text = new TextBlock { Text = format(value), Width = 44, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value, Width = 180, MinWidth = 120, TickFrequency = step,
            IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, e) => { set(e.NewValue); text.Text = format(e.NewValue); changed(); };
        var row = new FormRow(HelpLabel(label, help), Wrap(slider, text));
        Report?.Invoke(label.Trim(), help, row, () => SliderRow(label, min, max, value, step, format, set, changed, help));
        return row;
    }
}
