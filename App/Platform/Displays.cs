using System.Runtime.InteropServices;
using System.Windows;
using GaugeOverlay.Core.Domain;
using Forms = System.Windows.Forms;

namespace GaugeOverlay.App.Platform;

/// <summary>The monitors, in the units of the windows' Left and Top (<see cref="DisplayArea"/>), for 표시 화면.</summary>
public static partial class Displays
{
    /// <summary>Where a window with no spot of its own goes: the 표시 화면's work area, else the primary's.</summary>
    public static Rect WorkArea => _workArea ?? SystemParameters.WorkArea;
    static Rect? _workArea;

    /// <summary>Connected displays with their pixel sizes, for the 표시 화면 list.</summary>
    public static IReadOnlyList<(string Device, int Width, int Height, bool Primary)> Connected() =>
        [.. Forms.Screen.AllScreens.Select(s => (s.DeviceName, s.Bounds.Width, s.Bounds.Height, s.Primary))];

    public static IReadOnlyList<DisplayArea> All() => [.. Forms.Screen.AllScreens.Select(s =>
    {
        double scale = ScaleOf(s.Bounds.X + s.Bounds.Width / 2, s.Bounds.Y + s.Bounds.Height / 2);
        return new DisplayArea(s.DeviceName, s.Bounds.X / scale, s.Bounds.Y / scale, s.Bounds.Width / scale, s.Bounds.Height / scale, s.Primary);
    })];

    /// <summary>The display a window is mostly on; null without a window, or minimised.</summary>
    public static string? DeviceOf(IntPtr hwnd) => hwnd == 0 || GameWindow.IsMinimized(hwnd) ? null : Forms.Screen.FromHandle(hwnd).DeviceName;

    /// <summary>The default spots follow <paramref name="device"/> (null: the primary).</summary>
    public static void UseWorkAreaOf(string? device)
    {
        var s = Forms.Screen.AllScreens.FirstOrDefault(d => d.DeviceName == device);
        if (s is null) { _workArea = null; return; }
        var r = s.WorkingArea;
        double scale = ScaleOf(r.X + r.Width / 2, r.Y + r.Height / 2);
        _workArea = new Rect(r.X / scale, r.Y / scale, r.Width / scale, r.Height / scale);
    }

    static double ScaleOf(int x, int y) =>
        GetDpiForMonitor(MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST), MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;

    const uint MONITOR_DEFAULTTONEAREST = 2;
    const int MDT_EFFECTIVE_DPI = 0;
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [LibraryImport("user32.dll")] private static partial IntPtr MonitorFromPoint(POINT point, uint flags);
    [LibraryImport("shcore.dll")] private static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
