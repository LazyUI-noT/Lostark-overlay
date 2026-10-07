using System.Diagnostics;
using System.Runtime.InteropServices;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Platform;

/// <summary>Finds the game window and measures its drawn area. All rects are physical virtual-screen pixels
/// (the app is PerMonitorV2).</summary>
public static partial class GameWindow
{
    public const string ProcessName = "LOSTARK";

    /// <summary>테스트 모드: <see cref="Front"/> stands in for the game (a video of it, for trying detection), and the
    /// game process isn't looked for.</summary>
    public static volatile bool TestMode;

    static IntPtr _front;

    /// <summary>The window last brought to the front, leaving out this app's own windows and the taskbar, desktop and
    /// Start menu: so when test mode is turned on from the tray, it is the window that was in front before.</summary>
    public static IntPtr Front => Volatile.Read(ref _front);

    /// <summary>Called with each new foreground window (ForegroundWatcher).</summary>
    public static void NoteForeground(IntPtr hwnd)
    {
        if (hwnd == 0 || GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == Environment.ProcessId) return;
        var name = new char[64];
        int n = GetClassNameW(hwnd, name, name.Length);
        if (n <= 0 || Shell.Contains(new string(name, 0, n))) return;   // no class name: the window just closed
        Volatile.Write(ref _front, hwnd);
    }

    static readonly HashSet<string> Shell = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "#32768"];

    /// <summary>The main window of the first running game process (in test mode <see cref="Front"/>), or zero.</summary>
    public static IntPtr Find()
    {
        if (TestMode) return Front is var front && front != 0 && IsWindow(front) ? front : 0;
        var procs = Process.GetProcessesByName(ProcessName);
        try { return procs.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != 0); }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    /// <summary>Gives the game the foreground back when one of this app's windows has it (edit mode took it) or the taskbar
    /// does (the tray menu was just used), and only then: another app the user switched to keeps it. Windows allows the
    /// switch because this process had the last input. The overlay shows only while the game is in front.</summary>
    public static void TakeBackForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || !IsOursOrShell(hwnd)) return;   // none in front: mid-switch to something the user chose
        Activate();
    }

    static bool IsOursOrShell(IntPtr hwnd)
    {
        if (GetWindowThreadProcessId(hwnd, out uint pid) == 0) return false;
        if (pid == Environment.ProcessId) return true;
        var name = new char[64];
        int n = GetClassNameW(hwnd, name, name.Length);
        return n > 0 && Shell.Contains(new string(name, 0, n));
    }

    /// <summary>Brings the game to the front, when the user asked for something that needs it (스킬창 맞추기: the taskbar
    /// covers the skill bar while the tray menu or 설정 has the foreground). Nothing if it isn't running.</summary>
    public static void Activate()
    {
        if (Find() is var hwnd && hwnd != 0) SetForegroundWindow(hwnd);
    }

    /// <summary>Client area in screen pixels, or null if the window is gone or minimised.</summary>
    public static PixelRect? ClientRect(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var r) || r.Right <= 0 || r.Bottom <= 0) return null;
        var origin = new POINT();
        if (!ClientToScreen(hwnd, ref origin)) return null;
        return new(origin.X, origin.Y, r.Right, r.Bottom);
    }

    /// <summary>HMONITOR the window is mostly on (nearest if off-screen).</summary>
    public static IntPtr MonitorOf(IntPtr hwnd) => MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

    /// <summary>Monitor rect in screen pixels.</summary>
    public static PixelRect MonitorBounds(IntPtr monitor)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(monitor, ref mi);
        var r = mi.rcMonitor;
        return new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);
    [LibraryImport("user32.dll")] private static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassNameW(IntPtr hwnd, [Out] char[] name, int max);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hwnd);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hwnd, out RECT rect);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);
}
