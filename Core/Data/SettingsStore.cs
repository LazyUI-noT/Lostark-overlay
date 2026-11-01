using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>Reads and writes <see cref="Settings"/> as indented JSON (PLAN §6). Saves are atomic: write
/// settings.json.tmp, then swap it in with File.Replace, keeping the previous file as settings.json.bak.</summary>
public sealed class SettingsStore(string path)
{
    public static string DefaultPath { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GaugeOverlay", "settings.json");

    /// <summary>Enum names (Korean) as keys and values; missing properties and dictionary entries keep their
    /// defaults (Populate), so a file from an older version loads with the new settings at default.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,   // Overlay.Left/Top = NaN
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    };

    public string Path { get; } = path;
    private string Tmp => Path + ".tmp";
    private string Bak => Path + ".bak";

    /// <summary>Loads settings.json, else settings.json.bak, else defaults. <paramref name="wasReset"/> is true
    /// when a file existed but neither could be parsed, so the UI shows its one "settings were reset" notice; the
    /// unparsable files are first copied to &lt;name&gt;.broken-&lt;time&gt;, so the next save loses nothing.
    /// A file that stays locked (antivirus, backup, another instance) is retried briefly, then the IOException is
    /// thrown rather than starting from defaults that would overwrite it.</summary>
    public Settings Load(out bool wasReset)
    {
        wasReset = false;
        var broken = new List<string>();
        foreach (var file in new[] { Path, Bak })
        {
            if (!File.Exists(file)) continue;
            string text = Retry(() => File.ReadAllText(file));
            try
            {
                if (JsonSerializer.Deserialize<Settings>(text, Json) is { } s)
                {
                    s.Version = Settings.CurrentVersion;
                    return Normalize(s);
                }
            }
            catch (Exception e) when (e is JsonException or NotSupportedException) { }
            broken.Add(file);
        }
        if (broken.Count == 0) return new Settings();

        wasReset = true;
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        foreach (var file in broken)
            try { File.Copy(file, $"{file}.broken-{stamp}", overwrite: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // best effort
        return new Settings();
    }

    /// <summary>A hand edit can leave a class entry, or its 나 or 파티원 part, null: the entry is dropped, the part made default.</summary>
    static void RepairClasses(Settings s)
    {
        s.Classes ??= [];
        foreach (var (key, c) in s.Classes.ToList())
            if (c is null) s.Classes.Remove(key);
            else (c.Mine, c.Members) = (c.Mine ?? new(), c.Members ?? new());
    }

    /// <summary>A typed or remembered 해상도 a screen can have (the game's smallest is 1280×720; 16384 covers any monitor), else null.</summary>
    /// <summary>A hand-edited ★ list: no blanks, each once, order kept.</summary>
    static List<string> Distinct(List<string>? list) => [.. (list ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()];

    static ManualScreen? Screenable(ManualScreen? s) =>
        s is { Width: >= 640 and <= 16384, Height: >= 480 and <= 16384 } ? s : null;

    /// <summary>The repairs every loaded file gets (hand edits, lists saved by older beta builds), pure. Files from
    /// before the beta (settings version below 4) are no longer migrated: every beta release saved version 4.</summary>
    const int OldRecordKeep = 20;   // 전투 기록's default before it went per character

    internal static Settings Normalize(Settings s)
    {
        RepairClasses(s);
        PeerSlots(s);
        FiveProfiles(s);
        if (s.ManualHud is { } hud) s.ManualHud = Settings.NearestHud(hud);   // hand-edited, or saved by the old 70–130 slider
        foreach (var (key, f) in s.Adjusts.ToList())   // a hand edit the debug window couldn't have made
            if (f is null || !double.IsFinite(f.Hud) || f.Hud is < 50 or > 250 || !double.IsFinite(f.NudgeX) || !double.IsFinite(f.NudgeY)) s.Adjusts.Remove(key);
            else if (f.BossSize is not (null or >= Vision.FitAdjust.MinBoss and <= Vision.FitAdjust.MaxBoss)) s.Adjusts[key] = f with { BossSize = null };
        s.Cores ??= [];
        if (!s.RecordKeepMigrated) (s.RecordKeep, s.RecordKeepMigrated) = (s.RecordKeep == OldRecordKeep ? new Settings().RecordKeep : s.RecordKeep, true);
        foreach (var (key, c) in s.Cores.ToList())   // CoreChoiceJson repaired what was malformed inside an entry; here: levels outside 1–3, an entry that was not an object
            if (c is null) s.Cores.Remove(key); else s.Cores[key] = c.Clamped();
        s.Amsu ??= new();
        s.Amsu.Clamp();
        s.Judgment ??= new();
        s.Judgment.Characters ??= [];
        if (!JudgmentSettings.FontSizes.Contains(s.Judgment.FontSize)) s.Judgment.FontSize = 100;   // a hand edit
        s.Judgment.Opacity = double.IsFinite(s.Judgment.Opacity) ? Math.Clamp(s.Judgment.Opacity, JudgmentSettings.MinOpacity, 1) : 1;
        s.StackSkills ??= [];
        foreach (var (key, slots) in s.StackSkills.ToList())   // a hand edit: null, or a name that isn't a slot
            if (slots is null) s.StackSkills.Remove(key);
            else s.StackSkills[key] = [.. s.StackSkillsOf(key).Select(x => x.ToString())];
        (s.ManualScreen, s.DetectedScreen) = (Screenable(s.ManualScreen), Screenable(s.DetectedScreen));
        s.Buffs.Count = Math.Clamp(s.Buffs.Count, Vision.Geometry.MinBuffBoxCount, Vision.Geometry.MaxBuffBoxCount);   // a hand edit
        if (s.Debuffs.BossIconSize is < Vision.FitAdjust.MinBuffSize or > Vision.FitAdjust.MaxBuffSize) s.Debuffs.BossIconSize = Vision.Geometry.RefBossSize;
        s.Raids ??= new();
        s.Raids.Normalize(s.Debuffs);
        s.Meter ??= new();
        s.Meter.Normalize();
        s.BossBook ??= [];
        foreach (var (hp, e) in s.BossBook.ToList())   // a hand edit: no name, or a null entry
            if (e is null || string.IsNullOrWhiteSpace(e.Name)) s.BossBook.Remove(hp);
        s.FavoriteClasses = Distinct(s.FavoriteClasses);
        s.FavoriteSettings = Distinct(s.FavoriteSettings);
        return s;
    }

    /// <summary>Exactly one entry per other party member. Files saved while the list was added to on load hold extra
    /// entries; the first three are the latest.</summary>
    static void PeerSlots(Settings s)
    {
        var slots = s.Party.ShowOthersBySlot;
        const int Peers = Net.Membership.PartySize - 1;
        if (slots.Count > Peers) slots.RemoveRange(Peers, slots.Count - Peers);
        while (slots.Count < Peers) slots.Add(new());
    }

    /// <summary>Exactly <see cref="OverlayProfile.Count"/> profiles, one of them active.</summary>
    static void FiveProfiles(Settings s)
    {
        if (s.Profiles.Count > OverlayProfile.Count) s.Profiles.RemoveRange(OverlayProfile.Count, s.Profiles.Count - OverlayProfile.Count);
        while (s.Profiles.Count < OverlayProfile.Count) s.Profiles.Add(new());
        s.ActiveProfile = Math.Clamp(s.ActiveProfile, 0, OverlayProfile.Count - 1);
        foreach (var p in s.Profiles)   // a hand-edited area would throw where the editor lays the picture out
            if (p.Shot is { } a && !ScreenShots.SizeOk(a.Width, a.Height)) p.Shot = null;
    }

    /// <summary>Writes the settings atomically. A crash at any point leaves settings.json or .bak loadable. A transient
    /// lock is retried briefly; after that the IOException reaches the caller.</summary>
    public void Save(Settings settings) => Retry(() =>
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        using (var fs = new FileStream(Tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(fs, settings, Json);
            fs.Flush(flushToDisk: true);
        }
        if (File.Exists(Path)) File.Replace(Tmp, Path, Bak);
        else File.Move(Tmp, Path);
        return 0;
    });

    private static T Retry<T>(Func<T> io)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return io(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 5) { Thread.Sleep(100); }
        }
    }
}

/// <summary>Saves about 0.5 s after the last change, so dragging a slider doesn't write on every step.
/// Call <see cref="Changed"/> on every edit and poll <see cref="Due"/> from a UI timer; save when it returns true.</summary>
public sealed class SaveDebounce
{
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(0.5);
    private TimeSpan? _dueAt;

    public void Changed(TimeSpan now) => _dueAt = now + Delay;

    /// <summary>True once, when <see cref="Delay"/> has passed since the last change.</summary>
    public bool Due(TimeSpan now)
    {
        if (_dueAt is not { } due || now < due) return false;
        _dueAt = null;
        return true;
    }

    /// <summary>The caller saves now instead (leaving edit mode, exit): drops the pending save.</summary>
    public void Flush() => _dueAt = null;
}
