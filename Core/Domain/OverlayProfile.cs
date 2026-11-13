using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>A window's spot (Left/Top in DIP, NaN = the default spot) and box scale.</summary>
public sealed class WindowPlace : IWindowPlace
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;

    internal static WindowPlace Of(IWindowPlace p) => new() { Left = p.Left, Top = p.Top, Width = p.Width, Height = p.Height };
    internal void To(IWindowPlace p) => (p.Left, p.Top, p.Width, p.Height) = (Left, Top, Width, Height);
}

/// <summary>One buff bar's 두께 and 길이 (<see cref="BuffIndicator"/>).</summary>
public sealed class BarSize
{
    public double Thickness { get; set; } = 1;
    public double Length { get; set; } = 1;
}

/// <summary>A look 모든 프로필에 적용 copies into every profile (<see cref="Settings.ApplyLookToAll"/>): window spots and sizes are
/// never copied. What each covers, as the 설정 page says it: <see cref="LookShare.Covers"/>; the properties:
/// <see cref="ProfileEntry.Looks"/>.</summary>
public enum LookGroup { 판, 스킬칸, 게이지, 버프막대, 미터기, 암수, 단심, 디버프, 줄수창 }

/// <summary>프로필 1–5 (user, 2026-11-13: everything on screen, not only spots): the screenshot it was set up on
/// (<see cref="Shot"/>), every window's spot and size, and every look (<see cref="ProfileEntry"/>), so a different in-game
/// situation (another HUD spot, a bright or dark map) is one switch away. Not in it: reads, the class's skill choices and box
/// style, per-character on/off, party and data. A new profile holds the defaults.</summary>
public sealed class OverlayProfile : ProfileEntry
{
    public const int Count = 5;

    /// <summary>0.22's spot and size per 직업각인: here a mirror of each <see cref="Classes"/> entry's window, so 0.22 opening
    /// a 0.23 file keeps them (read by <see cref="Migrate"/> only).</summary>
    /// <remarks>ponytail: kept for the downgrade; drop once 0.22 is out of use.</remarks>
    public Dictionary<string, WindowPlace> SubclassPlaces { get; set; } = [];

    public bool PartyTogether { get; set; }
    public double BoundPartyLeft { get; set; } = double.NaN;
    public double BoundPartyTop { get; set; } = double.NaN;
    public string PartyPlateColor { get; set; } = OverlaySettings.DefaultPlate;
    /// <summary>Each party slot window's spot (only Left/Top are used).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<WindowPlace> PartySlots { get; set; } = [];

    /// <summary>The screen area (physical pixels) the profile's screenshot shows, so it maps onto window spots; null =
    /// no screenshot, or one not of the game's screen (a backdrop only).</summary>
    public PixelRect? Shot { get; set; }

    /// <summary>Each 클래스 · 직업각인's own places and looks in this profile (<see cref="Settings.CharacterKey"/>); a class not
    /// here draws the shared ones (this profile's own, which 0.22 reads). Null in a 0.22 file (<see cref="Migrate"/>).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public Dictionary<string, ProfileEntry>? Classes { get; set; }

    /// <summary>클래스 전용: this profile's 아덴 게이지 bars per <see cref="Data.GaugeBar.Key"/>. The active profile's live in
    /// <see cref="Settings.GaugeBars"/> (stored here at a profile switch). Null in a 0.22 file (<see cref="Migrate"/>).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public Dictionary<string, GaugeBarSettings>? GaugeBars { get; set; }

    /// <summary>클래스 전용: the 아덴1 / 아덴2 boxes per <see cref="Settings.Classes"/> key (<see cref="ClassProfiles.ShownKey"/>). The active
    /// profile's live ones are in <see cref="Settings.Classes"/> (stored here at a profile switch). Null in a 0.22 file (<see cref="Migrate"/>).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public Dictionary<string, AdenShow>? Aden { get; set; }

    /// <summary><paramref name="key"/>'s entry, the shared one where it has none (or <paramref name="key"/> is null).</summary>
    public ProfileEntry EntryOf(string? key) => key is not null && Classes?.TryGetValue(key, out var e) == true ? e : this;

