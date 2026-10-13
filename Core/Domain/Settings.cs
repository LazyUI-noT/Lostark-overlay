using System.Text.Json;
using GaugeOverlay.Core.Data;
using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>설정 → 스킬창: 자동 (read off the HP bar), 직접 입력 (typed 해상도 and HUD), 수동 조정 (moved by hand in the debug window).</summary>
public enum FitMode { 자동, 직접입력, 수동조정 }

/// <summary>Everything the user sets. One JSON file (%APPDATA%\GaugeOverlay\settings.json).
/// Adding a setting = adding a property with a default here; loading fills missing properties with defaults.</summary>
public sealed class Settings
{
    /// <summary>2 = 2.0: the effects moved into each class, and a class follows 기본 unless 개별 (<see cref="ClassSettings.Own"/>).</summary>
    public const int CurrentVersion = 4;
    public int Version { get; set; } = CurrentVersion;

    /// <summary>False until the first-start flow is completed.</summary>
    public bool FirstRunDone { get; set; }

    /// <summary>Key per slot, as the user binds it in the game (e.g. "Shift", "C").</summary>
    public Dictionary<SlotId, string> Keys { get; set; } = SlotInfo.All.ToDictionary(s => s, SlotInfo.DefaultKey);

    public OverlaySettings Overlay { get; set; } = new();
    public GaugeStyle Gauge { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public PartySettings Party { get; set; } = new();
    public BuffSettings Buffs { get; set; } = new();
    public DebuffSettings Debuffs { get; set; } = new();
    public JudgmentSettings Judgment { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();
    /// <summary>설정's colour theme (the window only; the overlay's colours are its own).</summary>
    public UiTheme Theme { get; set; } = UiTheme.DeepTeal;
    /// <summary>The 아덴 게이지 bar per "클래스/직업각인" (GaugeBar.Key): only the ones changed in the 게이지 tab; the
    /// rest use the classes.json default.</summary>
    public Dictionary<string, GaugeBarSettings> GaugeBars { get; set; } = [];

    /// <summary>Per-character choices, keyed by the character's display label.</summary>
    public Dictionary<string, CharacterSettings> Characters { get; set; } = [];
    public string? ActiveCharacter { get; set; }

    /// <summary>Makes the "클래스 · 직업각인" character active (the key the 캐릭터 tab uses), adding it the first time; its
    /// emblem is learned at the next capture. Returns the key.
    /// <paramref name="remember"/>: it was picked by hand or read off the bar, so it goes into <see cref="SubclassHistory"/>.</summary>
    public string PickCharacter(string family, string className, string subclass, bool remember = false)
    {
        var key = CharacterKey(className, subclass);
        if (!Characters.ContainsKey(key)) Characters[key] = new() { ClassFamily = family, Class = className, Subclass = subclass };
        if (remember) SubclassHistory[className] = subclass;
        return ActiveCharacter = key;
    }

    /// <summary>The key of a character in <see cref="Characters"/>, <see cref="Cores"/>: "클래스 · 직업각인".</summary>
    public static string CharacterKey(string className, string subclass) => $"{className} · {subclass}";

    /// <summary>코어 빌드 per "클래스 · 직업각인" (<see cref="CharacterKey"/>): kept when the character changes, one for all three
    /// 프로필 and apart from the active character. Read through <see cref="CoreOf"/>, set through <see cref="SetCore"/>.</summary>
    public Dictionary<string, CoreChoice> Cores { get; set; } = [];

    /// <summary>The choice saved for <paramref name="characterKey"/>, or the default (off, 1 1 1) when none was ever made.</summary>
    public CoreChoice CoreOf(string characterKey) => Cores.GetValueOrDefault(characterKey) ?? new();

    /// <summary>Saves <paramref name="choice"/> (digits clamped to 1–3) for <paramref name="characterKey"/> only.</summary>
    public void SetCore(string characterKey, CoreChoice choice) => Cores[characterKey] = choice.Clamped();

    /// <summary>Slots learned as stack skills per "클래스 · 직업각인" (<see cref="CharacterKey"/>): a count seen at 스킬창 맞추기 or
    /// beside timers in play. Never forgotten; a stack can come from the skill, a tripod or a core (user, 2026-10-13).
    /// Slot names, not SlotId: a name that isn't one (a hand edit) is skipped, where an enum would fail the whole file.</summary>
    public Dictionary<string, List<string>> StackSkills { get; set; } = [];

    public IReadOnlyList<SlotId> StackSkillsOf(string characterKey) =>
        [.. (StackSkills.GetValueOrDefault(characterKey) ?? []).Select(n => Enum.TryParse<SlotId>(n, out var s) && s.ToString() == n ? s : (SlotId?)null)
            .OfType<SlotId>().Distinct()];

    /// <summary>Remembers <paramref name="slot"/> as a stack skill of <paramref name="characterKey"/>; false if it was known.</summary>
    public bool AddStackSkill(string characterKey, SlotId slot)
    {
        var known = StackSkillsOf(characterKey);
        if (known.Contains(slot)) return false;
        StackSkills[characterKey] = [.. known.Append(slot).Select(s => s.ToString())];
        return true;
    }

    /// <summary>The 직업각인 last played per class (picked by hand or read off the bar): a class detected with one takes it
    /// and its 직업각인 isn't read again until asked (직업각인 다시 감지).</summary>
    public Dictionary<string, string> SubclassHistory { get; set; } = [];

    /// <summary>설정 → 캐릭터's supported 직업각인 and features the user has seen (Core.Data.ClassSupport keys): anything shipped
    /// later shows NEW until the tab is opened. Null: never recorded (set at start-up, so a first run marks nothing).</summary>
    public List<string>? SeenSupport { get; set; }

    /// <summary>The 초각스 icon shows <paramref name="subclass"/>: switches to the active class's character for it (added
    /// if new), which takes the emblem when it has none, since both are the same class on screen.</summary>
    public void DetectedSubclass(string subclass)
    {
        if (ActiveCharacter is not { } k || !Characters.TryGetValue(k, out var c)) return;
        PickCharacter(c.ClassFamily, c.Class, subclass, remember: true);
        Characters[ActiveCharacter].EmblemBgra ??= c.EmblemBgra;
    }

    /// <summary>Skill-bar fit per viewport size, keyed "W×H" (Geometry.ViewportKey).</summary>
    public Dictionary<string, Fit> Fits { get; set; } = [];

    /// <summary>크기 직접 입력: the game's HUD size (%) that 스킬창 맞추기 uses instead of reading it off the HP bar; null reads it.
    /// One of <see cref="HudSizes"/>.</summary>
    public double? ManualHud { get; set; }

    /// <summary>The HUD sizes the game's settings offer.</summary>
    public static readonly int[] HudSizes = [80, 90, 100, 105, 110];

    /// <summary>The 버프 크기 sizes the game's settings offer.</summary>
    public static readonly int[] BuffSizes = [80, 90, 100, 110, 120];

    /// <summary>The offered HUD size nearest <paramref name="hud"/> (a size read off the HP bar is a little off).</summary>
    public static int NearestHud(double hud) => HudSizes.MinBy(h => Math.Abs(h - hud));

    /// <summary>직접 입력 (on while <see cref="ManualHud"/> is set): the game's 해상도 and forced 21:9, which then give the game area
    /// instead of the black bars (Geometry.ManualViewport).</summary>
    public ManualScreen? ManualScreen { get; set; }

    /// <summary>What the last automatic 스킬창 맞추기 found: the game window's size and whether it had 21:9 bars. 직접 입력 starts from it.</summary>
    public ManualScreen? DetectedScreen { get; set; }

    /// <summary>Turns 직접 입력 on from what the last 스킬창 맞추기 found: its 해상도, 21:9 and the HUD size of its fit (else 1920×1080
    /// and 100).</summary>
    public void StartManual()
    {
        var d = DetectedScreen ?? new ManualScreen(1920, 1080, false);
        ManualScreen = d;
        var area = Vision.Geometry.ManualViewport(new PixelRect(0, 0, d.Width, d.Height), d);
        ManualHud = Fits.TryGetValue(Vision.Geometry.ViewportKey(area), out var fit) ? NearestHud(fit.Hud) : 100;
    }

    /// <summary>수동 조정: fits moved and scaled by hand in the debug window and applied (적용), per game-area size (ViewportKey).
    /// Used only while <see cref="UseAdjusts"/>; kept when another mode is picked.</summary>
    public Dictionary<string, Fit> Adjusts { get; set; } = [];
    public bool UseAdjusts { get; set; }

    /// <summary>Where the skill bar's place comes from (설정 → 스킬창).</summary>
    [JsonIgnore]
    public FitMode FitMode => UseAdjusts ? FitMode.수동조정 : ManualHud is not null ? FitMode.직접입력 : FitMode.자동;

    public void SetFitMode(FitMode mode)
    {
        switch (mode)
        {
            case FitMode.직접입력: if (ManualHud is null) StartManual(); UseAdjusts = false; break;   // typed values stay
            // 직접 입력's values stay (the game area they give is the one the adjust was made on); otherwise the area is measured.
            case FitMode.수동조정: UseAdjusts = true; break;
            default: (ManualHud, ManualScreen, UseAdjusts) = (null, null, false); break;
        }
    }

    /// <summary>적용: <paramref name="fit"/> is the game area <paramref name="key"/>'s 수동 조정, and that mode is on.</summary>
    public void ApplyAdjust(string key, Fit fit)
    {
        Adjusts[key] = fit;
        SetFitMode(FitMode.수동조정);
    }

    /// <summary>스킬창 다시 맞추기 chose to drop <paramref name="key"/>'s 수동 조정: back to 자동.</summary>
    public void DiscardAdjust(string key)
    {
        Adjusts.Remove(key);
        SetFitMode(FitMode.자동);
    }

    /// <summary>The game area <paramref name="key"/> uses an applied 수동 조정 now.</summary>
    public bool AdjustActive(string? key) => UseAdjusts && key is not null && Adjusts.ContainsKey(key);

    /// <summary>What the engine tries before the fits: a copy of <see cref="Adjusts"/> in 수동 조정, else null.</summary>
    public IReadOnlyDictionary<string, Fit>? ActiveAdjusts() => UseAdjusts ? new Dictionary<string, Fit>(Adjusts) : null;

    /// <summary>Per class (ClassInfo.Name, or <see cref="ClassProfiles.Template"/> for a class without its own): colours
    /// learned from its icons at a fit, and what to show, where and in what colours, for you and for party members. See
    /// <see cref="ClassProfiles"/> for the order they apply in.</summary>
    public Dictionary<string, ClassSettings> Classes { get; set; } = [];

    /// <summary>오버레이 프로필 1–5: each keeps a screenshot and where the windows are, their sizes, thicknesses, split or
    /// together and plate colours. The active one's values live in the settings above (so nothing else reads profiles);
    /// <see cref="SwitchProfile"/> swaps them. Replaced on load, not added to.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<OverlayProfile> Profiles { get; set; } = [.. Enumerable.Range(0, OverlayProfile.Count).Select(_ => new OverlayProfile())];
    public int ActiveProfile { get; set; }

    /// <summary>레이아웃 편집기의 패널 hidden from its 패널 tray (편집, 클래스, 칸 옵션, 전체 화면).</summary>
    public HashSet<string> EditorHidden { get; set; } = [];

    /// <summary>The 직업각인 whose window spot and size <see cref="Overlay"/> holds now; null = the one shared by a character with no listed 직업각인.</summary>
    public string? PlaceKey { get; set; }

    /// <summary>After the character changed: when the settings your boxes use (<see cref="ClassProfiles.ActiveKey"/>)
    /// are a 직업각인's, or no longer are, keeps your window's spot and size for the one left (in the active profile) and
    /// puts back the new one's; a 직업각인 placed for the first time starts where the window is. True when it changed.</summary>
    public bool SyncPlace()
    {
        var key = ClassProfiles.ActiveKey(this) is { } k && ClassProfiles.Parent(k) is { } p && p != ClassProfiles.Template ? k : null;
        if (key == PlaceKey) return false;
        if (ActiveProfile >= 0 && ActiveProfile < Profiles.Count)
        {
            var profile = Profiles[ActiveProfile];
            bool placed = key is null || profile.SubclassPlaces.ContainsKey(key);
            profile.KeepPlace(PlaceKey, this);
            if (placed) profile.PlaceOf(key).To(Overlay);
        }
        PlaceKey = key;
        return true;
    }

    /// <summary>Keeps the current places in the active profile and puts back <paramref name="index"/>'s. The same or a
    /// missing profile changes nothing.</summary>
    public void SwitchProfile(int index)
    {
        if (index == ActiveProfile || index < 0 || index >= Profiles.Count) return;
        if (ActiveProfile >= 0 && ActiveProfile < Profiles.Count) Profiles[ActiveProfile].CaptureFrom(this);
        Profiles[index].ApplyTo(this);
        ActiveProfile = index;
    }

    /// <summary>레이아웃 편집기 적용: copies what the editor changes from its copy <paramref name="edited"/> (places, sizes,
    /// looks, class boxes and effects, profiles) and nothing the app writes meanwhile: fits, the active
    /// character (the editor previews other classes on its copy), 오버레이 표시.</summary>
    public void ApplyLayoutFrom(Settings edited)
    {
        // a copy, so the editor's later edits don't reach these settings through shared objects
        edited = JsonSerializer.Deserialize<Settings>(JsonSerializer.SerializeToUtf8Bytes(edited, SettingsStore.Json), SettingsStore.Json)!;
        var (o, e) = (Overlay, edited.Overlay);
        (o.Left, o.Top, o.Width, o.Height, o.Opacity, o.PlateColor) = (e.Left, e.Top, e.Width, e.Height, e.Opacity, e.PlateColor);
        (o.BoxOpacity, o.ShowTimer, o.TimerFont, o.CooldownStyle, o.FillFrom) = (e.BoxOpacity, e.ShowTimer, e.TimerFont, e.CooldownStyle, e.FillFrom);
        o.ReadyCountColor = e.ReadyCountColor;
        (o.GoldColor, o.GoldThickness, o.AwakeningChangedColor, o.LayoutSnap, o.LayoutLinkSizes) =
            (e.GoldColor, e.GoldThickness, e.AwakeningChangedColor, e.LayoutSnap, e.LayoutLinkSizes);
        (Buffs, Debuffs, Judgment, Gauge, GaugeBars) = (edited.Buffs, edited.Debuffs, edited.Judgment, edited.Gauge, edited.GaugeBars);
        var (p, ep) = (Party, edited.Party);
        (p.Together, p.Left, p.Top, p.PlateColor) = (ep.Together, ep.Left, ep.Top, ep.PlateColor);
        for (int i = 0; i < Math.Min(p.ShowOthersBySlot.Count, ep.ShowOthersBySlot.Count); i++)
            (p.ShowOthersBySlot[i].Left, p.ShowOthersBySlot[i].Top) = (ep.ShowOthersBySlot[i].Left, ep.ShowOthersBySlot[i].Top);
        foreach (var (name, c) in edited.Classes)
        {
            var live = ClassProfiles.Entry(this, name);
            (live.Own, live.Mine, live.Members) = (c.Own, c.Mine, c.Members);
        }
        (Profiles, ActiveProfile, PlaceKey) = (edited.Profiles, edited.ActiveProfile, edited.PlaceKey);
        SyncPlace();   // the editor may have shown another 직업각인's spot: back to this character's
    }

    /// <summary>Whether <see cref="ApplyLayoutFrom"/> would change these settings: the 레이아웃 편집기 asks before 닫기
    /// drops changes not applied.</summary>
    public bool LayoutDiffers(Settings edited)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, SettingsStore.Json);
        Settings Copy() => JsonSerializer.Deserialize<Settings>(bytes, SettingsStore.Json)!;
        var applied = Copy();
        applied.ApplyLayoutFrom(edited);
        return !Captured(Copy()).AsSpan().SequenceEqual(Captured(applied));
    }

