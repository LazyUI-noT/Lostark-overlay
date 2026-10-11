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
    }

    /// <summary>Test mode was switched: whether "the game" is in front changes with it.</summary>
    public void Recheck() => Update(GetForegroundWindow());

    private void Update(IntPtr hwnd)
    {
        GameWindow.NoteForeground(hwnd);
        bool game = IsGame(hwnd);
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
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
