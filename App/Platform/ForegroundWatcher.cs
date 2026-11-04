using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GaugeOverlay.App.Platform;

/// <summary>Raises <see cref="Changed"/> when the game window gains or loses the foreground
/// (EVENT_SYSTEM_FOREGROUND, no polling). Create on a thread with a message loop (the UI thread).</summary>
public sealed partial class ForegroundWatcher : IDisposable
{
    private readonly WinEventProc _proc;   // kept alive: the hook calls it
    private readonly IntPtr _hook;

    /// <summary>True while a window of the game process is in the foreground.</summary>
    public bool IsGameForeground { get; private set; }

    /// <summary>New value of <see cref="IsGameForeground"/>; raised only when it changes.</summary>
    public event Action<bool>? Changed;

    public ForegroundWatcher()
    {
        _proc = (_, _, hwnd, _, _, _, _) => Update(hwnd);
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0,
            Marshal.GetFunctionPointerForDelegate(_proc), 0, 0, WINEVENT_OUTOFCONTEXT);
        GameWindow.NoteForeground(GetForegroundWindow());
        IsGameForeground = IsGame(GetForegroundWindow());
        IsOtherForeground = IsOther(GetForegroundWindow(), IsGameForeground);
    }

    /// <summary>True while another app's window is in front: not the game, nor this app's, nor the shell's (a video alt-tabbed
    /// to, which Desktop Duplication would capture as if it were the game).</summary>
    public bool IsOtherForeground { get; private set; }

    /// <summary>New value of <see cref="IsOtherForeground"/>; raised only when it changes.</summary>
    public event Action<bool>? OtherChanged;

    /// <summary>No window (mid-switch) or one already closed is not another app: the reads wait for the next foreground event to say so.</summary>
    static bool IsOther(IntPtr hwnd, bool game) =>
        hwnd != 0 && !game && GetWindowThreadProcessId(hwnd, out _) != 0 && !GameWindow.IsOursOrShell(hwnd);

    /// <summary>Test mode was switched: whether "the game" is in front changes with it.</summary>
    public void Recheck() => Update(GetForegroundWindow());

    /// <summary>The 설정 window's handle (0: not created yet): it, or a dialog it owns (colour picker), in front counts as
    /// <see cref="IsOwnForeground"/>. Not any window of the app: the first-run window and tray menus don't.</summary>
    public IntPtr OwnWindow { get; set; }

    /// <summary>True while <see cref="OwnWindow"/> or a window it owns is in the foreground.</summary>
    public bool IsOwnForeground { get; private set; }

    /// <summary>New value of <see cref="IsOwnForeground"/>; raised only when it changes.</summary>
    public event Action<bool>? OwnChanged;

    private void Update(IntPtr hwnd)
    {
        GameWindow.NoteForeground(hwnd);
        bool own = hwnd != 0 && OwnWindow != 0 && (hwnd == OwnWindow || GetWindow(hwnd, GW_OWNER) == OwnWindow);
        if (own != IsOwnForeground)
        {
            IsOwnForeground = own;
            OwnChanged?.Invoke(own);
        }
        bool game = IsGame(hwnd);
        if (IsOther(hwnd, game) is var other && other != IsOtherForeground)
        {
            IsOtherForeground = other;
            OtherChanged?.Invoke(other);
        }
        if (game == IsGameForeground) return;
        IsGameForeground = game;
        Changed?.Invoke(game);
    }

    private static bool IsGame(IntPtr hwnd)
    {
        if (GameWindow.TestMode) return hwnd != 0 && hwnd == GameWindow.Target;
        if (hwnd == 0 || GetWindowThreadProcessId(hwnd, out uint pid) == 0) return false;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals(GameWindow.ProcessName, StringComparison.OrdinalIgnoreCase);
        }
        // The process exited: before (ArgumentException) or after (InvalidOperationException) it was opened.
        // Nothing may escape here: this runs inside the native WinEvent callback.
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return false; }
    }

    public void Dispose() => UnhookWinEvent(_hook);

    private const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetWinEventHook(uint min, uint max, IntPtr module, IntPtr proc, uint pid, uint thread, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(IntPtr hook);
    [LibraryImport("user32.dll")] private static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial IntPtr GetWindow(IntPtr hwnd, uint cmd);
    private const uint GW_OWNER = 4;
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
