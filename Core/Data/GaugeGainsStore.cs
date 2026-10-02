using System.Diagnostics;
using System.Text.Json;

namespace GaugeOverlay.Core.Data;

/// <summary>The learned gains (<see cref="GaugeGains"/>) of every character, keyed "클래스 · 직업각인", in gains.json next
/// to settings.json. A missing or damaged file is nothing learned. Saves write gains.json.tmp, then move it over.</summary>
public sealed class GaugeGainsStore(string path)
{
    public static string DefaultPath { get; } = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "gains.json");

    public Dictionary<string, IReadOnlyDictionary<string, Gain>> Load()
    {
        try
        {
            if (!File.Exists(path)) return [];
            var all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Gain>>>(File.ReadAllText(path), SettingsStore.Json);
            return all?.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, Gain>)kv.Value) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.WriteLine($"[gains] {path}: {ex.Message}");
            return [];
        }
    }

    public void Save(IReadOnlyDictionary<string, IReadOnlyDictionary<string, Gain>> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, SettingsStore.Json));
        File.Move(tmp, path, overwrite: true);
    }
}
