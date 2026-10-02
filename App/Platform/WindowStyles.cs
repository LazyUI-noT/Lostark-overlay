using System.Runtime.InteropServices;

namespace GaugeOverlay.App.Platform;

/// <summary>Extended window styles for the overlay.</summary>
[Flags]
public enum ExStyle
{
    /// <summary>Mouse input passes through. Needs a layered window (WPF AllowsTransparency="True").</summary>
    ClickThrough = 0x20,        // WS_EX_TRANSPARENT
    /// <summary>Clicking or showing the window never takes focus from the game.</summary>
    NoActivate = 0x08000000,    // WS_EX_NOACTIVATE
    /// <summary>Not in the taskbar or Alt+Tab.</summary>
    ToolWindow = 0x80,          // WS_EX_TOOLWINDOW
}

public static partial class WindowStyles
{
    /// <summary>Turns the given extended styles on or off.</summary>
    public static void Set(IntPtr hwnd, ExStyle style, bool on)
    {
        nint cur = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, on ? cur | (nint)style : cur & ~(nint)style);
    }

    /// <summary>Hides the window from all screen capture, including our own Desktop Duplication
    /// (WDA_EXCLUDEFROMCAPTURE, Windows 10 2004+). Returns false if Windows refused.</summary>
    public static bool SetExcludeFromCapture(IntPtr hwnd, bool on) =>
        SetWindowDisplayAffinity(hwnd, on ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);

    private const int GWL_EXSTYLE = -20;
    private const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;

    [LibraryImport("user32.dll")] private static partial nint GetWindowLongPtrW(IntPtr hwnd, int index);
    [LibraryImport("user32.dll")] private static partial nint SetWindowLongPtrW(IntPtr hwnd, int index, nint value);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