    /// <summary>The settings with every profile's stored copy as a switch leaves it: a profile is stored only on leaving
    /// it (and an unvisited one in a shorter form), so a visit to another profile and back is no change.</summary>
    static byte[] Captured(Settings s)
    {
        int at = s.ActiveProfile;
        if (at >= 0 && at < s.Profiles.Count)
        {
            for (int i = 0; i < s.Profiles.Count; i++) s.SwitchProfile(i);
            s.SwitchProfile(at);
        }
        return JsonSerializer.SerializeToUtf8Bytes(s, SettingsStore.Json);
    }
}

/// <summary>Where a window sits and the size of its boxes: yours (<see cref="OverlaySettings"/>), and a party member's
/// (the slot's spot, <see cref="PeerDisplay"/>, with the size for their class, <see cref="MemberProfile"/>). Left/Top NaN =
/// the default spot; Width/Height are box scales, 0.3 – 2.0.</summary>
public interface IWindowPlace
{
    double Left { get; set; }
    double Top { get; set; }
    double Width { get; set; }
    double Height { get; set; }
}

public sealed class OverlaySettings : IWindowPlace
{
    public double Left { get; set; } = double.NaN;   // NaN = default position (centred above the skill bar)
    public double Top { get; set; } = double.NaN;
    // Box width and height, each 0.3 – 2.0. A party member's window has its own (PeerDisplay).
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;
    /// <summary>1.x's one party window for every member; SettingsStore moves them into each
    /// <see cref="PartySettings.ShowOthersBySlot"/> entry and clears them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? PartyLeft { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? PartyTop { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? PartyWidth { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? PartyHeight { get; set; }
    /// <summary>1.0's single size for both windows; SettingsStore moves it into the sizes above and clears it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Scale { get; set; }
    public double Opacity { get; set; } = 0.9;        // plate
    /// <summary>배경 판 표시: the plate behind your boxes and party members'; off, only the boxes, gauge and names are drawn.
    /// The detached buff window never has one.</summary>
    public bool ShowPlate { get; set; } = true;
    /// <summary>The plate's colour (#RRGGBB) behind your boxes; <see cref="Opacity"/> sets how see-through.</summary>
    public string PlateColor { get; set; } = DefaultPlate;
    public const string DefaultPlate = "#0C0D10";
    public double BoxOpacity { get; set; } = 1.0;     // boxes (and their timers)
    public bool ShowTimer { get; set; } = true;
    public double TimerFont { get; set; } = 14;      // DIP; drawn capped at what fits the box (OverlayRenderer.MaxTimerFont)
    /// <summary>쿨 돈 뒤 지난 시간's number colour (#RRGGBB), outlined in white, or black for a light colour.</summary>
    public string ReadyCountColor { get; set; } = "#000000";
    /// <summary>1.2's one switch for 준비되면 반짝임; SettingsStore turns false into every group's NoShine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowShine { get; set; }
    /// <summary>Before 2.0 the effects (<see cref="MineProfile"/>) were one set for every class; SettingsStore moves them
    /// into 기본 and every class, and clears them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HashSet<SlotGroup>? NoShine { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HashSet<SlotGroup>? Pulse { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<SlotGroup, ReadyGlowStyle>? PulseStyle { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<SlotGroup, string>? PulseColor { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HashSet<SlotGroup>? Icons { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<SlotId, bool>? SlotShine { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<SlotId, bool>? SlotPulse { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HashSet<SlotGroup>? EndFlash { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<SlotId, bool>? SlotEndFlash { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HashSet<SlotId>? KeyLetters { get; set; }
    public CooldownStyle CooldownStyle { get; set; } = CooldownStyle.Sweep;
    public FillFrom FillFrom { get; set; } = FillFrom.Bottom;
    /// <summary>Border drawn on a box while the game shows its gold frame on that skill (#RRGGBB), and its width in
    /// DIP, growing inward from the box edge; 0 = not drawn.</summary>
    public string GoldColor { get; set; } = "#FFD24A";
    public double GoldThickness { get; set; } = 2;

