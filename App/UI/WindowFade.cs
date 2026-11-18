using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GaugeOverlay.Core.Domain;
using Timeline = System.Windows.Media.Animation.Timeline;

namespace GaugeOverlay.App.UI;

/// <summary>A read-driven overlay window (미터기, 줄 수 창, 보스 체력, 대난투 체력) shown or hidden by <see cref="WindowChange"/>
/// (overlay-motion.plan.md): <see cref="WindowChange.FadeIn"/> fades the window in from <see cref="OverlayMotion.FadeFrom"/>
/// while its content rises <see cref="OverlayMotion.Rise"/> px, <see cref="WindowChange.FadeOut"/> fades it out and then
/// hides it; anything else, or motion off (<see cref="UiMotion.On"/>), is at once. On the window's opacity, not in the
/// renderer (it clears its state when it becomes visible). Frames capped at 초당 그리기 횟수.</summary>
sealed class WindowFade
{
    readonly Window _window;
    readonly UIElement _content;
    readonly Settings _settings;
    readonly Action _show, _hide;
    bool _leaving, _closed;   // a fade-out under way; the window closed (the app quitting): nothing more is done to it
    int _fadeNumber;          // the latest fade-out's, so a cancelled one's end does nothing

    public WindowFade(Window window, UIElement content, Settings settings, Action show, Action hide)
    {
        (_window, _content, _settings, _show, _hide) = (window, content, settings, show, hide);
        window.Closed += (_, _) => { _closed = true; _leaving = false; };
    }

    public void Apply(bool shown, WindowChange change)
    {
        if (_closed) return;
        if (shown)
        {
            if (_leaving) Stop();
            _show();
            if (change == WindowChange.FadeIn && UiMotion.On) Arrive();
            return;
        }
        if (_leaving && change == WindowChange.None) return;   // still fading out: let it end (a gate change comes as Snap)
        if (!_leaving && !_window.IsVisible) return;          // already hidden
        if (change == WindowChange.FadeOut && !_leaving && UiMotion.On) { Leave(); return; }
        Stop();
        _hide();
    }

    void Arrive()
    {
        if (_content.RenderTransform is not TranslateTransform { IsFrozen: false } shift) _content.RenderTransform = shift = new TranslateTransform();
        var time = TimeSpan.FromMilliseconds(OverlayMotion.InMs);
        _window.BeginAnimation(UIElement.OpacityProperty, Capped(UiMotion.Eased(OverlayMotion.FadeFrom, 1, time)));
        shift.BeginAnimation(TranslateTransform.YProperty, Capped(UiMotion.Eased(OverlayMotion.Rise, 0, time)));
    }

    void Leave()
    {
        (_leaving, int n) = (true, ++_fadeNumber);
        var go = Capped(UiMotion.Eased(1, 0, TimeSpan.FromMilliseconds(OverlayMotion.OutMs)));
        go.FillBehavior = FillBehavior.HoldEnd;   // stays out until hidden
        go.Completed += (_, _) =>
        {
            if (!_leaving || _closed || n != _fadeNumber) return;
            _hide();
            Stop();
        };
        _window.BeginAnimation(UIElement.OpacityProperty, go);
    }

    void Stop()
    {
        _leaving = false;
        _window.BeginAnimation(UIElement.OpacityProperty, null);
        if (_content.RenderTransform is TranslateTransform { IsFrozen: false } shift) shift.BeginAnimation(TranslateTransform.YProperty, null);
    }

    T Capped<T>(T timeline) where T : Timeline
    {
        Timeline.SetDesiredFrameRate(timeline, DrawSteps.Clamp(_settings.DrawsPerSecond));
        return timeline;
    }
}
