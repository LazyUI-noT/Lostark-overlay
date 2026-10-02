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
                    int fileVersion = s.Version;
                    s.Version = Settings.CurrentVersion;   // migrations from older versions go here
                    if (s.Overlay.Scale is { } k)
                    {
                        var o = s.Overlay;
                        (o.Width, o.Height, o.PartyWidth, o.PartyHeight, o.Scale) = (k, k, k, k, null);
                    }
                    SplitPartyWindow(s);
                    MoveIntoClassProfiles(s);
                    MoveEffectsIntoClasses(s);
                    if (fileVersion < 2) ClassProfiles.MarkOwnWhereDifferent(s);
                    // Before the history the active character was the one last played: it starts the history.
                    if (fileVersion < 3 && s.ActiveCharacter is { } a && s.Characters.GetValueOrDefault(a) is { Class: { Length: > 0 } cls, Subclass: { Length: > 0 } sub })
                        s.SubclassHistory.TryAdd(cls, sub);
                    // Before 빛 색 직접 고르기 a chosen glow colour always showed.
                    if (fileVersion < 4)
                        foreach (var c in s.Classes.Values)
                            c.Mine.CustomGlow = c.Mine.PulseColor.Values.Any(v => !string.Equals(v, "#FFFFFF", StringComparison.OrdinalIgnoreCase));
                    FiveProfiles(s);
                    if (s.ManualHud is { } hud) s.ManualHud = Math.Clamp(hud, 70, 130);   // a hand-edited size: the 설정 slider's range
                    (s.ManualScreen, s.DetectedScreen) = (Screenable(s.ManualScreen), Screenable(s.DetectedScreen));
                    if (s.Overlay.AwakeningChangedColor.Equals("#FF7F27", StringComparison.OrdinalIgnoreCase))
                        s.Overlay.AwakeningChangedColor = new OverlaySettings().AwakeningChangedColor;   // 1.x default → red
                    return s;
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

    /// <summary>A typed or remembered 해상도 a screen can have (the game's smallest is 1280×720; 16384 covers any monitor), else null.</summary>
    static ManualScreen? Screenable(ManualScreen? s) =>
        s is { Width: >= 640 and <= 16384, Height: >= 480 and <= 16384 } ? s : null;

    /// <summary>About one party member's row at box height 1.0, so members moved out of 1.x's shared party window line
    /// up where their rows were.</summary>
    const double PartyRow = 60;

    /// <summary>1.x drew every party member in one party window; each now has its own. The old window's size goes to
    /// every member, its spot to the first, and the others stack below it.</summary>
    static void SplitPartyWindow(Settings s)
    {
        var o = s.Overlay;
        var slots = s.Party.ShowOthersBySlot;
        // Files saved while the list was added to on load hold extra entries; the first three are the latest.
        const int Peers = Net.Membership.MaxMembers - 1;
        if (slots.Count > Peers) slots.RemoveRange(Peers, slots.Count - Peers);
        while (slots.Count < Peers) slots.Add(new());
        for (int i = 0; i < slots.Count; i++)
        {
            var d = slots[i];
            if (o.PartyWidth is { } w) d.Width = w;
            if (o.PartyHeight is { } h) d.Height = h;
            if (o.PartyLeft is { } left) d.Left = left;
            if (o.PartyTop is { } top) d.Top = top + i * PartyRow * (d.Height ?? 1);   // NaN stays NaN
        }
        (o.PartyLeft, o.PartyTop, o.PartyWidth, o.PartyHeight) = (null, null, null, null);
    }

    /// <summary>Before per-class settings, you had one display, layout and set of colours for all classes, and party
    /// members one per slot with one colour mode. They become the template class (the first slot's for members), which
    /// every class with settings starts from; colours set per class or per character go to that class's 개인 설정.</summary>
    static void MoveIntoClassProfiles(Settings s)
    {
        var (o, party) = (s.Overlay, s.Party);
        var first = party.ShowOthersBySlot.FirstOrDefault() ?? new();
        if (party.SupportSlot is { } slot)
            (party.SupportFixed, party.SupportNumber) = (slot >= 0, slot >= 0 ? PartySlots.Numbers(1)[Math.Min(slot, 2)] : 2);
        bool old = o.GroupVisible is not null || o.GroupColor is not null || o.Layout is not null || o.HiddenSlots is not null
            || party.ColorMode is not null || first.Groups is not null || first.Gauge is not null || first.Width is not null
            || first.Layout is not null || s.Classes.Values.Any(c => c.Custom is not null) || s.Characters.Values.Any(c => c.Colors is not null);
        if (old)
        {
            var t = ClassProfiles.Edit(s, ClassProfiles.Template);
            foreach (var (g, v) in o.GroupVisible ?? []) t.Mine.Groups[g] = v;
            t.Mine.HiddenSlots = o.HiddenSlots ?? [];
            t.Mine.Layout = o.Layout;
            Personal(t.Mine, (o.GroupColor ?? []).Where(c => !c.Value.Equals(ClassProfiles.BuiltIn.GetValueOrDefault(c.Key), StringComparison.OrdinalIgnoreCase)));
            foreach (var (g, v) in first.Groups ?? []) t.Members.Groups[g] = v;
            (t.Members.Gauge, t.Members.Width, t.Members.Height) = (first.Gauge ?? true, first.Width ?? 1, first.Height ?? 1);
            t.Members.Layout = first.Layout ?? o.Layout;   // a member's window followed yours
            t.Members.HiddenSlots = [.. t.Mine.HiddenSlots];
            t.Members.Color = party.ColorMode switch { PeerColorMode.Class => ColorSource.Personal, PeerColorMode.Remote => ColorSource.Received, _ => ColorSource.Default };
            foreach (var (name, c) in s.Classes.Where(c => c.Key != ClassProfiles.Template).ToList())
            {
                s.Classes.Remove(name);
                Personal(ClassProfiles.Edit(s, name).Mine, c.Custom ?? []);
            }
            foreach (var c in s.Characters.Values.Where(c => c.Class.Length > 0))
                Personal(ClassProfiles.Edit(s, c.Class).Mine, c.Colors ?? []);
            foreach (var c in s.Classes.Values) c.Members.Colors = new(c.Mine.Colors);   // 클래스별 used your class colours
        }
        (o.GroupVisible, o.GroupColor, o.Layout, o.HiddenSlots, party.ColorMode, party.SupportSlot) = (null, null, null, null, null, null);
        foreach (var d in party.ShowOthersBySlot) (d.Groups, d.Gauge, d.Width, d.Height, d.Colors, d.Layout) = (null, null, null, null, null, null);
        foreach (var c in s.Classes.Values) c.Custom = null;
        foreach (var c in s.Characters.Values) c.Colors = null;

        static void Personal(ClassProfile p, IEnumerable<KeyValuePair<SlotGroup, string>> colours)
        {
            foreach (var (g, hex) in colours) p.Colors[g] = hex;
            if (p.Colors.Count > 0) p.Color = ColorSource.Personal;
        }
    }

    /// <summary>Before 2.0 the effects were one set for every class (OverlaySettings); they go into 기본 and every class
    /// with settings, so nothing looks different after the update. 1.2's ShowShine = false is every group's NoShine.</summary>
    static void MoveEffectsIntoClasses(Settings s)
    {
        var o = s.Overlay;
        bool old = o.ShowShine is not null || o.NoShine is not null || o.Pulse is not null || o.PulseStyle is not null
            || o.PulseColor is not null || o.Icons is not null || o.SlotShine is not null || o.SlotPulse is not null
            || o.EndFlash is not null || o.SlotEndFlash is not null || o.KeyLetters is not null;
        if (old)
        {
            var from = new MineProfile
            {
                NoShine = o.ShowShine == false ? [.. Enum.GetValues<SlotGroup>()] : o.NoShine ?? [],
                Pulse = o.Pulse ?? [], PulseStyle = o.PulseStyle ?? [], PulseColor = o.PulseColor ?? [], Icons = o.Icons ?? [],
                SlotShine = o.SlotShine ?? [], SlotPulse = o.SlotPulse ?? [], EndFlash = o.EndFlash ?? [],
                SlotEndFlash = o.SlotEndFlash ?? [], KeyLetters = o.KeyLetters ?? [],
            };
            ClassProfiles.Entry(s, ClassProfiles.Template);
            foreach (var c in s.Classes.Values) c.Mine.CopyEffects(from);
        }
        (o.ShowShine, o.NoShine, o.Pulse, o.PulseStyle, o.PulseColor, o.Icons) = (null, null, null, null, null, null);
        (o.SlotShine, o.SlotPulse, o.EndFlash, o.SlotEndFlash, o.KeyLetters) = (null, null, null, null, null);
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