    public string AwakeningChangedColor { get; set; } = "#FF4D4D";   // red, as in the game (1.x: #FF7F27, migrated)

    /// <summary>While placing boxes: snap to the other boxes (Shift turns it off for a drag), and resize a set of boxes
    /// together (FreeLayout.Linked).</summary>
    public bool LayoutSnap { get; set; } = true;
    public bool LayoutLinkSizes { get; set; } = true;

    /// <summary>Before per-class settings (<see cref="ClassProfile"/>): the groups and slots shown, the colours for all
    /// classes and the free layout. SettingsStore moves them into the template class and clears them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<SlotGroup, bool>? GroupVisible { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<SlotGroup, string>? GroupColor { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<Part, Cell>? Layout { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HashSet<SlotId>? HiddenSlots { get; set; }

    /// <summary>Hide the overlay from screen capture (needed when it overlaps the skill bar; also hides it from OBS/Discord).</summary>
    public bool ExcludeFromCapture { get; set; } = true;

    /// <summary>오버레이 표시: off hides your overlay and the party windows (set also by the hotkey and the tray).</summary>
    public bool Visible { get; set; } = true;
}

public sealed class GaugeStyle
{
    public string Fill { get; set; } = "#1E9BE6";
    public string Empty { get; set; } = "#3A3A3A";
    public double Thickness { get; set; } = 6;
    public double Opacity { get; set; } = 1.0;
    public GaugeReadMode ReadMode { get; set; }
}

