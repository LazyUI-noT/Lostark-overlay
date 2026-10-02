using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Platform;

/// <summary>Global hotkeys (RegisterHotKey) for the actions in <see cref="HotkeySettings"/>. Create on the UI thread.</summary>
public sealed partial class Hotkeys : IDisposable
{
    private readonly HwndSource _window;
    private readonly List<string> _actions = [];   // index = hotkey id

    /// <summary>Action name: the <see cref="HotkeySettings"/> property's (EditMode, ToggleOverlay, …, Profile1–5).</summary>
    public event Action<string>? Pressed;

    public Hotkeys()
    {
        _window = new HwndSource(new HwndSourceParameters("GaugeOverlay.Hotkeys") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
        _window.AddHook(WndProc);
    }

    /// <summary>Replaces all hotkeys with these. Returns the actions that could not be registered: taken by
    /// another program, or not a valid combination (e.g. "Ctrl+Shift+E"; key names as in <see cref="KeyNames"/>).
    /// Unset ones are skipped. Side mouse buttons aren't valid: nothing reads the mouse but the app's own windows.</summary>
    public IReadOnlyList<string> Apply(HotkeySettings s)
    {
        for (int id = 0; id < _actions.Count; id++) UnregisterHotKey(_window.Handle, id);
        _actions.Clear();
        List<string> failed = [];
        foreach (var (action, combo) in s.All().Where(a => a.Combo != ""))
        {
            if (TryParse(combo, out uint mods, out int vk) && RegisterHotKey(_window.Handle, _actions.Count, mods | MOD_NOREPEAT, (uint)vk))
                _actions.Add(action);
            else
                failed.Add(action);
        }
        return failed;
    }

    private static bool TryParse(string combo, out uint mods, out int vk)
    {
        mods = 0; vk = 0;
        if (!KeyNames.TryParse(combo, out var m, out var key)) return false;
        mods = (uint)m;   // ModifierKeys has the MOD_ALT/CONTROL/SHIFT/WIN bit values
        vk = KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && (int)wParam is var id && id >= 0 && id < _actions.Count)
        {
            handled = true;
            Pressed?.Invoke(_actions[id]);
        }
        return 0;
    }

    public void Dispose()
    {
        for (int id = 0; id < _actions.Count; id++) UnregisterHotKey(_window.Handle, id);
        _window.Dispose();
    }

    private static readonly IntPtr HWND_MESSAGE = -3;
    private const int WM_HOTKEY = 0x312;
    private const uint MOD_NOREPEAT = 0x4000;

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hwnd, int id);
}
