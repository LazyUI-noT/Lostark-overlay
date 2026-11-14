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

    /// <summary>A copy sharing nothing with <paramref name="s"/>.</summary>
    public static Settings Copy(Settings s) => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s, Json), Json) ?? new Settings();

    /// <summary>[Test]'s file, beside settings.json: 테스트 모드's own options and places.</summary>
    public static string TestPath { get; } = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(DefaultPath)!, "settings.test.json");
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
        return new Settings { HomeworkWithoutKey = true };   // reset, but not a new install
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
        if (!s.CooldownLooksMoved) ClassProfileMigration.MoveCooldownViews(s);   // after RepairClasses: no null entries
        PeerSlots(s);
        FiveProfiles(s);
        // Hand-edited, or saved by the old 70–130 slider: a preset, unless 값 직접 조정 is on (it saves any whole % in FitAdjust's range).
        if (s.ManualHud is { } hud) s.ManualHud = s.FineSizes && hud == Math.Round(hud) ? Math.Clamp(hud, Vision.FitAdjust.MinHud, Vision.FitAdjust.MaxHud) : Settings.NearestHud(hud);
        foreach (var (key, f) in s.Adjusts.ToList())   // a hand edit the debug window couldn't have made
            if (f is null || !double.IsFinite(f.Hud) || f.Hud is < 50 or > 250 || !double.IsFinite(f.NudgeX) || !double.IsFinite(f.NudgeY)) s.Adjusts.Remove(key);
            else if (f.BossSize is not (null or >= Vision.FitAdjust.MinBoss and <= Vision.FitAdjust.MaxBoss)) s.Adjusts[key] = f with { BossSize = null };
        s.Cores ??= [];
        if (s.AutoSubclass) (s.ClassDetect, s.AutoSubclass) = (ClassDetectMode.자동, false);   // merged into 클래스 인식 자동 (user, 2026-11-05)
        if (!s.RecordKeepMigrated) (s.RecordKeep, s.RecordKeepMigrated) = (s.RecordKeep == OldRecordKeep ? new Settings().RecordKeep : s.RecordKeep, true);
        foreach (var (key, c) in s.Cores.ToList())   // CoreChoiceJson repaired what was malformed inside an entry; here: levels outside 1–3, an entry that was not an object
            if (c is null) s.Cores.Remove(key); else s.Cores[key] = c.Clamped();
        s.FillSuraCore();   // after the repair: a broken saved entry was dropped and starts on 322 like a new one
        s.Amsu ??= new();
        s.Amsu.Clamp();
        s.ReadsPerSecond = ReadSteps.Clamp(s.ReadsPerSecond);
        s.DrawsPerSecond = DrawSteps.Clamp(s.DrawsPerSecond);
        s.Judgment ??= new();
        s.Judgment.Clamp();
        s.StackSkills ??= [];
        foreach (var (key, slots) in s.StackSkills.ToList())   // a hand edit: null, or a name that isn't a slot
            if (slots is null) s.StackSkills.Remove(key);
            else s.StackSkills[key] = [.. s.StackSkillsOf(key).Select(x => x.ToString())];
        (s.ManualScreen, s.DetectedScreen) = (Screenable(s.ManualScreen), Screenable(s.DetectedScreen));
        s.Debuffs.BossCount = Math.Clamp(s.Debuffs.BossCount, 1, Vision.Geometry.MaxBossSlots);
        s.Buffs.Migrate();   // per-bar 자동 → one 자동 (user, 2026-11-12)
        s.Buffs.Count = Math.Clamp(s.Buffs.Count, Vision.Geometry.MinBuffBoxCount, Vision.Geometry.MaxBuffBoxCount);   // a hand edit
        if (s.Debuffs.BossIconSize is < Vision.FitAdjust.MinBuffSize or > Vision.FitAdjust.MaxBuffSize) s.Debuffs.BossIconSize = Vision.Geometry.RefBossSize;
        s.Raids ??= new();
        s.Raids.Normalize(s.Debuffs);
        s.Meter ??= new();
        s.Meter.Normalize();
        foreach (var p in s.Profiles) p.Fill(s);   // after 암수, 단심 and the meter are repaired: an older file's profiles take them
        s.UnshareLooks();   // an older file's 모든 위치 프로필에 적용 marks: their value into every profile, none shared now
        s.MigrateClasses();   // a 0.22 file: its 직업각인 spots become class entries, its 게이지 bars every profile's (after the fill and marks: entries clone them)
        s.BossBook ??= [];
        s.MutedNotices ??= [];   // a hand edit: null
        if (s.RailSeen is not null) s.RailSeen = s.RailSeen.ToDictionary(kv => kv.Key, kv => kv.Value ?? []);   // a hand edit: a null list
        s.Homework ??= new();
        s.RaidCapsules ??= new();
        s.RaidCapsules.Colors ??= [];
        s.Homework.Order = [.. (s.Homework.Order ?? []).Select(n => n ?? "")];   // a hand edit or an odd save: CardOrder reads every name
        foreach (var (hp, e) in s.BossBook.ToList())   // a hand edit: no name, or a null entry
            if (e is null || string.IsNullOrWhiteSpace(e.Name)) s.BossBook.Remove(hp);
        s.FavoriteClasses = Distinct(s.FavoriteClasses);
        s.FavoriteSettings = Distinct([.. (s.FavoriteSettings ?? []).Select(id => FeatureMap.MigrateSettingId(id ?? ""))]);   // 2026-11-08 renames
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