/// <summary>When a gauge read is shown (GaugeFilter): <see cref="Agreed"/>, once 5 reads agree; <see cref="SteadyRise"/>, a small
/// rise already after 2; <see cref="Predicted"/> (beta), a rise the casts before it predict after 2 (GaugeGains).</summary>
public enum GaugeReadMode { Agreed, SteadyRise, Predicted }

/// <summary>How the gauge bar glows: <see cref="Pulse"/>, brightness breathing; <see cref="Shine"/>, a white sweep across;
/// <see cref="Outline"/>, a bright border that breathes.</summary>
public enum GlowStyle { Pulse, Shine, Outline }

/// <summary>How a ready box shows 쿨 돈 동안 계속 빛남: a band sweeping top-left → bottom-right (사선 띠) or left → right
/// (가로 띠), the whole box breathing (숨쉬기), or a border that breathes (테두리).</summary>
public enum ReadyGlowStyle { Diagonal, Horizontal, Breathe, Outline }

/// <summary>One 클래스·직업각인's gauge bar as the user set it (게이지 tab). <see cref="Splits"/> null = the classes.json
/// default. When the bar glows is class data (adenGauge), not a setting.</summary>
public sealed class GaugeBarSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double>? Splits { get; set; }
    public double SplitThickness { get; set; } = 2;   // DIP
    public string SplitColor { get; set; } = "#101010";
    /// <summary>모든 구간 같은 색: every segment in the 게이지 fill colour; off, <see cref="SegmentColors"/> left to right.</summary>
    public bool UnifiedColor { get; set; } = true;
    public List<string> SegmentColors { get; set; } = [];
    /// <summary>The class's full-section colour, over classes.json "colors" (and, without those, the 게이지 fill colour);
    /// null = the default.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FullColor { get; set; }
    /// <summary>The colour of the section still filling (바드's gold), over classes.json "colors"; null = the default.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FillingColor { get; set; }
    /// <summary>색 단계: the whole fill deepens as sections fill (classes.json "ramp"); <see cref="RampColors"/> are weak,
    /// medium, strong, null = the fill colour deepening (<see cref="Data.GaugeBar.RampDefaults"/>).</summary>
    public bool Ramp { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? RampColors { get; set; }
    public bool GlowEnabled { get; set; } = true;
    public GlowStyle GlowStyle { get; set; } = GlowStyle.Pulse;
    public double GlowPeriod { get; set; } = 1.2;     // seconds per pulse
}

