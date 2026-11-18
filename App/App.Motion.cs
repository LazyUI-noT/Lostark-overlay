using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using GaugeOverlay.App.UI;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App;

/// <summary>--demo --motion=&lt;name&gt; [--motion-out=&lt;folder&gt;] [--motion-slow=4]: plays one 설정 or tray motion and grabs its frames
/// (<see cref="MotionRecorder"/>, PrintWindow: no screen capture, no input sent; <see cref="UiMotion.DemoMouse"/> stands in for the
/// click), writes them with a contact sheet, then exits. Names: page, page-off (애니메이션 off), map-open, raid-open, press,
/// first-run, search-flash, map-line, tray. Normal starts never get here.</summary>
public partial class App
{
    async Task RecordMotion(string name, string folder, Settings settings, double slow)
    {
        try
        {
            if (name.EndsWith("-off", StringComparison.Ordinal)) settings.Animations = false;
            UiMotion.DemoMouse = true;
            FirstRunWindow? first = null;
            Func<nint> window = () => _settingsWindow.DemoHandle;
            Func<double?>? probe = _settingsWindow.DemoMinOpacity;   // what a frame shows is read against the number: the lowest opacity on the page
            bool fromFirstFrame = false;
            Action start = () => { };
            Func<Rectangle?> crop = () => null;
            double[] marks = [0, 20, 40, 60, 80, 110, 160, 220];
            double after = 500, before = 150;

            switch (name)
            {
                case "page" or "page-off":
                    _settingsWindow.ShowTab("키·단축키");
                    start = () => _settingsWindow.ShowTab("테마·업데이트");
                    break;
                case "map-open":
                    _settingsWindow.ShowTab("기능 맵");
                    start = () => _settingsWindow.DemoMapToggle("boss");
                    marks = [0, 20, 40, 70, 100, 140, 180, 260];
                    break;
                case "raid-open":
                    _settingsWindow.ShowTab("레이드");
                    start = _settingsWindow.DemoOpenFirstRaid;
                    marks = [0, 20, 40, 70, 100, 140, 180, 260];
                    break;
                case "press":
                    probe = _settingsWindow.DemoPressScale;
                    _settingsWindow.ShowTab("테마·업데이트");
                    start = () =>
                    {
                        _settingsWindow.DemoPress();
                        _ = Task.Delay(300).ContinueWith(_ => Dispatcher.Invoke(_settingsWindow.DemoRelease));
                    };
                    crop = () => _settingsWindow.DemoPressBounds();
                    marks = [0, 40, 100, 200, 300, 340, 400, 460];
                    after = 700;
                    break;
                case "first-run":
                    first = ShowFirstRun(() => { }, fitted: false);
                    window = () => new WindowInteropHelper(first).Handle;
                    probe = () => first.Page.Opacity;
                    start = () => first.NextButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    break;
                case "search-flash":
                    _settingsWindow.ShowTab("테마·업데이트");
                    probe = _settingsWindow.DemoRowOpacity;
                    start = _settingsWindow.DemoFlashRow;
                    crop = () => _settingsWindow.DemoRowBounds();
                    marks = [0, 50, 100, 150, 200, 300, 400, 520];
                    break;
                case "map-line":
                    _settingsWindow.DemoMap(null, null, seen: string.Join(',', FeatureMap.Boxes.Select(b => b.Id)));
                    _settingsWindow.ShowTab("기능 맵");
                    marks = [0, 150, 300, 450, 600, 750, 900, 1050];
                    before = 0;
                    after = 1200;
                    break;
                case "tray":
                    window = () => _tray.DemoMenu is { IsHandleCreated: true, Visible: true } m ? m.Handle : 0;
                    probe = () => _tray.DemoMenu.Visible ? _tray.DemoMenu.Opacity : null;
                    start = () => _tray.DemoShowMenu(null);
                    fromFirstFrame = true;
                    marks = [0, 15, 30, 45, 60, 80, 100, 140];
                    after = 400;
                    break;
                default:
                    throw new ArgumentException($"--motion={name}: page, page-off, map-open, raid-open, press, first-run, search-flash, map-line or tray");
            }

            await Task.Delay(1800);   // the window up and laid out before the first frame
            var recorder = new MotionRecorder(window, probe);
            await recorder.Record(Dispatcher, start, before, after);
            recorder.Save(folder, slow, marks, crop(), fromFirstFrame);
            Trace.WriteLine($"[motion] {name}: saved to {folder}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[motion] {name} failed: {ex}");
            Environment.ExitCode = 1;
        }
        Shutdown();
    }
}
