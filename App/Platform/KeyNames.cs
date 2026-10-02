using System.Windows.Input;

namespace GaugeOverlay.App.Platform;

/// <summary>The one key-name format, shared by Settings.Keys, HotkeySettings, <see cref="Hotkeys"/>
/// and the settings UI (they must match exactly or keys never match): WPF <see cref="Key"/> names ("E", "D1", "Oem3"),
/// sides merged ("Shift", "Ctrl", "Alt"), held modifiers as a "Ctrl+Alt+Shift+" prefix.
/// <see cref="Display"/> turns a stored name into what the user sees ("D1" → "1").</summary>
public static class KeyNames
{
    public static string Of(Key k) => k switch
    {
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftCtrl or Key.RightCtrl => "Ctrl",
        Key.LeftAlt or Key.RightAlt => "Alt",
        _ => k.ToString(),
    };

    public static string OfVirtualKey(int vk) => Of(KeyInterop.KeyFromVirtualKey(vk));

    public static bool IsModifier(string name) => name is "Shift" or "Ctrl" or "Alt";

    public static string Combo(bool ctrl, bool alt, bool shift, string key) =>
        (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "") + key;

    /// <summary>"Ctrl+Shift+E" → modifiers and key. False when a prefix isn't a modifier or the last part isn't a key.</summary>
    public static bool TryParse(string combo, out ModifierKeys mods, out Key key)
    {
        mods = ModifierKeys.None;
        key = Key.None;
        var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (var p in parts[..^1])
        {
            var m = p.ToLowerInvariant() switch
            {
                "ctrl" => ModifierKeys.Control, "shift" => ModifierKeys.Shift, "alt" => ModifierKeys.Alt, "win" => ModifierKeys.Windows,
                _ => ModifierKeys.None,
            };
            if (m == ModifierKeys.None) return false;
            mods |= m;
        }
        return Enum.TryParse(parts[^1], true, out key) && key != Key.None;
    }

    /// <summary>For showing only: "Ctrl+D1" → "Ctrl+1", "Oem3" → "`", "Capital" → "CapsLock".</summary>
    public static string Display(string stored) => string.Join("+", stored.Split('+').Select(Friendly));

    static string Friendly(string part)
    {
        if (int.TryParse(part, out _) || !Enum.TryParse<Key>(part, out var k)) return part;
        return k switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (int)(k - Key.NumPad0),
            Key.Oem3 => "`", Key.OemMinus => "-", Key.OemPlus => "=", Key.Oem4 => "[", Key.Oem6 => "]", Key.Oem5 => "\\",
            Key.Oem1 => ";", Key.Oem7 => "'", Key.OemComma => ",", Key.OemPeriod => ".", Key.Oem2 => "/",
            Key.Capital => "CapsLock", Key.Return => "Enter", Key.Back => "Backspace", Key.Escape => "Esc",
            Key.Prior => "PageUp", Key.Next => "PageDown",
            _ => part,
        };
    }
}