/// <summary>The party buff indicators (PLAN §4 버프). Local only: never sent to the party.</summary>
public sealed class BuffSettings : IWindowPlace
{
    /// <summary>버프 막대 따로 두기: the bars leave the plate for a window of their own, placed and sized on its own
    /// (<see cref="Left"/> … <see cref="Height"/>; NaN = next to the overlay, until first placed).</summary>
    public bool Detached { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;
    /// <summary>The game's 버프 크기 setting (%), usually one of <see cref="Settings.BuffSizes"/> (위치 조정's ± sets any whole %): where the buff and debuff rows sit and
    /// how big their icons are. 스킬창 맞추기 doesn't detect it; 120 is the size the reads are measured best at.</summary>
    public int Size { get; set; } = 120;
    /// <summary>세로: the bars stand upright side by side instead of lying flat one under another. In the fixed grid they stand
    /// left of the plate, outside it (flat: under the grid, two to a row); in their own window they fill it (flat: one to a
    /// row). A free layout keeps them where they were put.</summary>
    public bool Side { get; set; }
    /// <summary>버프 막대 줄어듦: the bar drains as the buff runs out; off, it stays full while the buff is on.</summary>
    public bool Drain { get; set; } = true;
    /// <summary>Only the indicators changed from <see cref="Default"/>; read through <see cref="Of"/>.</summary>
    public Dictionary<BuffKind, BuffIndicator> Indicators { get; set; } = [];

    public BuffIndicator Of(BuffKind kind) => Indicators.GetValueOrDefault(kind) ?? Default(kind);

    /// <summary>마지막 1초 깜빡임: whether the bar flashes in its drain direction through its last second; unset, on for
    /// 공증 and 아덴 피증.</summary>
    public bool EndFlash(BuffKind kind) => Of(kind).EndFlash ?? kind is BuffKind.공증 or BuffKind.아덴피증;

    /// <summary>The indicator to change, added from the default the first time.</summary>
    public BuffIndicator Edit(BuffKind kind) =>
        Indicators.TryGetValue(kind, out var i) ? i : Indicators[kind] = Default(kind);

    static BuffIndicator Default(BuffKind kind) => new()
    {
        Color = kind switch { BuffKind.공증 => "#E8262B", BuffKind.아덴피증 => "#FFE600", BuffKind.초각스피증 => "#1BA8E0", _ => "#A04FB0" },
    };
}

/// <summary>디버프 (PLAN §4): the raid whose debuffs are read, and the debuff window's own spot and size (NaN = next to the
/// overlay, until first placed). The row's size is the game's 버프 크기 (<see cref="BuffSettings.Size"/>).</summary>
public sealed class DebuffSettings : IWindowPlace
{
    public Raid Raid { get; set; } = Raid.없음;
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;