    /// <summary>Takes the places and looks now in <paramref name="s"/> as <paramref name="key"/>'s (null = the shared ones);
    /// the screenshot is kept. A class whose values are the shared ones keeps no entry, so it follows later shared changes.</summary>
    public void Keep(string? key, Settings s)
    {
        KeepParty(s);
        if (key is null) { CaptureFrom(s); ShareProfileWide(); return; }
        var entry = new ProfileEntry();
        entry.CaptureFrom(s);
        TakeProfileWide(entry);
        ShareProfileWide();
        Classes ??= [];
        if (SameAsShared(entry))
        {
            Classes.Remove(key);
            SubclassPlaces.Remove(key);
        }
        else (Classes[key], SubclassPlaces[key]) = (entry, WindowPlace.Of(entry.Overlay));
    }

    /// <summary><see cref="Keep"/> from 설정's copy (<see cref="Settings.EditTarget"/>): the party windows and 버프 막대 are
    /// taken from <paramref name="live"/>, never the copy's older ones.</summary>
    internal void KeepEdit(string? key, Settings edited, Settings live)
    {
        var buffs = new ProfileEntry();
        buffs.CaptureFrom(live);
        Keep(key, edited);
        KeepParty(live);
        TakeProfileWide(buffs);
        ShareProfileWide();
    }

    bool SameAsShared(ProfileEntry entry) =>
        JsonSerializer.SerializeToUtf8Bytes(entry, Compact).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes<ProfileEntry>(this, Compact));

    /// <summary>The file's options without indenting: <see cref="Keep"/> compares on every class change.</summary>
    static readonly JsonSerializerOptions Compact = new(Data.SettingsStore.Json) { WriteIndented = false };

    void KeepParty(Settings s)
    {
        (PartyTogether, BoundPartyLeft, BoundPartyTop, PartyPlateColor) = (s.Party.Together, s.Party.Left, s.Party.Top, s.Party.PlateColor);
        PartySlots = [.. s.Party.ShowOthersBySlot.Select(d => new WindowPlace { Left = d.Left, Top = d.Top })];
    }

    /// <summary>0.22's keep, for the active profile of a 0.22 file (its live values were never stored): the looks into the
    /// shared ones (one per profile then), the window's spot as <paramref name="key"/>'s.</summary>
    internal void Keep022(string? key, Settings s)
    {
        var shared = Overlay;
        KeepParty(s);
        CaptureFrom(s);
        if (key is not null) (SubclassPlaces[key], Overlay) = (Overlay, shared);
    }

    /// <summary>A 0.22 file: each 직业각인 with a spot of its own (<see cref="SubclassPlaces"/>) becomes an entry holding the
    /// profile's looks and that spot; the 게이지 bars, one for all profiles then, go into each. Hand-edited null entries
    /// go. Idempotent.</summary>
    internal void Migrate(Settings s)
    {
        if (Classes is null)
        {
            Classes = [];
            foreach (var (key, place) in SubclassPlaces.Where(kv => kv.Value is not null))
            {
                var entry = Clone(this);
                entry.Overlay = WindowPlace.Of(place);
                if (!SameAsShared(entry)) Classes[key] = entry;
            }
        }
        GaugeBars ??= CloneBars(s.GaugeBars);
        Aden ??= AdenShow.Snapshot(s.Classes);
        foreach (var (key, e) in Classes.ToList())
            if (e is null) Classes.Remove(key); else e.Fill(s);
        ShareProfileWide();   // a file from before 버프 막대 / 줄 수 창 / 디버프 숨김 were one per profile: the shared one's (the live, played class's wins at the next keep)
    }

    /// <summary>버프 막대, the 줄 수 창 and 디버프 숨김 are one per profile (<see cref="ProfileEntry.TakeProfileWide"/>): every class
    /// entry holds the shared entry's, so whatever reads an entry (apply, 복사, the split migration) sees the profile's.</summary>
    internal void ShareProfileWide()
    {
        foreach (var e in Classes?.Values ?? Enumerable.Empty<ProfileEntry>()) e.TakeProfileWide(this);
    }

    /// <summary>Puts <paramref name="shared"/>'s places and looks back as this profile's shared entry; the classes, 게이지 bars,
    /// party windows and screenshot stay.</summary>
    internal void RestoreShared(ProfileEntry shared)
    {
        foreach (var p in typeof(ProfileEntry).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (p is { CanRead: true, CanWrite: true }) p.SetValue(this, p.GetValue(shared));
    }

    internal static ProfileEntry CloneEntry(ProfileEntry e) => Clone(e);

    /// <summary>Sets <paramref name="key"/>'s entry and the 0.22 mirror of its window spot.</summary>
    internal void PutEntry(string key, ProfileEntry entry)
    {
        Classes ??= [];
        (Classes[key], SubclassPlaces[key]) = (entry, WindowPlace.Of(entry.Overlay));
    }

    internal static ProfileEntry Clone(ProfileEntry e) =>
        JsonSerializer.Deserialize<ProfileEntry>(JsonSerializer.SerializeToUtf8Bytes(e, Data.SettingsStore.Json), Data.SettingsStore.Json)!;

    internal static Dictionary<string, GaugeBarSettings> CloneBars(Dictionary<string, GaugeBarSettings> bars) =>
        JsonSerializer.Deserialize<Dictionary<string, GaugeBarSettings>>(JsonSerializer.SerializeToUtf8Bytes(bars, Data.SettingsStore.Json), Data.SettingsStore.Json)!;

    /// <summary>The shared entry and every class's (what a look applied to all reaches).</summary>
    internal IEnumerable<ProfileEntry> Entries() => Classes is null ? [this] : [this, .. Classes.Values];

    /// <summary>Puts <paramref name="key"/>'s places and looks and the party windows into <paramref name="s"/>.</summary>
    public void Apply(string? key, Settings s)
    {
        EntryOf(key).ApplyTo(s);
        (s.Party.Together, s.Party.Left, s.Party.Top, s.Party.PlateColor) = (PartyTogether, BoundPartyLeft, BoundPartyTop, PartyPlateColor);
        for (int i = 0; i < s.Party.ShowOthersBySlot.Count; i++)
        {
            var at = i < PartySlots.Count ? PartySlots[i] : new WindowPlace();
            (s.Party.ShowOthersBySlot[i].Left, s.Party.ShowOthersBySlot[i].Top) = (at.Left, at.Top);
        }
    }
}

