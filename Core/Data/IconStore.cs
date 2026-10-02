using System.Diagnostics;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>Each 직업각인's latest icon per slot (아이콘으로 표시, <see cref="SkillIcon"/>): icons/&lt;클래스&gt;_&lt;직업각인&gt;/&lt;slot&gt;.bin
/// next to settings.json, one <see cref="Image.ToBytes"/> file per slot. A missing or damaged file is a slot without an icon.</summary>
public sealed class IconStore(string dir)
{
    public static string DefaultDir { get; } = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "icons");

    string Folder(string cls, string sub) => Path.Combine(dir, $"{cls}_{sub}");

    public Dictionary<SlotId, Image> Load(string cls, string sub)
    {
        var icons = new Dictionary<SlotId, Image>();
        var folder = Folder(cls, sub);
        if (!Directory.Exists(folder)) return icons;
        foreach (var file in Directory.GetFiles(folder, "*.bin"))
        {
            if (!Enum.TryParse<SlotId>(Path.GetFileNameWithoutExtension(file), out var slot)) continue;
            try { if (Image.FromBytes(File.ReadAllBytes(file)) is { } img) icons[slot] = img; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[icons] {file}: {ex.Message}"); }
        }
        return icons;
    }

    /// <summary>Writes <paramref name="slot"/>'s icon, replacing the one before (via a .tmp file, so a crash leaves the old one).</summary>
    public void Save(string cls, string sub, SlotId slot, Image icon)
    {
        var folder = Folder(cls, sub);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{slot}.bin");
        File.WriteAllBytes(path + ".tmp", icon.ToBytes());
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public void Clear(string cls, string sub)
    {
        if (Directory.Exists(Folder(cls, sub))) Directory.Delete(Folder(cls, sub), recursive: true);
    }
}
