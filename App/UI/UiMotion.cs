using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The one door every 설정 animation goes through (ui-motion-tokens.plan.md phase 3, <c>.interface-design/system.md</c>
/// "Motion"): <see cref="On"/> asks <see cref="Motion.Runs"/> with Windows' 애니메이션 효과, the remote session and
/// 일반 → 애니메이션. Read each time (not cached), so a switch in Windows or in 설정 counts at the next animation.
/// The in-game overlay's existing motion (sweep, shine, glow, blink) keeps its own rule in <see cref="OverlayRenderer"/>;
/// its new motion (overlay-motion.plan.md: <see cref="WindowFade"/>, buff arrivals, the 미터기's bars) asks <see cref="On"/>.
/// 설정's window hands the settings over at start-up (App.BuildUi builds it), so the overlay sees the switch from the start.</summary>
static class UiMotion
{
    /// <summary>The settings object, not a window's closure: a closed window must not stay alive through this field.</summary>
    static Settings? _settings;

    /// <summary>설정 and 시작하기 hand over the settings (the same object); until then only Windows and the session decide.</summary>
    public static void Follow(Settings settings) => _settings = settings;

    public static bool On => Motion.Runs(SystemParameters.ClientAreaAnimation, SystemParameters.IsRemoteSession, _settings?.Animations ?? true);

    /// <summary>system.md "Motion", page change by mouse: fades in, rises 8 px, 160 ms.</summary>
    public const double PageRise = 8;
    public static readonly TimeSpan PageTime = TimeSpan.FromMilliseconds(160);

    /// <summary>system.md "Motion", a card or 기능 맵 box opens: its new content fades in, drops 4 px, 180 ms; closing is at once.</summary>
    public const double OpenDrop = 4;
    public static readonly TimeSpan OpenTime = TimeSpan.FromMilliseconds(180);

    /// <summary>The last input was the mouse: what was started from the keyboard (rail arrows, Ctrl+F, Enter) never moves.</summary>
    static bool ByMouse => DemoMouse || InputManager.Current.MostRecentInputDevice is MouseDevice;

    /// <summary>--demo --motion=: the recording plays what a click starts without one; never set on a normal start.</summary>
    internal static bool DemoMouse;

    /// <summary>system.md's <c>Out</c>, cubic-bezier(0.23, 1, 0.32, 1): everything that arrives; nothing eases in.</summary>
    static readonly KeySpline Out = Frozen(new KeySpline(0.23, 1, 0.32, 1));

    /// <summary>Fades <paramref name="e"/> in while it rises <paramref name="rise"/> px into place (negative: comes down from
    /// above, as opened content does). Only when motion is
    /// <see cref="On"/> and a click started it; the caller decides it is new content, not a refill. A second call mid-way
    /// starts it again from the bottom; the end state is the element's own (opacity 1, no shift).</summary>
    public static void Arrive(UIElement e, double rise, TimeSpan time)
    {
        if (!On || !ByMouse) return;
        if (e.RenderTransform != Transform.Identity && e.RenderTransform is not TranslateTransform) return;   // its own transform stays
        if (e.RenderTransform is not TranslateTransform shift || shift.IsFrozen) e.RenderTransform = shift = new TranslateTransform();
        e.BeginAnimation(UIElement.OpacityProperty, Eased(0, 1, time));
        shift.BeginAnimation(TranslateTransform.YProperty, Eased(rise, 0, time));
    }

    /// <summary>The search flash (baseline "Motion review"): from 0.6, not 0.2, so the row stays readable at the moment it is
    /// looked for; 400 ms <c>Out</c>, not 1 s linear. It finds the row, so it runs after Enter too (only <see cref="On"/>).</summary>
    const double FlashFrom = 0.6;
    static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(400);

    public static void Flash(UIElement e)
    {
        if (On) e.BeginAnimation(UIElement.OpacityProperty, Eased(FlashFrom, 1, FlashTime));
    }

    /// <summary>system.md "Motion", button press: scale 0.98, 100 ms down, 160 ms back on release.</summary>
    const double PressScale = 0.98;
    static readonly TimeSpan PressDown = TimeSpan.FromMilliseconds(100), PressUp = TimeSpan.FromMilliseconds(160);

    /// <summary>Every <see cref="Button"/> under <paramref name="window"/> answers a mouse press (check boxes, toggles, the
    /// rail's radio buttons and scroll bars are other types: Fluent's own transitions stay). A button that already has a
    /// transform of its own is left alone.</summary>
    public static void PressButtons(UIElement window) =>
        window.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) => Press(ButtonOf(e.OriginalSource))), true);

    /// <summary>--demo --motion=press: the press handler on a button, without a routed event (a hand-made one did not reach it).</summary>
    internal static void DemoPress(Button b) => Press(b);

    static void Press(Button? b)
    {
        if (b is not { IsEnabled: true } || !On) return;
        if (b.RenderTransform is not ScaleTransform { IsFrozen: false } scale)
        {
            if (b.RenderTransform != Transform.Identity) return;
            (b.RenderTransform, b.RenderTransformOrigin) = (scale = new ScaleTransform(), new Point(0.5, 0.5));
        }
        ScaleTo(scale, PressScale, PressDown);
        b.LostMouseCapture -= Release;   // the button holds the mouse until the release, a drag off it or Alt+Tab
        b.LostMouseCapture += Release;
        // A button that never took the mouse (ClickMode.Hover, or its press handled above it) gets no LostMouseCapture:
        // once the press has been handled, it goes back at once.
        b.Dispatcher.BeginInvoke(() => { if (!b.IsMouseCaptured) Restore(b); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    static void Release(object sender, MouseEventArgs e) => Restore((Button)sender);

    static void Restore(Button b)
    {
        b.LostMouseCapture -= Release;
        if (b.RenderTransform is ScaleTransform scale) ScaleTo(scale, 1, PressUp);
    }

    static void ScaleTo(ScaleTransform scale, double to, TimeSpan time)
    {
        var go = new DoubleAnimationUsingKeyFrames { KeyFrames = { new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(time), Out) } };   // from where it is
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, go);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, go);
    }

    static Button? ButtonOf(object? source)
    {
        for (var d = source as DependencyObject; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is Button b) return b;
        return null;
    }

    internal static DoubleAnimationUsingKeyFrames Eased(double from, double to, TimeSpan time) => new()
    {
        FillBehavior = FillBehavior.Stop,   // back to the base value, which is the end state
        KeyFrames =
        {
            new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)),
            new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(time), Out),
        },
    };

    static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