    public const int BoxCount = 3;
    /// <summary>Each box's own 너비 and 높이 (내 색, 교체, 번개), times the window's size; read through <see cref="Box"/>.</summary>
    public List<DebuffBox> Boxes { get; set; } = [];

    /// <summary>Box <paramref name="i"/>'s size, added at 1 × 1 the first time.</summary>
    public DebuffBox Box(int i)
    {
        while (Boxes.Count <= i) Boxes.Add(new());
        return Boxes[i];
    }
}

public sealed class DebuffBox
{
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;
}

public sealed class BuffIndicator
{
    /// <summary>표시. With every indicator off the buff row isn't read at all.</summary>
    public bool Visible { get; set; } = true;
    /// <summary>시간 표시: the "6s" label on the bar.</summary>
    public bool ShowLabel { get; set; } = true;
    public string Color { get; set; } = "";   // BuffSettings.Default sets it
    /// <summary>길이: share (0.1–1) of the bar's length in the layout, along which it drains (upright: its height, from
    /// the bottom; flat: its width, from the left). In a free layout, of its cell (BarSizes.Sized).</summary>
    public double Length { get; set; } = 1;
    /// <summary>두께: the layout's bar thickness × this (0.25–3).</summary>
    public double Thickness { get; set; } = 1;
    /// <summary>마지막 1초 깜빡임; null = the kind's default (<see cref="BuffSettings.EndFlash"/>).</summary>
    public bool? EndFlash { get; set; }
}

/// <summary>업데이트 확인 (설정, bottom left): whether the app looks for a new release at start, and the release it last told
/// the user about, so the notice shows once per version.</summary>
public sealed class UpdateSettings
{
    public bool Check { get; set; } = true;
    /// <summary>Tag of the release the tray notice was shown for ("v1.0.6"); "" for none.</summary>
    public string Notified { get; set; } = "";
    /// <summary>Just installed, shown once by the new version: its tag and its notes as plain lines; "" when nothing is due.</summary>
    public string WhatsNewTag { get; set; } = "";
    public string WhatsNew { get; set; } = "";
}

public sealed class HotkeySettings
{
    public string EditMode { get; set; } = "Ctrl+Shift+E";
    public string ToggleOverlay { get; set; } = "Ctrl+Shift+H";
    /// <summary>스킬창 다시 맞추기: fits the skill bar again (the 아덴 keys may have moved), then detects the character
    /// (class emblem and 초각스 icon). Named for what it did first, so a key set before still works.</summary>
    public string DetectCharacter { get; set; } = "Ctrl+Shift+D";
    /// <summary>테스트 모드: the window last in front (a video player) stands in for the game, which isn't looked for.</summary>
    public string TestMode { get; set; } = "Ctrl+Shift+T";
    /// <summary>직업각인 다시 감지: reads the 직업각인 again, past the history; unset ("") by default.</summary>
    public string DetectSubclass { get; set; } = "";
    /// <summary>아이콘 다시 읽기: copies every slot's icon again (아이콘으로 표시); unset ("") by default.</summary>
    public string RereadIcons { get; set; } = "";
    /// <summary>오버레이 프로필 1–5; unset ("") by default.</summary>
    public string Profile1 { get; set; } = "";
    public string Profile2 { get; set; } = "";
    public string Profile3 { get; set; } = "";
    public string Profile4 { get; set; } = "";
    public string Profile5 { get; set; } = "";

