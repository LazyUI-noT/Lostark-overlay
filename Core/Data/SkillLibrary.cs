using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Data;

/// <summary>A class's skill in the shipped library (skill-icons.plan.md): Korean name, kind (the slot group it sits in: 일반스킬,
/// 초각스, 각성기 for both 각성기 and 초각성기), its icon on Smilegate's CDN ("gl_skill/gl_skill_01_30.png"), the 직업각인 a
/// 초각스 belongs to when known, and its <see cref="IconPrint"/>s (whole, frame-trimmed).</summary>
public sealed record LibrarySkill(string Class, string Name, SlotGroup Kind, string Icon, string? Subclass, IReadOnlyList<IconPrint> Prints);

/// <summary>Profiles/skills/&lt;클래스&gt;.json, generated from the dev data in reference/skill-icons (SkillLibraryTests.
/// Build_skill_library_when_requested). Each class is read on first use; a missing or damaged file is a class without skills.
/// Safe to read from several threads.</summary>
public sealed class SkillLibrary
{
    sealed record SkillFile(string Name, SlotGroup Kind, string Icon, string? Subclass, string[] Prints);
    sealed record ClassFile(string Class, SkillFile[] Skills);

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    readonly string _dir;
    readonly HashSet<string> _known;
    readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<LibrarySkill>>> _classes = new();
    readonly Lazy<IReadOnlySet<string>> _shared;

    SkillLibrary(string dir, IReadOnlyList<string> classes)
    {
        _dir = dir;
        Classes = classes;
        _known = [.. classes];
        _shared = new(() => Classes.SelectMany(c => Skills(c).Select(s => s.Icon).Distinct()).GroupBy(i => i)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet());
    }

    static readonly Lazy<SkillLibrary> ShippedLibrary = new(() => Load(Path.Combine(AppContext.BaseDirectory, "Profiles", "skills")));

    /// <summary>The library the app ships (Profiles/skills beside the exe), read once.</summary>
    public static SkillLibrary Shipped => ShippedLibrary.Value;

    /// <summary>The classes with a file, by file name.</summary>
    public IReadOnlyList<string> Classes { get; }

    public static SkillLibrary Load(string dir)
    {
        try { return new(dir, Directory.Exists(dir) ? [.. Directory.GetFiles(dir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).Order()] : []); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[skills] {dir}: {e.Message}");
            return new(dir, []);
        }
    }

    /// <summary>The class's skills of one kind; empty for a class without a file (a name never becomes a path unless listed).</summary>
    public IReadOnlyList<LibrarySkill> Pool(string cls, SlotGroup kind) => [.. Skills(cls).Where(s => s.Kind == kind)];

    /// <summary>An icon file more than one class uses: it can't tell the classes apart.</summary>
    public bool Shared(string icon) => _shared.Value.Contains(icon);

    IReadOnlyList<LibrarySkill> Skills(string cls) => _known.Contains(cls) ? _classes.GetOrAdd(cls, c => new(() => Read(c))).Value : [];

    IReadOnlyList<LibrarySkill> Read(string cls)
    {
        var path = Path.Combine(_dir, cls + ".json");
        if (!File.Exists(path)) return [];
        try
        {
            var file = JsonSerializer.Deserialize<ClassFile>(File.ReadAllText(path), Json);
            // Shipped data, still checked: an entry without a name, icon or prints is left out, not a crash.
            return file?.Skills is not { } skills ? []
                : [.. skills.Where(s => s is { Name.Length: > 0, Icon.Length: > 0, Prints.Length: > 0 } && Enum.IsDefined(s.Kind))
                    .Select(s => new LibrarySkill(cls, s.Name, s.Kind, s.Icon, s.Subclass,
                        [.. s.Prints.Select(p => IconPrint.FromBytes(Convert.FromBase64String(p ?? "")))]))];
        }
        // Anything a damaged file throws: the Lazy would otherwise keep rethrowing it for the class and for Shared.
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Trace.WriteLine($"[skills] {path}: {e.Message}");
            return [];
        }
    }

    /// <summary>Writes a class's file (the generator).</summary>
    public static void Save(string dir, string cls, IEnumerable<LibrarySkill> skills)
    {
        Directory.CreateDirectory(dir);
        var file = new ClassFile(cls,
            [.. skills.Select(s => new SkillFile(s.Name, s.Kind, s.Icon, s.Subclass, [.. s.Prints.Select(p => Convert.ToBase64String(p.ToBytes()))]))]);
        var path = Path.Combine(dir, cls + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(file, Json));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    /// <summary>A name chip's text: the first letter of each of the first two words ("차지 스팅거" → "차스"), the first two of a
    /// one-word name ("오라토리오" → "오라"). Each word from its first letter; words without one (":") are skipped.</summary>
    public static string Short(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => new string([.. w.SkipWhile(c => !char.IsLetter(c))]))
            .Where(w => w.Length > 0).ToArray();
        return words.Length switch
        {
            0 => name.Trim(),
            1 => new string([.. words[0].Take(2)]),
            _ => $"{words[0][0]}{words[1][0]}",
        };
    }
}
