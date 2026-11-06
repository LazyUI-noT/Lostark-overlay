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

    /// <summary>테스트 모드 → 강화 읽기 (reinforced-reader.plan.md): buffs, debuffs, 전분, the boss's HP and 암수 read for compressed
    /// video (cleaned, several ways, checked against the fight; more CPU). Only with <see cref="TestMode"/>; off at start.</summary>
    public static volatile bool Reinforced;

    /// <summary>The 강화 readers are in use: 테스트 모드 with 강화 읽기 on. The game always reads as it is.</summary>
    public static bool ReadsReinforced => TestMode && Reinforced;

    static IntPtr _front, _picked;

    /// <summary>The window last brought to the front, leaving out this app's own windows and the taskbar, desktop and
    /// Start menu: so when test mode is turned on from the tray, it is the window that was in front before.</summary>
    public static IntPtr Front => Volatile.Read(ref _front);

    /// <summary>The window picked from the tray's 테스트 모드 list (zero: follow <see cref="Front"/>).</summary>
    public static IntPtr Picked
    {
        get => Volatile.Read(ref _picked);
        set => Volatile.Write(ref _picked, value);
    }

    /// <summary>The window 테스트 모드 treats as the game: the picked one, else the one last in front.</summary>
    public static IntPtr Target => Picked is var p && p != 0 ? p : Front;

    /// <summary>The window still exists (it may be minimised).</summary>
    public static bool Exists(IntPtr hwnd) => hwnd != 0 && IsWindow(hwnd);

    /// <summary>A window for the 테스트 모드 list.</summary>
    public sealed record Choice(IntPtr Hwnd, string Title);

    /// <summary>Visible top-level windows with a title, front to back, without this app's own, the shell's, tool windows and
    /// hidden (cloaked) store apps. A fullscreen store app (영화 및 TV, checked 2026-10-31) is missed by EnumWindows, hence
    /// FindWindowEx, and is cloaked by the shell as soon as anything else (the tray menu) is in front: the window last in
    /// front and the picked one are listed first even while cloaked.</summary>
    public static IReadOnlyList<Choice> Choices()
    {
        var list = new List<Choice>();
        for (IntPtr h = FindWindowExW(0, 0, 0, 0); h != 0; h = FindWindowExW(0, h, 0, 0))
            if (TitleIfChoosable(h, cloakedOk: false) is { } t) list.Add(new(h, t));
        foreach (var h in new[] { Front, Picked }.Distinct())
            if (h != 0 && list.All(c => c.Hwnd != h) && TitleIfChoosable(h, cloakedOk: true) is { } t) list.Insert(0, new(h, t));
        return list;
    }

    static string? TitleIfChoosable(IntPtr h, bool cloakedOk)
    {
        if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != 0 || IsOursOrShell(h)) return null;
        if ((GetWindowLongPtrW(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return null;
        if (!cloakedOk && DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return null;
        var buf = new char[256];
        int n = GetWindowTextW(h, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : null;
    }

    /// <summary>Restores <paramref name="hwnd"/> if minimised and brings it to the front (a window just picked from the tray).</summary>
    public static void Show(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }

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

    /// <summary>What the capture would show of <paramref name="game"/> with another app in front: the first window over it
    /// counts, leaving out the shell's, the game's own, hidden and minimised ones, click-through ones (other overlays) and our
    /// overlay windows, which never take focus. 설정 counts: Desktop Duplication would capture it. Called at most 2/s, only while
    /// the game isn't in front with 창 밖에서도 읽기 on.</summary>
    public static (GameView View, string? By) ViewOf(IntPtr game)
    {
        if (IsIconic(game) || ClientRect(game) is not { } rect) return (GameView.Minimized, null);
        GetWindowThreadProcessId(game, out uint gamePid);
        var above = GetWindow(game, GW_HWNDPREV);
        for (int i = 0; above != 0 && i < MaxWindowsAbove; i++, above = GetWindow(above, GW_HWNDPREV))
        {
            if (!IsWindowVisible(above) || IsIconic(above)) continue;
            GetWindowThreadProcessId(above, out uint pid);
            long style = GetWindowLongPtrW(above, GWL_EXSTYLE);
            if (pid == gamePid || (style & WS_EX_TRANSPARENT) != 0) continue;
            if (pid == Environment.ProcessId ? (style & WS_EX_NOACTIVATE) != 0 : IsOursOrShell(above)) continue;
            if (DwmGetWindowAttribute(above, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            if (DwmWindowBounds(above, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) != 0) continue;
            if (!BackgroundRead.Covers(rect, new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top))) continue;
            var title = new char[128];
            int n = GetWindowTextW(above, title, title.Length);
            return (GameView.Covered, n > 0 ? new string(title, 0, n) : ClassOf(above));
        }
        return (GameView.Clear, null);
    }

    /// <summary>Minimised: its rect is off every display, so it says nothing about where it stands.</summary>
    public static bool IsMinimized(IntPtr hwnd) => IsIconic(hwnd);

    /// <summary>Restores <paramref name="hwnd"/> if minimised, leaving the front as it is.</summary>
    public static void Restore(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_SHOWNOACTIVATE);
    }

    /// <summary>The main window of the first process called <paramref name="process"/>, or zero.</summary>
    public static IntPtr MainWindowOf(string process)
    {
        var procs = Process.GetProcessesByName(process);
        try { return procs.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != 0); }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    /// <summary>The main window of the first running game process (in test mode <see cref="Target"/>), or zero.</summary>
    public static IntPtr Find() => Find(TestMode);

    /// <summary><see cref="Find()"/> for the mode the caller already read (the engine reads it once per step).</summary>
    public static IntPtr Find(bool test)
    {
        if (test) return Target is var target && Exists(target) ? target : 0;
        return MainWindowOf(ProcessName);
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

    /// <summary>The window's class name (for logs).</summary>
    public static string ClassOf(IntPtr hwnd)
    {
        var name = new char[64];
        int n = GetClassNameW(hwnd, name, name.Length);
        return n > 0 ? new string(name, 0, n) : "?";
    }

    /// <summary>A window of this app, or the taskbar, desktop or Start menu: neither the game nor another app.</summary>
    internal static bool IsOursOrShell(IntPtr hwnd)
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

    private const uint MONITOR_DEFAULTTONEAREST = 2, GW_OWNER = 4, GW_HWNDPREV = 3, DWMWA_CLOAKED = 14, DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int GWL_EXSTYLE = -20, SW_RESTORE = 9, SW_SHOWNOACTIVATE = 4;
    private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;
    /// <summary>GetWindow's walk can loop if the order changes under it.</summary>
    private const int MaxWindowsAbove = 2048;

    /// <summary>Top-level windows in z-order: parent, class and title all zero (no filter).</summary>
    [LibraryImport("user32.dll")] private static partial IntPtr FindWindowExW(IntPtr parent, IntPtr after, IntPtr cls, IntPtr title);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int cmd);
    [LibraryImport("user32.dll")] private static partial IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [LibraryImport("user32.dll")] private static partial long GetWindowLongPtrW(IntPtr hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowTextW(IntPtr hwnd, [Out] char[] text, int max);
    [LibraryImport("dwmapi.dll")] private static partial int DwmGetWindowAttribute(IntPtr hwnd, uint attr, out int value, int size);
    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static partial int DwmWindowBounds(IntPtr hwnd, uint attr, out RECT value, int size);

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
