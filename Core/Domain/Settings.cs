using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>The settings file's schema: every saved property of the root, in file order (System.Text.Json writes them in
/// declaration order, and <see cref="Palette"/> must follow "Theme"). The slices are their own classes (OverlaySettings,
/// PartySettings, …); the root's logic lives in the Settings.*.cs partials. Adding a setting = a property with a default, here or in its slice;
/// loading fills missing properties with defaults.</summary>
public sealed partial class Settings : IJsonOnDeserializing
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
    /// <summary>설정 → 암흑수류탄: the 암수 창 (amsu.design.md).</summary>
    public AmsuSettings Amsu { get; set; } = new();
    /// <summary>초당 읽기 횟수 (ReadSteps): the read loop's steps a second, 5–30.</summary>
    public int ReadsPerSecond { get; set; } = ReadSteps.Default;
    public UpdateSettings Updates { get; set; } = new();
    /// <summary>설정 → 레이드: the 줄 수 창, boss recognition and each gate's switches (project B).</summary>
    public RaidSettings Raids { get; set; } = new();
    /// <summary>설정 → DPS 미터기 (project C).</summary>
    public MeterSettings Meter { get; set; } = new();
    /// <summary>전투 기록: how many automatic records to keep (1–50, RecordStore clamps); 보관 ones don't count.</summary>
    public int RecordKeep { get; set; } = 50;
    /// <summary>A saved <see cref="RecordKeep"/> of 20 (the default before 전투 기록 went per character) has moved to 50 once.</summary>
    public bool RecordKeepMigrated { get; set; } = true;
    /// <summary>전투 기록's 자동 저장: off, only the last fight is held (unsaved) until the next one, and 보관 saves it.</summary>
    public bool RecordAutoSave { get; set; } = true;
    /// <summary>전투 기록: how long the 전분 timer may stay stopped (a wipe) or hidden (a cutscene) before the fight ends.</summary>
    public int RecordPauseSeconds { get; set; } = 120;
    public const int MinRecordPause = 10, MaxRecordPause = 600;
    /// <summary>전투 기록's 글자 크기: the timeline's text, in % (user, 2026-11-03).</summary>
    public int RecordFontPercent { get; set; } = 100;
    public const int MinRecordFont = 100, MaxRecordFont = 200;
    /// <summary>전투 기록's names by boss max HP (<see cref="Domain.BossBook"/>): learned from the dungeon panel, editable by hand.</summary>
    public Dictionary<long, BossBookEntry> BossBook { get; set; } = [];
    /// <summary>설정 → 숙제: the characters known, their picks and renamed tags (the clears are in homework.json).</summary>
    public HomeworkSettings Homework { get; set; } = new();
    /// <summary>설정's colour theme (the window only; the overlay's colours are its own). New installs start on 로스트아크;
    /// a file without one was saved while 딥 틸 was the default (<see cref="OnDeserializing"/>).</summary>
    [JsonIgnore] public UiTheme Theme { get; set; } = UiTheme.LostArk;

    /// <summary>"Theme" in the file: one of the four themes 0.11.x knows, so going back to it keeps the settings (it can't
    /// read a name it doesn't know). A newer theme saves 딥 틸 here and itself in <see cref="Palette"/>.</summary>
    [JsonPropertyName("Theme")]
    public UiTheme SavedTheme { get => Theme > UiTheme.Dark ? UiTheme.DeepTeal : Theme; set => Theme = value; }

    /// <summary>A theme newer than 0.11.x (로스트아크, 그래파이트, 아크라시아 밤), read after "Theme"; null for the four it knows.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UiTheme? Palette { get => Theme > UiTheme.Dark ? Theme : null; set { if (value is { } v) Theme = v; } }

    /// <summary>Tray → 표시's memory for this session (fields: never saved): what turning a part off hid, which turning it
    /// on brings back (<see cref="TrayToggles"/>).</summary>
    internal (string? Key, SlotGroup[] Groups)? TrayHiddenGroups;
    internal BuffKind[]? TrayHiddenBars;

    /// <summary>Before a file's values are read: what a missing value means in a saved file, where it differs from a new install's.
    /// A file saved before the 레이드 table has no "Raids": its old 모르둠 debuff switch is carried over once (SettingsStore.Normalize).</summary>
    void IJsonOnDeserializing.OnDeserializing() => (Theme, Raids.Migrated, Raids.DebuffsMigrated, RecordKeepMigrated) = (UiTheme.DeepTeal, false, false, false);
    /// <summary>The 아덴 게이지 bar per "클래스/직업각인" (GaugeBar.Key): only the ones changed in the 게이지 tab; the
    /// rest use the classes.json default.</summary>
    public Dictionary<string, GaugeBarSettings> GaugeBars { get; set; } = [];

    /// <summary>Per-character choices, keyed by the character's display label.</summary>
    public Dictionary<string, CharacterSettings> Characters { get; set; } = [];
    public string? ActiveCharacter { get; set; }

    /// <summary>코어 빌드 per "클래스 · 직업각인" (<see cref="CharacterKey"/>): kept when the character changes, one for all three
    /// 프로필 and apart from the active character. Read through <see cref="CoreOf"/>, set through <see cref="SetCore"/>.</summary>
    public Dictionary<string, CoreChoice> Cores { get; set; } = [];

    /// <summary>Slots learned as stack skills per "클래스 · 직업각인" (<see cref="CharacterKey"/>): a count seen at 스킬창 맞추기 or
    /// beside timers in play. Never forgotten; a stack can come from the skill, a tripod or a core (user, 2026-10-13).
    /// Slot names, not SlotId: a name that isn't one (a hand edit) is skipped, where an enum would fail the whole file.</summary>
    public Dictionary<string, List<string>> StackSkills { get; set; } = [];

    /// <summary>★ classes: a class name stars all its 직업각인, a "클래스 · 직업각인" entry stars that one. Listed first in the pickers.</summary>
    public List<string> FavoriteClasses { get; set; } = [];

    /// <summary>★ settings (<see cref="SettingEntry.Id"/>), shown on 즐겨찾기 in the order starred.</summary>
    public List<string> FavoriteSettings { get; set; } = [];

    /// <summary>The 직업각인 last played per class (picked by hand or read off the bar): a class detected with one takes it
    /// and its 직업각인 isn't read again until asked (직업각인 다시 감지).</summary>
    public Dictionary<string, string> SubclassHistory { get; set; } = [];

    /// <summary>로딩 뒤 직업각인 자동 감지 (user, 2026-11-03; off by default, every class): after a loading screen the ready 초각스
    /// sets the 직업각인 only (CharacterService.AutoRedetect).</summary>
    public bool AutoSubclass { get; set; }

    /// <summary>설정 → 캐릭터's supported 직업각인 and features the user has seen (Core.Data.ClassSupport keys): anything shipped
    /// later shows NEW until the tab is opened. Null: never recorded (set at start-up, so a first run marks nothing).</summary>
    public List<string>? SeenSupport { get; set; }

    /// <summary>Skill-bar fit per viewport size, keyed "W×H" (Geometry.ViewportKey).</summary>
    public Dictionary<string, Fit> Fits { get; set; } = [];

    /// <summary>크기 직접 입력: the game's HUD size (%) that 스킬창 맞추기 uses instead of reading it off the HP bar; null reads it.
    /// One of <see cref="HudSizes"/>.</summary>
    public double? ManualHud { get; set; }

    /// <summary>직접 입력 (on while <see cref="ManualHud"/> is set): the game's 해상도 and forced 21:9, which then give the game area
    /// instead of the black bars (Geometry.ManualViewport).</summary>
    public ManualScreen? ManualScreen { get; set; }

    /// <summary>What the last automatic 스킬창 맞추기 found: the game window's size and whether it had 21:9 bars. 직접 입력 starts from it.</summary>
    public ManualScreen? DetectedScreen { get; set; }

    /// <summary>수동 조정: fits moved and scaled by hand in the debug window and applied (적용), per game-area size (ViewportKey).
    /// Used only while <see cref="UseAdjusts"/>; kept when another mode is picked.</summary>
    public Dictionary<string, Fit> Adjusts { get; set; } = [];
    public bool UseAdjusts { get; set; }

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

    /// <summary>The 직업각인 whose window spot and size <see cref="Overlay"/> holds now; null = the one shared by a character with no listed 직업각인.</summary>
    public string? PlaceKey { get; set; }
}