/// <summary>The skill boxes' look kept per 배치: 칸 투명도, the gold edge, the 각성기 changed colour and the ready count's
/// colour. The boxes' colours, glow and cooldown style are the class's.</summary>
public sealed record BoxLooks(double BoxOpacity, string GoldColor, double GoldThickness, string AwakeningChangedColor, string ReadyCountColor)
{
    public static BoxLooks Of(OverlaySettings o) => new(o.BoxOpacity, o.GoldColor, o.GoldThickness, o.AwakeningChangedColor, o.ReadyCountColor);

    public void To(OverlaySettings o) =>
        (o.BoxOpacity, o.GoldColor, o.GoldThickness, o.AwakeningChangedColor, o.ReadyCountColor) = (BoxOpacity, GoldColor, GoldThickness, AwakeningChangedColor, ReadyCountColor);
}

/// <summary>The gauge's look kept per 배치: fill and empty colours, 투명도. Splits, section colours and glow are the
/// class's (<see cref="Settings.GaugeBars"/>); <see cref="GaugeStyle.ReadMode"/> is a read setting.</summary>
public sealed record GaugeLooks(string Fill, string Empty, double Opacity)
{
    public static GaugeLooks Of(GaugeStyle g) => new(g.Fill, g.Empty, g.Opacity);

    public void To(GaugeStyle g) => (g.Fill, g.Empty, g.Opacity) = (Fill, Empty, Opacity);
}