    static readonly string[] ProfileActions = [nameof(Profile1), nameof(Profile2), nameof(Profile3), nameof(Profile4), nameof(Profile5)];

    /// <summary>Every action with its combo, by action name (nameof the property).</summary>
    public IReadOnlyList<(string Action, string Combo)> All() =>
    [
        (nameof(EditMode), EditMode), (nameof(ToggleOverlay), ToggleOverlay), (nameof(DetectCharacter), DetectCharacter),
        (nameof(DetectSubclass), DetectSubclass), (nameof(RereadIcons), RereadIcons),
        (nameof(TestMode), TestMode), (nameof(Profile1), Profile1), (nameof(Profile2), Profile2), (nameof(Profile3), Profile3),
        (nameof(Profile4), Profile4), (nameof(Profile5), Profile5),
    ];

    /// <summary>The profile (0-based) a ProfileN action switches to; null for the other actions.</summary>
    public static int? ProfileOf(string action) => Array.IndexOf(ProfileActions, action) is var i and >= 0 ? i : null;

    /// <summary>Actions whose combo another action or a skill key (<paramref name="skillKeys"/>) also uses: one of them
    /// wouldn't register.</summary>
    public IReadOnlyList<string> Clashes(IEnumerable<string> skillKeys)
    {
        var used = All().Select(a => a.Combo).Concat(skillKeys).Where(c => c != "").GroupBy(c => c)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return [.. All().Where(a => used.Contains(a.Combo)).Select(a => a.Action)];
    }
}

/// <summary>A device the host let in (참가 요청 승인): its <c>DeviceIdentity.Fingerprint</c> and the name it last used.</summary>
public sealed record TrustedDevice(string Device, string Name);

public sealed class PartySettings
{
    public string DisplayName { get; set; } = "";
    public int HostPort { get; set; } = 47800;

    /// <summary>참가 요청 승인: while hosting, ask before letting in a device that isn't trusted.</summary>
    public bool ApproveJoins { get; set; } = true;
    /// <summary>신뢰한 기기: devices approved once, let in without asking from then on (removed by 내보내기 or 삭제).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<TrustedDevice> TrustedDevices { get; set; } = [];
    public Dictionary<SlotGroup, bool> Send { get; set; } = Enum.GetValues<SlotGroup>().ToDictionary(g => g, _ => true);
    public bool SendGauge { get; set; } = true;

    /// <summary>A copy of the groups to send (a group missing from <see cref="Send"/> is sent, as the settings UI shows it),
    /// safe to hand to the party's network threads while the UI edits <see cref="Send"/>.</summary>
    public IReadOnlySet<SlotGroup> SendGroups() => Enum.GetValues<SlotGroup>().Where(g => Send.GetValueOrDefault(g, true)).ToHashSet();
    /// <summary>What stands left of each party member's boxes.</summary>
    public PeerLabel Label { get; set; } = PeerLabel.IconAndName;

