using System.Diagnostics;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>직업각인 등록: the user's own 초각스 icon per 직업각인 (<see cref="Vision.SubclassMatcher.Art"/>), used where
/// Profiles/subclass ships none: subclass/&lt;클래스&gt;_&lt;직업각인&gt;.bin next to settings.json, an <see cref="Image.ToBytes"/> file.</summary>
public sealed class SubclassIconStore(string dir)
{
    public static string DefaultDir { get; } = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "subclass");

    string PathOf(string cls, string sub) => Path.Combine(dir, $"{cls}_{sub}.bin");

    /// <summary>The registered icon of each of <paramref name="subclasses"/> that has one.</summary>
    public Dictionary<string, Image> Load(string cls, IEnumerable<string> subclasses)
    {
        var icons = new Dictionary<string, Image>();
        foreach (var sub in subclasses)
        {
            var path = PathOf(cls, sub);
            if (!File.Exists(path)) continue;
            try { if (Image.FromBytes(File.ReadAllBytes(path)) is { } img) icons[sub] = img; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[subclass] {path}: {ex.Message}"); }
        }
        return icons;
    }

    /// <summary>Writes the 직업각인's icon, replacing the one before (via a .tmp file).</summary>
    public void Save(string cls, string sub, Image icon)
    {
        Directory.CreateDirectory(dir);
        var path = PathOf(cls, sub);
        File.WriteAllBytes(path + ".tmp", icon.ToBytes());
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