/// <summary>The buff bars' look kept per 배치: 줄어듦, 비었을 때 숨기기, and each bar's colour, 시간 표시 and 깜빡임. Their
/// 표시 (on / 자동 / off) isn't in it: that stays per character.</summary>
public sealed record BuffLooks(bool Drain, bool HideWhenEmpty, Dictionary<BuffKind, BuffBarLook> Bars)
{
    public static BuffLooks Of(BuffSettings b) => new(b.Drain, b.HideWhenEmpty,
        b.Indicators.ToDictionary(i => i.Key, i => new BuffBarLook(i.Value.Color, i.Value.ShowLabel, i.Value.EndFlash)));

    public void To(BuffSettings b)
    {
        (b.Drain, b.HideWhenEmpty) = (Drain, HideWhenEmpty);
        var defaults = new BuffSettings();
        foreach (var kind in Enum.GetValues<BuffKind>())
        {
            if (!Bars.ContainsKey(kind) && !b.Indicators.ContainsKey(kind)) continue;   // stays default
            var look = Bars.GetValueOrDefault(kind) ?? BuffBarLook.Of(defaults.Of(kind));
            var bar = b.Edit(kind);
            (bar.Color, bar.ShowLabel, bar.EndFlash) = (look.Color, look.ShowLabel, look.EndFlash);
        }
    }
}

public sealed record BuffBarLook(string Color, bool ShowLabel, bool? EndFlash)
{
    public static BuffBarLook Of(BuffIndicator i) => new(i.Color, i.ShowLabel, i.EndFlash);
}

/// <summary>The meter window's look kept per 배치: 표시 방식, the graph, row colours, background, 투명도.</summary>
public sealed record MeterLooks(MeterLook Look, bool Graph, string? My, string? Dealer, string? Support, string? Other, string Background, double Opacity)
{
    public static MeterLooks Of(MeterSettings m) => new(m.Look, m.Graph, m.MyColor, m.DealerColor, m.SupportColor, m.OtherColor, m.Background, m.Opacity);

    public void To(MeterSettings m) =>
        (m.Look, m.Graph, m.MyColor, m.DealerColor, m.SupportColor, m.OtherColor, m.Background, m.Opacity) = (Look, Graph, My, Dealer, Support, Other, Background, Opacity);
}

/// <summary>The 줄 수 창's look kept per profile: 배경 색, 불투명도.</summary>
public sealed record RaidLooks(string Background, double Opacity)
{
    public static RaidLooks Of(RaidSettings r) => new(r.Background, r.Opacity);

    public void To(RaidSettings r) => (r.Background, r.Opacity) = (Background, Opacity);
}

/// <summary>The 줄 수 창's 켜기, 빛날 때만 표시 and glow rules, one per profile (user, 2026-11-14; was per profile · class). The live
/// values stay in <see cref="RaidSettings"/>, which the reads, the tray and the 기능 맵 use.</summary>
/// <remarks>The defaults are <see cref="RaidSettings"/>'s: a field missing from the file (a hand edit, "RaidShow": {}) reads as
/// today's default, never as false (the count read off).</remarks>
public sealed record RaidShow(bool CountEnabled = true, bool OnlyWhenGlowing = false, bool Glow = true, GlowUntil GlowUntil = GlowUntil.Seconds,
    int GlowSeconds = 20, int GlowBars = 5)
{
    public static RaidShow Of(RaidSettings r) => new(r.CountEnabled, r.OnlyWhenGlowing, r.Glow, r.GlowUntil, r.GlowSeconds, r.GlowBars);

    public void To(RaidSettings r) =>
        (r.CountEnabled, r.OnlyWhenGlowing, r.Glow, r.GlowUntil, r.GlowSeconds, r.GlowBars) = (CountEnabled, OnlyWhenGlowing, Glow, GlowUntil, GlowSeconds, GlowBars);

    /// <summary>Hand-edited numbers back into the sliders' range.</summary>
    internal RaidShow Clamped() =>
        this with { GlowSeconds = Math.Clamp(GlowSeconds, 1, RaidSettings.MaxGlowSeconds), GlowBars = Math.Clamp(GlowBars, 1, RaidSettings.MaxGlowBars) };
}