    /// <summary>Where each party slot's window is (0/1/2 = the members numbered by <see cref="PartySlots.Numbers"/>),
    /// whoever occupies it; what it shows follows the member's class (<see cref="ClassSettings.Members"/>). Replaced on
    /// load, not added to (the default three would stay in front of the saved ones).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<PeerDisplay> ShowOthersBySlot { get; set; } = [new(), new(), new()];

    /// <summary>기본 colour for party members: the 일반스킬 colour of each role (<see cref="ColorSource.Default"/>).</summary>
    public Dictionary<Role, string> RoleColor { get; set; } = new() { [Role.딜러] = "#FF6B70", [Role.서포터] = "#E8B53A" };

    /// <summary>파티원 크기 받기 / 파티원 배치 받기: use the box size or layout a member sent, fitted inside the size you set
    /// for them (<see cref="ClassProfiles.Member"/>). Off: what they sent is kept but unused.</summary>
    public bool ReceiveSize { get; set; }
    public bool ReceiveLayout { get; set; }

    /// <summary>파티원 순서: the name pinned to each slot (null = whoever is next by join order). See
    /// <see cref="PartySlots.Assign"/>.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<string?> SlotNames { get; set; } = [];
    /// <summary>내 번호: your number in the party, 1–4; members take the other three (<see cref="PartySlots.Numbers"/>).</summary>
    public int MyNumber { get; set; } = 1;
    /// <summary>서포터 고정: the number a supporter always takes (not while you are the supporter).</summary>
    public bool SupportFixed { get; set; }
    public int SupportNumber { get; set; } = 2;

    /// <summary>Before per-class settings: the 파티원 색 mode and the support slot (0–2, -1 = none). SettingsStore moves
    /// them into <see cref="ClassSettings.Members"/> and <see cref="SupportFixed"/> and clears them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PeerColorMode? ColorMode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? SupportSlot { get; set; }

    /// <summary>Size of the class icon beside a party member's boxes, 0.5 – 2.0.</summary>
    public double IconScale { get; set; } = 1.0;
    /// <summary>이름 길이: members' names are cut to this many units on your screen (한글 2, 영문 1; PlayerName).</summary>
    public int NameLength { get; set; } = PlayerName.MaxUnits;

    /// <summary>파티 묶기: the members stacked in one window with one plate, at <see cref="Left"/>/<see cref="Top"/> (NaN =
    /// the default spot); off, each slot has its own window (<see cref="ShowOthersBySlot"/>).</summary>
    public bool Together { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    /// <summary>The party windows' plate colour (#RRGGBB).</summary>
    public string PlateColor { get; set; } = OverlaySettings.DefaultPlate;
}

public enum PeerLabel { IconAndName, Name, Icon }

public enum Role { 딜러, 서포터 }

/// <summary>Before per-class settings, how party members' boxes were coloured; only read to move it
/// (<see cref="PartySettings.ColorMode"/>).</summary>
public enum PeerColorMode { Role, Class, Same, Slot, Remote }

public sealed class ClassSettings
{
    /// <summary>개별: the class uses its own <see cref="Mine"/> and <see cref="Members"/>; off, 기본's (kept for when it
    /// is turned on again).</summary>
    public bool Own { get; set; }
    /// <summary>나: your own boxes while you play this class.</summary>
    public MineProfile Mine { get; set; } = new();
    /// <summary>파티원: a party member's boxes while they play this class.</summary>
    public MemberProfile Members { get; set; } = new();
    /// <summary>Before per-class settings: the colours set for the class. SettingsStore moves them into
    /// <see cref="Mine"/> and <see cref="Members"/> and clears them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<SlotGroup, string>? Custom { get; set; }
}

/// <summary>One party slot's window spot; what it shows follows its member's class.</summary>
public sealed class PeerDisplay
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    /// <summary>Before per-class settings: what this slot showed, its size, colours and layout. SettingsStore moves the
    /// first slot's into the template class (<see cref="ClassSettings.Members"/>) and clears them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<SlotGroup, bool>? Groups { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Gauge { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Width { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Height { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<SlotGroup, string>? Colors { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<Part, Cell>? Layout { get; set; }
}

public sealed class CharacterSettings
{
    public string ClassFamily { get; set; } = "";   // 직업군
    public string Class { get; set; } = "";         // 클래스
    public string Subclass { get; set; } = "";      // 직업각인
    /// <summary>Emblem crop learned on first pick, BGRA, for auto-detection. Base64.</summary>
    public string? EmblemBgra { get; set; }
    /// <summary>Before per-class settings: colours for this character only. SettingsStore moves them into its class
    /// (<see cref="ClassSettings.Mine"/>) and clears them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<SlotGroup, string>? Colors { get; set; }
}
