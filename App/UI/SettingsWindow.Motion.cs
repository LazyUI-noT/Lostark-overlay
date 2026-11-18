using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace GaugeOverlay.App.UI;

/// <summary>Hooks for <c>--demo --motion=</c> (App.Motion.cs): the real controls driven as a click would, with nothing sent to
/// the screen. Demo only; a normal start never calls them.</summary>
public partial class SettingsWindow
{
    internal nint DemoHandle => new WindowInteropHelper(this).Handle;

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) yield return hit;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    Button? _demoPressed;
    string? _demoRow;

    /// <summary>A mouse press on the first plain button of the page: UiMotion's press handler sees the tunnelling event, and the
    /// button holds the mouse until <see cref="DemoRelease"/>, as under a real press.</summary>
    internal void DemoPress()
    {
        _demoPressed = Descendants<Button>(SectionPage).FirstOrDefault(b => b.IsVisible && b.IsEnabled && b.ActualWidth > 40 && b.RenderTransform == Transform.Identity);
        if (_demoPressed is not { } b) return;
        UiMotion.DemoPress(b);
        b.CaptureMouse();
    }

    internal void DemoRelease() => _demoPressed?.ReleaseMouseCapture();

    /// <summary>The first raid box's header, clicked through its automation peer.</summary>
    internal void DemoOpenFirstRaid()
    {
        var header = Descendants<Button>(Body).FirstOrDefault(b => AutomationProperties.GetName(b).EndsWith("펼치기", StringComparison.Ordinal));
        if (header is not null) ((IInvokeProvider)new ButtonAutomationPeer(header).GetPattern(PatternInterface.Invoke)!).Invoke();
    }

    /// <summary>The 애니메이션 check box's row, found as a search result is: its page, scrolled to, flashed.</summary>
    internal void DemoFlashRow() => _registry.Navigate(_demoRow = (_registry.All.FirstOrDefault(e => e.Path.Contains("테마")) ?? _registry.All.FirstOrDefault() ?? throw new InvalidOperationException("search registry empty")).Id);

    /// <summary>the lowest opacity among what an animation is moving (UiMotion.Arrive animates opacity from 0): the recording's number for a frame.</summary>
    internal double? DemoMinOpacity() => Descendants<UIElement>(SectionPage).Prepend(SectionPage).Where(e => e.IsVisible && e.HasAnimatedProperties).Select(e => e.Opacity).DefaultIfEmpty(1).Min();   // only what an animation is moving

    internal double? DemoPressScale() => _demoPressed?.RenderTransform is ScaleTransform s ? s.ScaleX : 1;

    internal double? DemoRowOpacity() => _registry.ElementOf(_demoRow ?? "")?.Opacity;

    internal void DemoMapToggle(string id) => _map?.DemoToggle(id);

    static System.Drawing.Rectangle? ScreenBounds(FrameworkElement? e, int pad)
    {
        if (e is null || PresentationSource.FromVisual(e) is not { } source) return null;
        var to = source.CompositionTarget.TransformToDevice;
        var at = e.PointToScreen(new Point(0, 0));
        return new System.Drawing.Rectangle((int)at.X - pad, (int)at.Y - pad, (int)(e.ActualWidth * to.M11) + 2 * pad, (int)(e.ActualHeight * to.M22) + 2 * pad);
    }

    /// <summary>The pressed button and the flashed row in screen pixels, for the recording's crop.</summary>
    internal System.Drawing.Rectangle? DemoPressBounds() => ScreenBounds(_demoPressed, 60);
    internal System.Drawing.Rectangle? DemoRowBounds() => ScreenBounds(_registry.ElementOf(_demoRow ?? "") as FrameworkElement, 40);
}
