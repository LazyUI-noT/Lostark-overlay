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
    /// <summary>표시 화면 and 창 밖에서도 읽기: for the install, not per profile.</summary>
    public ScreenSettings Screens { get; set; } = new();
    public GaugeStyle Gauge { get; set; } = new();
    /// <summary>기력·충격 창 (브레이커's ring).</summary>
    public RingSettings Ring { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public PartySettings Party { get; set; } = new();
    public BuffSettings Buffs { get; set; } = new();
    /// <summary>합쳐서 / 따로: false = skill boxes, gauge and buff bars in one window, true = each in its own. The one value
    /// <c>Buffs.Detached</c> (per profile and class in <see cref="ProfileEntry.BuffsDetached"/>), read by all three panels.</summary>
    [JsonIgnore] public bool Apart { get => Buffs.Detached; set => Buffs.Detached = value; }
    public DebuffSettings Debuffs { get; set; } = new();
    public JudgmentSettings Judgment { get; set; } = new();
    /// <summary>설정 → 암흑수류탄: the 암수 창 (amsu.design.md).</summary>
    public AmsuSettings Amsu { get; set; } = new();
    /// <summary>초당 읽기 횟수 (ReadSteps): the read loop's steps a second, 5–30.</summary>
    public int ReadsPerSecond { get; set; } = ReadSteps.Default;
    /// <summary>초당 그리기 횟수 (DrawSteps): the overlays' redraws a second while something moves, 10–60.</summary>
    public int DrawsPerSecond { get; set; } = DrawSteps.Default;
    public UpdateSettings Updates { get; set; } = new();
    /// <summary>설정 → 레이드: the 줄 수 창, boss recognition and each gate's switches (project B).</summary>
    public RaidSettings Raids { get; set; } = new();
    /// <summary>설정 → DPS 미터기 (project C).</summary>
    public MeterSettings Meter { get; set; } = new();
    /// <summary>전투 기록: how many automatic records to keep (1–50, RecordStore clamps); 보관 ones don't count.</summary>
    public int RecordKeep { get; set; } = 50;
    /// <summary>A saved <see cref="RecordKeep"/> of 20 (the default before 전투 기록 went per character) has moved to 50 once.</summary>
    public bool RecordKeepMigrated { get; set; } = true;
    /// <summary>False in a file from before 0.23: <see cref="SplitLayout.Migrate"/> runs once on it (a new install needs none).</summary>
    public bool SplitMigrated { get; set; } = true;
    /// <summary>False in a file from before 0.20: its fights past the new count turn 보관 once at start-up (RecordStore.KeepOverflow).</summary>
    public bool RecordOverflowKept { get; set; } = true;
    /// <summary>전투 기록's rows: 날짜순 (newest first) or 레이드순 (RaidOrder.Group) (user, 2026-11-10).</summary>
    public bool RecordSortByRaid { get; set; }
    /// <summary>레이드순's raid order as the user set it (숙제 raid ids); raids not in it follow, highest entry level first.</summary>
    public List<string> RecordRaidOrder { get; set; } = [];
    /// <summary>휴지통 비우기 (user, 2026-11-10): the day and hour (0–23, PC clock) the bin empties every week.</summary>
    public DayOfWeek BinEmptyDay { get; set; } = BinSchedule.DefaultDay;
    public int BinEmptyHour { get; set; } = BinSchedule.DefaultHour;
    /// <summary>The last emptying run, or when the day or hour was changed (<see cref="BinSchedule.Due"/>).</summary>
    public DateTime BinEmptiedAt { get; set; }
    /// <summary>설정 → 경고: the warnings the user turned off (다시 보지 않기, or the page's switch).</summary>
    public HashSet<WarningKind> MutedWarnings { get; set; } = [];
    /// <summary>설정 → 경고 → Windows 알림: the kinds of tray notice turned off (<see cref="Notices.Shows"/>); empty = all on.</summary>
    public HashSet<NoticeKind> MutedNotices { get; set; } = [];
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
    /// <summary>The difficulty capsules' colours (RaidBadge), one palette for every page that shows them; local display only.</summary>
    public RaidCapsuleColors RaidCapsules { get; set; } = new();
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

    /// <summary>Before a file's values are read: what a missing value means in a saved file, where it differs from a new install's.
    /// A file saved before the 레이드 table has no "Raids": its old 모르둠 debuff switch is carried over once (SettingsStore.Normalize).</summary>
    void IJsonOnDeserializing.OnDeserializing() =>
        (Theme, Raids.Migrated, Raids.DebuffsMigrated, RecordKeepMigrated, RecordOverflowKept, HomeworkWithoutKey, CooldownLooksMoved, SplitMigrated) = (UiTheme.DeepTeal, false, false, false, false, true, false, false);

    /// <summary>The global 쿨 진행 표시 & co. (<see cref="OverlaySettings.CooldownStyle"/>) have moved into 기본 and every 개별
    /// class (<see cref="ClassProfileMigration.MoveCooldownViews"/>); false in an older file.</summary>
    public bool CooldownLooksMoved { get; set; } = true;

    /// <summary>숙제 without an API key: kept by installs from before the key gate (a file without this value), not by new ones
    /// (settings-access.plan.md B, user 2026-11-11).</summary>
    public bool HomeworkWithoutKey { get; set; }

    /// <summary>Whether 숙제 runs (tab, in-game tags, clears kept): with a saved key, or on an install from before the gate.</summary>
    public bool HomeworkOn(bool hasKey) => hasKey || HomeworkWithoutKey;
    /// <summary>The 아덴 게이지 bar per "클래스/직업각인" (GaugeBar.Key): only the ones changed in the 게이지 tab; the
    /// rest use the classes.json default.</summary>
    public Dictionary<string, GaugeBarSettings> GaugeBars { get; set; } = [];

    /// <summary>Per-character choices, keyed by the character's display label.</summary>
    public Dictionary<string, CharacterSettings> Characters { get; set; } = [];
    public string? ActiveCharacter { get; set; }

    /// <summary>코어 빌드 per "클래스 · 직업각인" (<see cref="CharacterKey"/>): kept when the character changes, one for all three
    /// 프로필 and apart from the active character. Read through <see cref="CoreOf"/>, set through <see cref="SetCore"/>.</summary>
    public Dictionary<string, CoreChoice> Cores { get; set; } = [];

    /// <summary>The best 전투력 per character × 직업각인 from the Lost Ark API (<see cref="CombatPowerBook"/>): character → 직업각인
    /// without spaces ("" = none) → value. No key or secret here: the API key lives in Windows Credential Manager only.</summary>
    public Dictionary<string, Dictionary<string, double>> CombatPower { get; set; } = [];

    /// <summary>설정 → 로아 API's 대표 캐릭터: the exact name the 원정대 lookup starts from (<see cref="ApiRoster.Root"/>); null = the
    /// character read off the 게임 메뉴.</summary>
    public string? ApiMainCharacter { get; set; }

    /// <summary>The buff lock's unlock key as entered (<see cref="Domain.BuffKey"/>), checked again at every start; null: locked.
    /// The key itself, never an "unlocked" flag (buff-lock.design.md).</summary>
    public string? BuffUnlockKey { get; set; }

    /// <summary>설정 → 로아 API: each character's best 전투력 on the 게임 메뉴 (<see cref="MenuPower"/>). Apart from
    /// <see cref="HomeworkSettings.ShowOnGrid"/>: hiding the 숙제 tags keeps these.</summary>
    public bool ApiShowCombatPower { get; set; } = true;

    /// <summary>The meter's 비교 fight picked with 「미터기 비교」 (<see cref="Domain.MeterCompare"/>): "character|gate|difficulty" →
    /// the record's file name. A raid not here, or whose file is gone, compares with its most recent fight (자동).</summary>
    public Dictionary<string, string> MeterCompare { get; set; } = [];

    /// <summary>표시 안 함 per "클래스 · 직업각인" ("" = 기본) for the windows shown per character (user, 2026-11-05): buff bars and
    /// the DPS 미터기. Read through <see cref="ClassProfiles.WindowsOf"/>.</summary>
    public Dictionary<string, CharacterWindows> CharacterWindows { get; set; } = [];

    /// <summary>Slots learned as stack skills per "클래스 · 직업각인" (<see cref="CharacterKey"/>): a count seen at 스킬창 맞추기 or
    /// beside timers in play. Never forgotten; a stack can come from the skill, a tripod or a core (user, 2026-10-13).
    /// Slot names, not SlotId: a name that isn't one (a hand edit) is skipped, where an enum would fail the whole file.</summary>
    public Dictionary<string, List<string>> StackSkills { get; set; } = [];

    /// <summary>★ classes: a class name stars all its 직업각인, a "클래스 · 직업각인" entry stars that one. Listed first in the pickers.</summary>
    public List<string> FavoriteClasses { get; set; } = [];

    /// <summary>★ settings (<see cref="SettingEntry.Id"/>), shown on 즐겨찾기 in the order starred.</summary>
    public List<string> FavoriteSettings { get; set; } = [];

    /// <summary>The 직업각인 last played per class (picked by hand or read off the bar): a class detected with one takes it
    /// and its 직업각인 isn't read again until asked (직업각인 다시 읽기).</summary>
    public Dictionary<string, string> SubclassHistory { get; set; } = [];

    /// <summary>The old 로딩 뒤 직업각인 자동 감지: on migrates once to <see cref="ClassDetect"/> 자동 (user, 2026-11-05; SettingsStore.Normalize).</summary>
    public bool AutoSubclass { get; set; }

    /// <summary>클래스 인식 끄기 / 수동 / 자동 (class-detect.plan.md).</summary>
    public ClassDetectMode ClassDetect { get; set; } = ClassDetectMode.수동;

    /// <summary>설정 → 캐릭터's supported 직업각인 and features the user has seen (Core.Data.ClassSupport keys): anything shipped
    /// later shows NEW until the tab is opened. Null: never recorded (set at start-up, so a first run marks nothing).</summary>
    public List<string>? SeenSupport { get; set; }

    /// <summary>The rail's orbs (<see cref="RailNews"/>): per tab, the keys seen when it was last opened. Null: never recorded
    /// (set when 설정 is built, so a first run lights nothing).</summary>
    public Dictionary<string, List<string>>? RailSeen { get; set; }

    /// <summary>설정 pages' 미리보기 배경화면: null = the 배치's saved game screen (else the sample screen), "#RRGGBB" = one
    /// colour, anything else = a picture file the user picked (user, 2026-11-10).</summary>
    public string? PreviewBack { get; set; }

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

    /// <summary>값 직접 조정 (user, 2026-11-05): the % sizes (HUD, 버프, 타겟 UI 버프 (보스)) take any whole value by − +, not only the
    /// game's presets. Off in normal play; 테스트 모드 always has it on (a video may show any size). <see cref="SetFineSizes"/>.</summary>
    public bool FineSizes { get; set; }

    /// <summary>Per class (ClassInfo.Name, or <see cref="ClassProfiles.Template"/> for a class without its own): colours
    /// learned from its icons at a fit, and what to show, where and in what colours, for you and for party members. See
    /// <see cref="ClassProfiles"/> for the order they apply in.</summary>
    public Dictionary<string, ClassSettings> Classes { get; set; } = [];

    /// <summary>프로필 1–5: each keeps a screenshot, every window's spot and size and every look (<see cref="OverlayProfile"/>).
    /// The active one's values live in the settings above (so nothing else reads profiles); <see cref="SwitchProfile"/> swaps
    /// them. Replaced on load, not added to.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<OverlayProfile> Profiles { get; set; } = [.. Enumerable.Range(0, OverlayProfile.Count).Select(_ => new OverlayProfile { Classes = [], GaugeBars = [], Aden = [] })];
    public int ActiveProfile { get; set; }
    /// <summary>Old (0.20–0.22) 모든 위치 프로필에 적용 marks: read once into every profile (<see cref="UnshareLooks"/>), then
    /// written empty, so an older version sees every look per profile too. Replaced on load, not added to.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public HashSet<LookGroup>? SharedLooks { get; set; } = [];

    /// <summary>The 클래스 · 직업각인 whose places and looks the live settings hold now (<see cref="SyncClass"/>); null = the
    /// profile's shared ones. The JSON name is 0.22's, which kept only the window's spot per 직업각인 under it.</summary>
    public string? PlaceKey { get; set; }
}
