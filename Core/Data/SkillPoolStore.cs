using System.Diagnostics;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Data;

/// <summary>Each 직업각인's pool of 일반 slot crops (스킬 등록, <see cref="SkillPool"/>): skills/&lt;클래스&gt;_&lt;직업각인&gt;/*.bin next to
/// settings.json, one <see cref="Image.ToBytes"/> file per skill. A missing or damaged file is a skill not submitted.</summary>
public sealed class SkillPoolStore(string dir)
{
    public static string DefaultDir { get; } = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "skills");

    string Folder(string cls, string sub) => Path.Combine(dir, $"{cls}_{sub}");

    /// <summary>The pool of each of <paramref name="subclasses"/> of <paramref name="cls"/> (empty when none was submitted).</summary>
    public Dictionary<string, IReadOnlyList<Image>> Load(string cls, IEnumerable<string> subclasses) =>
        subclasses.ToDictionary(s => s, s => (IReadOnlyList<Image>)LoadOne(cls, s));

    List<Image> LoadOne(string cls, string sub)
    {
        var list = new List<Image>();
        var folder = Folder(cls, sub);
        if (!Directory.Exists(folder)) return list;
        foreach (var file in Directory.GetFiles(folder, "*.bin"))
        {
            try { if (Image.FromBytes(File.ReadAllBytes(file)) is { } img) list.Add(img); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[skills] {file}: {ex.Message}"); }
        }
        return list;
    }

    /// <summary>Adds the skills of <paramref name="ready"/> not in the pool yet. Returns how many were added and the pool's size.</summary>
    public (int Added, int Total) Add(string cls, string sub, IReadOnlyList<Image> ready)
    {
        var pool = LoadOne(cls, sub);
        var fresh = SkillPool.Fresh(pool, ready);
        var folder = Folder(cls, sub);
        Directory.CreateDirectory(folder);
        int added = 0;
        for (int i = 0; i < ready.Count; i++)
        {
            if (!fresh[i]) continue;
            File.WriteAllBytes(Path.Combine(folder, Guid.NewGuid().ToString("N") + ".bin"), ready[i].ToBytes());
            added++;
        }
        return (added, pool.Count + added);
    }

    public void Clear(string cls, string sub)
    {
        if (Directory.Exists(Folder(cls, sub))) Directory.Delete(Folder(cls, sub), recursive: true);
    }
}
