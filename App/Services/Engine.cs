using System.Diagnostics;
using System.IO;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>The pipeline (ARCHITECTURE §2) on its own thread: capture only the due slot areas (L2) → read them (L1)
/// → track cooldowns (L0) → publish an immutable <see cref="Snapshot"/>.
/// All events are raised on the engine thread; the UI marshals them.</summary>
public sealed partial class Engine : IDisposable
{
    /// <summary>What the engine needs from the settings, handed over as one immutable value (no shared mutable state).</summary>
    /// <param name="AdenRects">Where the class draws 아덴1/아덴2 when not at the usual spots (reference pixels).</param>
    /// <param name="AdenDark">The 아덴 skills are case 2, and how they show it: dark = <see cref="SlotPhase.Unavailable"/>.</param>
    /// <param name="GaugeRule">The 직업각인's gauge rule; its <see cref="GaugeRule.Spend"/> skills take full sections when used.</param>
    /// <param name="Character">The active character ("클래스 · 직업각인"): whose learned gains (gains.json) are used and added to.</param>
    /// <param name="ManualHud">직접 입력: the HUD size 스킬창 맞추기 keeps, instead of reading it off the HP bar.</param>
    /// <param name="ManualScreen">직접 입력: the game's 해상도 and 21:9, which place the game area without the black-bar probe.</param>
    /// <param name="Adjusts">수동 조정 (null: another mode): fits applied in the debug window, tried before <paramref name="Fits"/>.</param>
    /// <param name="CoreWatches">코어 빌드, worked out when the config is built: the slots to read on every frame after a cast (null: none).
    /// A hidden, unsent slot (<see cref="ReadPlan.Skip"/>) is never read: as the target its reset isn't tracked, and as the trigger its cast is
    /// never seen, so nothing starts.</param>
    /// <param name="Judgment">단심 (설정 → 기타), when on for <paramref name="Character"/>: what to read it from (null: off).</param>
    /// <param name="StackSkills">The slots <paramref name="Character"/> has shown a stack count on (Settings.StackSkills): known before
    /// the first cast, so its re-use lockout's "1s" isn't taken for a 1 s cooldown (null: none, or another class on screen).</param>
    /// <param name="BuffCount">버프 읽을 칸 수: how many buff boxes are read, from the row's right end.</param>
    /// <param name="BossSize">The game's 보스 디버프 크기 (%), apart from <paramref name="DebuffSize"/> (버프 크기, your own rows).</param>
    /// <param name="Raids">설정 → 레이드, a copy (RaidSettings.Clone); null reads nothing for it.</param>
    /// <param name="RaidGates">The gates (Profiles/raids.json) and <paramref name="BossNames"/> their name templates (Profiles/bossnames).</param>
    public sealed record Config(IReadOnlyDictionary<string, Fit> Fits, IReadOnlyCollection<SlotId> AdenSlots, bool Debug, ReadPlan Reads,
        GaugeDef? Gauge = null, IReadOnlyDictionary<SlotId, PixelRect>? AdenRects = null, DarkCue AdenDark = DarkCue.None, GaugeRule? GaugeRule = null,
        GaugeReadMode ReadMode = GaugeReadMode.Agreed, string? Character = null, int DebuffSize = 100, double? ManualHud = null,
        ManualScreen? ManualScreen = null, IReadOnlyDictionary<string, Fit>? Adjusts = null, IReadOnlyList<CoreWatch>? CoreWatches = null,
        IReadOnlyCollection<SlotId>? StackSkills = null, JudgmentPlan? Judgment = null, int BuffCount = Geometry.BuffBoxCount, int BossSize = Geometry.RefBossSize,
        RaidSettings? Raids = null, IReadOnlyList<RaidGate>? RaidGates = null, IReadOnlyList<(string GateId, Image Template)>? BossNames = null,
        bool MeterOn = false, IReadOnlyDictionary<string, PixelRect>? MeterPanels = null, bool AmsuOn = false,
        AmsuRate AmsuRate = AmsuRate.높음)
    {
        /// <summary>직접 입력: the 해상도 and 21:9 given, or the game window's size while no 해상도 is picked; null when off.</summary>
        public (double Hud, ManualScreen Screen)? Manual(PixelRect client) =>
            ManualHud is { } hud ? (hud, ManualScreen ?? new(client.Width, client.Height, false)) : null;
    }

    /// <summary>One slot read, for the debug window.</summary>
    public sealed record DebugRow(SlotId Slot, Image Crop, Reading Reading, SlotState State);
    /// <summary>The debug window's row for a buff or debuff box or the boss's counts: its crop, what was read, what is shown.</summary>
    public sealed record ExtraDebugRow(string Key, Image Crop, string Reading, string State);

    public event Action<Snapshot>? SnapshotReady;
    public event Action<bool>? GameRunningChanged;
    /// <summary>True while the skill bar is on screen; false during loading screens and cutscenes.</summary>
    public event Action<bool>? HudVisibleChanged;
    /// <summary>No saved fit for this game-area size: the user must run 스킬창 맞추기.</summary>
    public event Action<Viewport>? FitNeeded;
    /// <summary>Result of <see cref="RequestFit"/>: a fit (null on failure), a message for the user, and on a fit found
    /// without 직접 입력 the 해상도 and 21:9 it was found at (직접 입력 starts from them); then 테스트 모드 (never saved), and whether
    /// 되돌리기 (<see cref="RevertTestFit"/>) can undo it (a 테스트 모드 fit request's fine-tune).</summary>
    public event Action<Viewport, Fit?, string, ManualScreen?, bool, bool>? FitFinished;
    /// <summary>The class emblem, captured on the first frame the skill bar is certainly on screen after <see cref="RequestDetect"/>
    /// or <see cref="RequestEmblem"/> (스킬창 다시 맞추기, a character picked by hand, 테스트 모드). Never on its own.</summary>
    public event Action<Image>? EmblemCaptured;
    /// <summary>The 초각스 slot on its first ready read, with the bar certainly on screen, after <see cref="RequestDetect"/> or
    /// <see cref="RequestSubclass"/> (스킬창 다시 맞추기, 직업각인 다시 감지, 테스트 모드). Never on its own. Its icon tells the
    /// 직업각인 (SubclassMatcher). With it, when the whole bar is greyed out (SubclassMatcher.Greyed), the 일반 slots' last
    /// crops (SkillPool); else null.</summary>
    public event Action<Image, IReadOnlyList<Image>?>? SubclassSlotCaptured;
    /// <summary>Result of <see cref="RequestSkills"/>: the 일반 slots ready now, or null when the bar is greyed out.</summary>
    public event Action<IReadOnlyList<Image>?>? SkillsCaptured;
    /// <summary>A skill's icon as the skill bar shows it when ready (아이콘으로 표시): while a copy run is on
    /// (<see cref="CopyIcons"/>), once per slot at its first ready read. Never while the HUD is hidden or the bar is greyed.
    /// The int is the run it was read in (<see cref="CopyIcons"/>'s result), so a read posted before a new run can be dropped.</summary>
    public event Action<SlotId, Image, int>? IconSeen;
    /// <summary>A slot of the character ("클래스 · 직업각인") showed a stack count: at 스킬창 맞추기 while ready, or in play
    /// (<see cref="StackLearner"/>). Raised for a slot not yet in <see cref="Config.StackSkills"/>.</summary>
    public event Action<string, SlotId>? StackSkillLearned;
    public event Action<DebugRow>? Debug;
    /// <summary>Like <see cref="Debug"/>, for the buff and debuff boxes and the boss's counts (only while the debug window is open).</summary>
    public event Action<ExtraDebugRow>? ExtraDebug;
    /// <summary>게임 화면 찍기: the game's client area (on its monitor) and where it lies on screen; null picture when there's
    /// no game or no frame came.</summary>
    public event Action<object, Image?, PixelRect>? ScreenshotCaptured;

    static readonly TimeSpan LabelEvery = TimeSpan.FromMilliseconds(250), PollEvery = TimeSpan.FromMilliseconds(100),
        ViewportEvery = TimeSpan.FromSeconds(2);
    static readonly TimeSpan ShotWait = TimeSpan.FromSeconds(2);
    const int FitWaitSteps = 20, TaskbarSettleMs = 500;   // 스킬창 맞추기: up to 2 s for the game to come to the front

    readonly Thread _thread;
    volatile bool _stop, _fitRequested, _detectRequested, _emblemRequested, _subclassRequested, _gainsReset, _iconsStale, _copyIcons, _foreground = true;
    volatile Config _config = new(new Dictionary<string, Fit>(), [], false, new(new HashSet<SlotId>(), true, null, false));
    TimeSpan? _castAt;   // the last cast seen on screen: the gauge is read every frame just after it (GaugePace)

    readonly ReadScheduler _scheduler = new();
    readonly HudVote _hud = new();
    readonly SlotState[] _states = [.. SlotInfo.All.Select(SlotState.Unknown)];
    readonly bool[] _iconFresh = new bool[SlotInfo.All.Length];   // IconSeen already sent in this copy run
    const double MinHueSpread = 20;   // below: one dark tint (town, silenced), a poor picture of the skill
    BuffState[] _buffs = [.. Enum.GetValues<BuffKind>().Select(k => BuffState.Absent(k, TimeSpan.Zero))];
    bool _buffsOn, _gaugeOn, _debuffsOn;   // planned this step (Publish reads these, not the live config)
    DebuffStatus _debuffs = DebuffStatus.None;
    TimeSpan _lastBoss = TimeSpan.MinValue;
    static readonly TimeSpan BossEvery = TimeSpan.FromMilliseconds(500);   // the boss's counts change only on a swap
    // Debuff icons: 3 a second with none up, 5 while one is (boxes filled from the left, plus the next); the 결투자's timer
    // 10 a second in between, its ticks putting the countdown back to when it really came (DebuffTracker.DuelTimer).
    DebuffSlots _slots = DebuffSlots.None;   // your debuff row at the last read and when each pass ran (DebuffSchedule: cadence v2)
    double? _gauge;
    GaugeDef? _gaugeFrom;      // the definition _gauge was read with (one instance per 직업각인: CharacterService caches it)
    readonly GaugeFilter _gaugeFilter = new();   // drops the shine the game sweeps over the gauge
    readonly ImmediateSpend _spender = new();   // which presses drop the gauge at once (도화가, 바드)
    bool _gaugeSpent;   // the gauge was set by a press: publish it without waiting for a read
    GaugeGains _gains = new();   // what each cast adds to the gauge (GaugeReadMode.Predicted, beta), for _gainsFor
    readonly GaugeGainsStore _gainStore = new(GaugeGainsStore.DefaultPath);
    Dictionary<string, IReadOnlyDictionary<string, Gain>>? _savedGains;   // gains.json, read at the first character
    string? _gainsFor;
    readonly StackLearner _stackLearner = new();
    string? _stacksFor;   // whose casts _stackLearner counts
    int _gainsSaved;   // _gains.Version last written
    TimeSpan _gainsSavedAt = TimeSpan.MinValue;
    static readonly TimeSpan GainsSaveEvery = TimeSpan.FromMinutes(1);
    string? _adenFile;   // the 아덴2 calibration file the 아덴 calibrations were loaded for (class, spots, 수동 조정)
    readonly SlotCalibration?[] _cal = new SlotCalibration?[SlotInfo.All.Length];
    readonly Dictionary<PixelRect, Image> _buffers = [];
    ICaptureSource? _source;
    IntPtr _hwnd, _monitor;
    PixelRect _client;
    Viewport _vp;
    Fit? _fit;
    DigitReader? _digits;
    TimeSpan _lastViewport = TimeSpan.MinValue, _lastLabel = TimeSpan.MinValue, _lastPoll = TimeSpan.MinValue, _lastGauge = TimeSpan.MinValue;   // MinValue = never (see Elapsed)
    bool _awakeningChanged, _labelMissed, _running, _fitNeededRaised, _emblemPending, _subclassPending, _testMode;
    // 테스트 모드: fits found on the video in front, memory only; _quietFit while one is looked for unasked (no failure notices).
    readonly Dictionary<IntPtr, TestFits> _testFitsBy = [];   // per picked window: another video of the same size is its own
    /// <summary>테스트 모드's memory for the window followed now (engine thread only).</summary>
    TestFits _testFits => _testFitsBy.TryGetValue(_hwnd, out var f) ? f : _testFitsBy[_hwnd] = new();
    bool _vpPinned;   // 테스트 모드 after 적용: the game area stays on these screen pixels (a video's dark scene or a moved window doesn't shift it)
    bool _quietFit;
    bool _adjusted;   // the fit in use is a 수동 조정 (Config.Adjusts): its calibration files are its own
    string? _areaKey;

    /// <summary>The game area's size key ("W×H", Settings.Fits / Adjusts), null before one is measured; any thread.</summary>
    public string? GameAreaKey => Volatile.Read(ref _areaKey);
    // Each 일반 slot's colour and crop at its last read (NaN, null until read): a greyed-out bar (a town) and its skills, sent with
    // the 초각스 crop; the ready ones for 스킬 등록.
    readonly double[] _skillColour = [.. Enumerable.Repeat(double.NaN, 8)];
    readonly Image?[] _skillCrops = new Image?[8];
    volatile bool _skillsRequested;

    public Engine() => _thread = new Thread(Loop) { IsBackground = true, Name = "GaugeOverlay engine", Priority = ThreadPriority.AboveNormal };

    public void Start() => _thread.Start();

    public void Configure(Config config) => _config = config;

    /// <summary>Whether the game window is in the foreground (set from ForegroundWatcher; true until told otherwise).
    /// Desktop Duplication captures whatever is on top, so ready icons are learned only while this is true.</summary>
    public bool GameForeground { get => _foreground; set => _foreground = value; }

    volatile bool _seesBehind;   // the capture source reads the window even while covered (테스트 모드's WGC)

    /// <summary>The capture reads the game window even behind others (테스트 모드 on a window WGC can capture): App needn't bring
    /// it to the front for a fit, a revert or 적용, which would show a video player's controls over the skill bar.</summary>
    public bool CanCaptureBehind => _seesBehind;

    /// <summary>A capture now shows the game: it is in front, or the source sees behind other windows.</summary>
    bool CanRead => _foreground || _seesBehind;

    /// <summary>Runs 스킬창 맞추기 on the next frame. The user should be in the 수련장 with every cooldown reset.</summary>
    public void RequestFit() => _fitRequested = true;

    /// <summary>테스트 모드's 되돌리기: the fit in use before the last fit request comes back on the next frame.</summary>
    public void RevertTestFit() => _revertRequested = true;

    volatile bool _revertRequested;

    object? _shotFor;   // the pending 게임 화면 찍기 request's token

    /// <summary>게임 화면 찍기 on the next frame (no fit needed): answered by <see cref="ScreenshotCaptured"/> with
    /// <paramref name="token"/>, so an answer that comes after its asker gave up can't reach the next asker.</summary>
    public void RequestScreenshot(object token) => Volatile.Write(ref _shotFor, token);

    /// <summary>스킬창 다시 맞추기 / 테스트 모드: captures the class emblem and the next ready 초각스 slot once.</summary>
    public void RequestDetect() => _detectRequested = true;

    /// <summary>스킬 등록: the 일반 slots ready on the next frame the bar is certainly on screen (<see cref="SkillsCaptured"/>).</summary>
    public void RequestSkills() => _skillsRequested = true;

    /// <summary>스킬 등록 gave up (no bar in time): a later frame mustn't answer the next request early.</summary>
    public void CancelSkills() => _skillsRequested = false;

    /// <summary>After a character is picked by hand: capture the emblem (learned for it) but not the 직업각인, so the
    /// pick holds until the next 스킬창 다시 맞추기.</summary>
    public void RequestEmblem() => _emblemRequested = true;

    /// <summary>직업각인 다시 감지: the next ready 초각스 is handed to <see cref="SubclassSlotCaptured"/>, with no refit.</summary>
    public void RequestSubclass() => _subclassRequested = true;

    /// <summary>배운 값 지우기: forgets what the active character's casts were learned to add (gains.json too).</summary>
    public void ResetGains() => _gainsReset = true;

    /// <summary>Starts (true) or ends a copy run: while on, every slot's icon is copied once, at its next ready read
    /// (<see cref="IconSeen"/>). Started by 아이콘 다시 읽기, or while a 직업각인's saved icons are missing. Returns the run's
    /// number, which each <see cref="IconSeen"/> of the run carries.</summary>
    public int CopyIcons(bool on)
    {
        int run = on ? Interlocked.Increment(ref _iconRun) : Volatile.Read(ref _iconRun);
        if (on) _iconsStale = true;
        _copyIcons = on;
        return run;
    }
    int _iconRun;   // CopyIcons runs started; read once per ReadSlots, before _iconsStale

    void Loop()
    {
        while (!_stop)
        {
            try { Step(); }
            catch (Exception ex)
            {
                Trace.WriteLine($"[engine] {ex}");
                ResetSource();
                Thread.Sleep(500);
            }
        }
        ResetSource();
        SaveGains();
    }

    void Step()
    {
        if (_gainsReset) { _gainsReset = false; ForgetGains(); }   // also with no game running
        if (!EnsureGame())
        {
            if (_fitRequested)
            {
                _fitRequested = false;
                FitFinished?.Invoke(_vp, null, "게임이 실행 중이 아닙니다. 게임을 켜고 다시 시도해 주세요.", null, _testMode, false);
            }
            if (_revertRequested)
            {
                _revertRequested = false;   // never kept for a video opened later
                FitFinished?.Invoke(_vp, null, "고른 창이 보이지 않아 되돌리지 못했습니다.", null, _testMode, false);
            }
            if (Interlocked.Exchange(ref _shotFor, null) is { } missed) ScreenshotCaptured?.Invoke(missed, null, default);
            Thread.Sleep(1000);
            return;
        }
        if (Interlocked.Exchange(ref _shotFor, null) is { } token) { TakeScreenshot(token); return; }
        var now = Clock.Now();

        // 테스트 모드: a moved or resized player window is followed at once (the kept picture moves with it, no capture).
        if (Elapsed(now, _lastViewport, ViewportEvery) || _testMode && GameWindow.ClientRect(_hwnd) is { } c && c != _client) UpdateViewport(now);
        if (_fitRequested)
        {
            (_fitRequested, _revertRequested) = (false, false);   // a new fit replaces what 되돌리기 would have put back
            var kept = _vp;
            _previewing = false;   // the debug window's preview starts over on the new fit
            UpdateViewport(now, fitting: true);
            if (!RunFit() && _vp != kept) (_vp, _fit) = (kept, null);   // a failed fit keeps the game area its saved fit is for
            return;
        }
        if (_revertRequested) { _revertRequested = false; RevertFit(); return; }
        if (!EnsureFit()) { Thread.Sleep(200); return; }
        PreviewStep();
        if (_fit is null) return;   // a preview just ended: the saved fit is resolved next step
        if (_testMode) StepTestBuffSize(now);
        // Detection runs only when asked (스킬창 다시 맞추기, 테스트 모드, a pick by hand), never on its own: after a raid
        // cutscene it used to pick the wrong class.
        // Both in one tick: the full detection wins.
        if (_emblemRequested) { _emblemRequested = false; _emblemPending = true; _subclassPending = false; }
        if (_detectRequested) { _detectRequested = false; _emblemPending = _subclassPending = true; }
        if (_subclassRequested) { _subclassRequested = false; _subclassPending = true; }

        var config = _config;
        // A slot or label that is neither shown nor sent isn't read (ClassProfiles.Reads).
        var slots = _scheduler.Due(now, _states).Where(s => !config.Reads.Skip.Contains(s)).ToList();
        bool wantLabel = config.Reads.Label && Elapsed(now, _lastLabel, LabelEvery);
        // The buff row is polled every 100 ms and the gauge by GaugePace, whether or not a slot is due.
        bool poll = Elapsed(now, _lastPoll, PollEvery);
        var gaugeDef = config.Reads.Gauge ? config.Gauge : null;
        if (config.Gauge != _gaugeFrom) { (_gaugeFrom, _gauge) = (config.Gauge, null); _gaugeFilter.Reset(); _spender.Reset(); }   // another character: not its value
        if (config.Character != _gainsFor) SwitchGains(config.Character);
        if (config.Character != _stacksFor) { _stacksFor = config.Character; _stackLearner.Reset(); }
        SwitchJudgment(config, now);
        StepPresence(now);
        _gaugeFilter.Mode = config.ReadMode;
        // Another class: its own 아덴 spots and ready looks (the file name holds both, SlotCalibration.FileName).
        if (CalibrationFile(SlotId.아덴2, config) is var aden && aden != _adenFile) { _adenFile = aden; ReloadAden(config); }
        (_buffsOn, _gaugeOn, _debuffsOn) = (config.Reads.BuffSize is not null, gaugeDef is not null, DebuffRaid(config) != Raid.없음);
        // 위치 조정 tries a 버프 크기 live: both rows read at it until 적용 stores it in 설정 or the panel closes.
        int? previewSize = _previewing ? _previewBuffSize : null;
        // 테스트 모드: the 버프 크기 위치 조정 applied for the video (memory only), else 설정's.
        int? testSize = _testMode ? _testFits.BuffSize : null;
        int? buffSize = poll && config.Reads.BuffSize is { } set ? previewSize ?? testSize ?? set : null;
        int debuffSize = previewSize ?? testSize ?? config.DebuffSize;
        // Debuff cadence v2: a frame pass 5/s (the filled boxes and the next), a timer pass 10/s in between for the timers under 2 s.
        var debuffPass = _debuffsOn ? DebuffSchedule.Due(_slots, now) : RowPass.None;
        bool debuffIcons = debuffPass == RowPass.Frames;
        IReadOnlyList<int> fastTimers = debuffPass == RowPass.Timers ? DebuffSchedule.FastTimers(_slots) : [];
        bool debuffs = debuffIcons || fastTimers.Count > 0, boss = debuffIcons && Elapsed(now, _lastBoss, BossEvery);
        int debuffBoxes = DebuffSchedule.Boxes(_slots);
        if (!Elapsed(now, _lastGauge, _gaugeFilter.Armed(now) ? GaugePace.Fast : GaugePace.Every(now, _castAt))) gaugeDef = null;
        bool preview = _previewing && Elapsed(now, _lastPreview, PreviewEvery);
        var (judgRow, judgBand) = JudgmentRects(config.Judgment, poll, now);
        bool judging = judgRow is not null || judgBand is not null;
        var (raidName, raidCount, raidHp) = RaidRects(config, poll, now);
        bool raiding = raidName is not null || raidCount is not null;
        var (meterHp, meterPanel, dungeonRect, searchRect) = MeterRects(config, now);
        raiding |= meterHp is not null;
        var (bossProbe, meterProbe) = (BossProbeRect(config, now), MeterProbeRect(config, now));
        bool sideReads = dungeonRect is not null || searchRect is not null || bossProbe is not null || meterProbe is not null;   // ride on this step's capture: no frame wait of their own
        if (slots.Count == 0 && !wantLabel && buffSize is null && gaugeDef is null && !debuffs && !preview && !judging && !raiding && !sideReads)
        {
            if (Advance(now) | _gaugeSpent) { _gaugeSpent = false; Publish(now); }
            Thread.Sleep(4);
            return;
        }

        var rects = new List<PixelRect>(slots.Count + 3);
        foreach (var s in slots) rects.Add(Rect(s, _fit!, config));
        if (wantLabel) rects.Add(Geometry.ToScreen(Geometry.RefAwakeningLabel, _vp, _fit!));
        if (gaugeDef is not null) rects.Add(Geometry.ToScreen(gaugeDef.Ref, _vp, _fit!));
        if (buffSize is { } size) rects.Add(Geometry.BuffArea(_vp, _fit!, size, config.BuffCount));
        int debuffAt = rects.Count;
        if (debuffIcons) rects.Add(Geometry.DebuffArea(_vp, _fit!, debuffSize, debuffBoxes));
        else foreach (int box in fastTimers) rects.Add(Geometry.DebuffBoxes(_vp, _fit!, debuffSize)[box].Timer);   // each timer alone, at its box
        if (boss) rects.Add(Geometry.BossRow(_vp, _fit!, config.BossSize));
        int judgAt = rects.Count;
        if (judgRow is { } row) rects.Add(row);
        if (judgBand is { } band) rects.Add(band);
        int raidAt = rects.Count;
        if (raidName is { } nameRect) rects.Add(nameRect);
        int raidCountAt = rects.Count;
        if (raidCount is { } countRect) rects.Add(countRect);
        int raidHpAt = rects.Count;
        if (raidHp is { } raidHpRect) rects.Add(raidHpRect);
        int meterAt = rects.Count;
        if (meterHp is { } hpRect) rects.Add(hpRect);
        if (meterPanel is { } panelRect) rects.Add(panelRect);
        var (amsuText, amsuStrip) = AmsuRects(config, poll, now);
        int amsuAt = rects.Count;
        if (amsuText is { } amsuTextRect) rects.Add(amsuTextRect);
        if (amsuStrip is { } amsuStripRect) rects.Add(amsuStripRect);
        int previewAt = rects.Count;   // 위치 조정's bar score, from the same capture as the reads
        if (preview) rects.Add(FitSolver.Band(_vp));
        int dungeonAt = rects.Count;
        if (dungeonRect is { } dungeonPanel) rects.Add(dungeonPanel);
        int searchAt = rects.Count;
        if (searchRect is { } searchArea) rects.Add(searchArea);
        int bossProbeAt = rects.Count;
        if (bossProbe is { } bossRow) rects.Add(bossRow);
        int meterProbeAt = rects.Count;
        if (meterProbe is { } meterSpot) rects.Add(meterSpot);

        var images = rects.Select(Buffer).ToList();
        long ts = 0;
        long capWall = Stopwatch.GetTimestamp(), capCpu = Perf.ThreadCpu();
        bool captured = _source!.TryCapture(rects, images, TimeSpan.FromMilliseconds(50), out ts);
        Perf.AddCpu("화면 캡처 (새 프레임 대기 포함)", Stopwatch.GetTimestamp() - capWall, Perf.ThreadCpu() - capCpu);
        if (!captured)
        {
            now = Clock.Now();
            if (Advance(now) | _gaugeSpent | KeepWhileHidden(now)) { _gaugeSpent = false; Publish(now); }   // a still screen (paused cutscene) keeps the dungeon too
            Thread.Sleep(50);   // capture unavailable (lock screen, UAC) or a static screen: don't spin
            return;
        }
        var t = Clock.FromTimestamp(ts);
        bool changed = Perf.Run("스킬 쿨다운", () => ReadSlots(slots, images, t, config)) | _gaugeSpent | KeepWhileHidden(t);
        _gaugeSpent = false;
        if (buffSize is not null || debuffs || judging || raiding) _lastPoll = t;
        if (boss) _lastBoss = t;
        if (gaugeDef is not null) _lastGauge = t;
        if (gaugeDef is not null && _hud.Visible == true)   // loading screens and cutscenes would read as empty
        {
            double v = Perf.Run("아덴 게이지", () => _gaugeFilter.Add(gaugeDef!.Read(images[slots.Count + (wantLabel ? 1 : 0)]), t));
            changed |= v != _gauge;
            _gauge = v;
            if (!_previewing) _gains.Seen(v, t);
            if (Elapsed(now, _gainsSavedAt, GainsSaveEvery)) { SaveGains(); _gainsSavedAt = now; }
        }
        else if (_hud.Visible == false) _gaugeFilter.Reset();   // reads from before a loading screen are stale
        if (buffSize is { } read) changed |= Perf.Run("버프", () => ReadBuffs(images[debuffAt - 1], read, config.BuffCount, t));
        if (debuffIcons) changed |= Perf.Run("디버프", () => ReadDebuffs(images[debuffAt], boss ? images[debuffAt + 1] : null, debuffSize, config.BossSize, t, debuffBoxes));
        else if (fastTimers.Count > 0) changed |= Perf.Run("디버프 (시간)", () => ReadDebuffTimers(images, debuffAt, fastTimers, debuffSize, t));
        if (judging)
            changed |= Perf.Run("단심", () => ReadJudgment(config.Judgment!, judgRow is null ? null : images[judgAt], judgBand is null ? null : images[judgAt + (judgRow is null ? 0 : 1)], t));
        if (meterHp is not null) changed |= Perf.Run("DPS 미터기 (보스 HP · 전분)", () => ReadMeter(images[meterAt], meterPanel is null ? null : images[meterAt + 1], t));
        if (amsuText is not null || amsuStrip is not null)
            changed |= Perf.Run("암수", () => ReadAmsu(config, amsuText is null ? null : images[amsuAt], amsuStrip is null ? null : images[amsuAt + (amsuText is null ? 0 : 1)], t));
        if (raidName is not null || raidCount is not null)
            changed |= Perf.Run("레이드 (보스 이름 · 줄 수)", () => ReadRaid(config, raidName is null ? null : images[raidAt], raidCount is null ? null : images[raidCountAt], raidHp is null ? null : images[raidHpAt], t));
        if (bossProbe is not null) Perf.Run("보스 바 확인", () => ReadBossProbe(images[bossProbeAt], t));
        if (meterProbe is not null) ReadMeterProbe(images[meterProbeAt], t);
        if (dungeonRect is not null) StartDungeon(images[dungeonAt], t);
        if (searchRect is not null) Perf.Run("전분 위치 찾기", () => SearchPanel(images[searchAt], t));
        if (wantLabel)
        {
            _lastLabel = t;
            bool seen = Perf.Run("각성기 표시", () => AwakeningLabel.IsShown(images[slots.Count])), shown = AwakeningLabel.Hold(seen, _awakeningChanged, _labelMissed);
            _labelMissed = !seen;
            changed |= shown != _awakeningChanged;
            _awakeningChanged = shown;
        }
        changed |= Advance(t);
        if (changed) Publish(t);
        if (preview)
        {
            _lastPreview = t;
            double bar = FitSolver.Score(images[previewAt], rects[previewAt], _vp, _fit!);
            PreviewReads?.Invoke(_gen, AdjustReads.From(bar, _buffsOn ? _buffs : null, _debuffsOn ? _debuffs : null));
        }
    }

    /// <summary>Reads each captured slot and updates its state. Returns true if anything changed.</summary>
    bool ReadSlots(IReadOnlyList<SlotId> slots, IReadOnlyList<Image> images, TimeSpan t, Config config)
    {
        bool changed = false;
        int unreadable = 0, calibrated = 0;
        var uncalibratedReady = new List<int>();
        var icons = new List<int>();
        int iconRun = Volatile.Read(ref _iconRun);   // a run started after this read sends this step's icons as the old run
        if (_iconsStale) { _iconsStale = false; Array.Clear(_iconFresh); }
        Image? subclassCrop = null;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var crop = images[i];
            var cal = _cal[(int)slot];
            var reading = SlotClassifier.Read(slot, crop, t, cal, _digits!, Dark(slot, config));
            _scheduler.MarkRead(slot, t);
            // ponytail: a copy per 일반 read (≤ 80 small crops a second); copy only greyed ones or on request if GC shows.
            if (slot is >= SlotId.일반1 and <= SlotId.일반8)
                (_skillColour[slot - SlotId.일반1], _skillCrops[slot - SlotId.일반1]) = (SubclassMatcher.Colour(crop), crop.Copy());

            if (cal is not null) { calibrated++; if (reading.Kind == ReadKind.Unreadable) unreadable++; }
            else if (reading.Kind == ReadKind.Ready) uncalibratedReady.Add(i);   // slots on cooldown during the fit

            var prev = _states[(int)slot];
            bool known = config.StackSkills?.Contains(slot) == true;
            var next = CooldownTracker.Update(known ? prev with { StackSkill = true } : prev, reading, CoreEffects.ResetsOnUse(config.CoreWatches, slot));
            if (next != prev) { _states[(int)slot] = next; changed = true; }
            if (!known && !_testMode && config.Character is { } who && _stackLearner.Add(reading, next)) StackSkillLearned?.Invoke(who, slot);
            // A cast (ready → cooldown): a skill that spends the gauge arms it for its drop when the screen shows the
            // cast (no key is read), and the gauge learns what the cast adds (Cast). Not ready → dark (Unavailable):
            // a dark icon follows a spend.
            if (prev.Phase == SlotPhase.Ready && next.Phase is SlotPhase.Cooldown or SlotPhase.LongCooldown) Cast(slot, config, t);
            if (_copyIcons && SkillIcon.Due(slot, reading, _iconFresh[(int)slot], config.AdenSlots)) icons.Add(i);
            if (slot == SlotId.초각스 && next.Phase == SlotPhase.Ready && _subclassPending) subclassCrop = crop.Copy();
            if (config.Debug) Debug?.Invoke(new DebugRow(slot, crop.Copy(), reading, next));
        }

        var wasVisible = _hud.Visible;
        bool? shown = _hud.Add(calibrated, unreadable);
        if (_hud.Visible != wasVisible) HudVisibleChanged?.Invoke(_hud.Visible == true);
        if (_hud.Visible == true && wasVisible != true) WakeDungeon();
        // Only while the bar is certainly on screen in this very frame: Desktop Duplication also captures loading
        // screens, the login screen and windows covering the game. References are also added while too few exist to
        // vote at all (HudVote.MaySave), or the vote could never start.
        if (_foreground && HudVote.MaySave(shown, _cal.Count(c => c is not null), calibrated, unreadable))
            foreach (int i in uncalibratedReady) SaveCalibration(slots[i], images[i], trusted: false, config);
        if (shown == true && _foreground)
        {
            if (_emblemPending) { _emblemPending = false; CaptureEmblem(); }
            bool greyed = SubclassMatcher.Greyed(_skillColour);
            if (subclassCrop is not null) { _subclassPending = false; SubclassSlotCaptured?.Invoke(subclassCrop, greyed ? [.. _skillCrops.OfType<Image>()] : null); }
            if (_skillsRequested)
            {
                _skillsRequested = false;
                SkillsCaptured?.Invoke(greyed ? null : [.. Enumerable.Range(0, 8)
                    .Where(k => _skillCrops[k] is not null && _states[(int)(SlotId.일반1 + k)].Phase == SlotPhase.Ready).Select(k => _skillCrops[k]!)]);
            }
            // A greyed bar (a town) is a poor picture of every skill; one icon in one hue is not (전투 태세 Q and W, sBug.mp4:
            // spread 7.5 and 15).
            foreach (int i in greyed ? [] : icons)
            {
                var icon = images[i].Copy();
                _iconFresh[(int)slots[i]] = true;
                IconSeen?.Invoke(slots[i], icon, iconRun);
            }
        }
        return changed;
    }

    bool Advance(TimeSpan now)
    {
        bool changed = false;
        for (int i = 0; i < _states.Length; i++)
        {
            var next = CooldownTracker.Advance(_states[i], now);
            if (next != _states[i]) { _states[i] = next; changed = true; }
        }
        return changed;
    }

    /// <summary>Finds the buffs in the captured buff row (<see cref="Geometry.BuffArea"/>) and updates each indicator.
    /// Kept apart from the slot reads: nothing here feeds the skill or gauge paths. Returns true if anything changed.</summary>
    bool ReadBuffs(Image area, int size, int count, TimeSpan t)
    {
        var reader = new BuffReader(Geometry.BuffScale(_vp, size));
        var origin = Geometry.BuffArea(_vp, _fit!, size, count);
        PixelRect Local(PixelRect r) => r with { X = r.X - origin.X, Y = r.Y - origin.Y };
        var readings = new List<BuffReading>();
        var boxes = Geometry.BuffBoxes(_vp, _fit!, size, count);
        for (int i = 0; i < boxes.Length; i++)
        {
            var kind = BuffReader.Classify(area, Local(boxes[i].Icon));
            double? seconds = kind is null ? null : reader.ReadTimer(area, Local(boxes[i].Timer));
            if (kind is { } k) readings.Add(new(k, seconds, t));
            BoxDebug($"버프 {i + 1}", area, Local(boxes[i].Icon), Local(boxes[i].Timer), kind?.ToString(), seconds);
        }

        var next = _buffs.Select(b => BuffTracker.Update(b, readings, t, _hud.Visible == true)).ToArray();
        bool changed = !next.SequenceEqual(_buffs);
        _buffs = next;
        return changed;
    }

    /// <summary>The debug window's row for one buff or debuff box: its icon and timer, and what was read.</summary>
    void BoxDebug(string key, Image area, PixelRect icon, PixelRect timer, string? kind, double? seconds)
    {
        if (!_config.Debug) return;
        int x = Math.Min(icon.X, timer.X), right = Math.Max(icon.Right, timer.Right);
        var crop = area.Crop(new PixelRect(x, icon.Y, right - x, timer.Bottom - icon.Y));
        ExtraDebug?.Invoke(new(key, crop, kind is null ? "없음" : $"{kind}{(seconds is { } s ? $" {s:0.#}초" : "")}", ""));
    }

    /// <summary>The frame pass (<see cref="DebuffSchedule"/>): your debuffs in the captured row (<see cref="Geometry.DebuffArea"/> of the
    /// first <paramref name="count"/> boxes) and, when captured, the boss's counts in <paramref name="boss"/> (<see cref="Geometry.BossRow"/>).
    /// Debuffs fill the row from the left, so the boxes are checked up to the first without a debuff frame; the icons are identified
    /// only when the schedule asks (every 0.5 s, the row shifted, a timer unread), else kept; the timed boxes' timers are read.
    /// Returns true if anything changed.</summary>
    bool ReadDebuffs(Image area, Image? boss, int size, int bossSize, TimeSpan t, int count)
    {
        var reader = new BuffReader(Geometry.BuffScale(_vp, size));
        var origin = Geometry.DebuffArea(_vp, _fit!, size, count);
        PixelRect Local(PixelRect r) => r with { X = r.X - origin.X, Y = r.Y - origin.Y };
        var boxes = Geometry.DebuffBoxes(_vp, _fit!, size);
        int filled = RowCadence.Filled(count, i => DebuffReader.Filled(area, Local(boxes[i].Icon)));   // one hidden frame passed
        if (filled < count && _config.Debug) BoxDebug($"디버프 {filled + 1}", area, Local(boxes[filled].Icon), Local(boxes[filled].Timer), "없음", null);
        bool classify = DebuffSchedule.Classify(_slots, filled, t);
        var kinds = new DebuffKind?[filled];
        var seconds = new double?[filled];
        var reads = new List<DebuffReading>();
        double? duelSeconds = null;
        for (int i = 0; i < filled; i++)
        {
            double duelScore = 0;
            kinds[i] = classify ? DebuffReader.Classify(area, Local(boxes[i].Icon), out duelScore) : _slots.Kinds[i];
            seconds[i] = DebuffSchedule.Timed(kinds[i]) ? reader.ReadTimer(area, Local(boxes[i].Timer)) : null;
            // 결투자's countdown and hold are the tracker's own clock (DuelMark); its "38초" only sets when it came (DuelTimer).
            double? sec = seconds[i];
            if (kinds[i] == DebuffKind.결투자) (duelSeconds, sec) = (sec, null);
            if (kinds[i] is { } k) reads.Add(new(k, sec));
            if (_config.Debug) BoxDebug($"디버프 {i + 1}", area, Local(boxes[i].Icon), Local(boxes[i].Timer), DebuffReader.DebugText(kinds[i], duelScore), seconds[i]);
        }
        _slots = DebuffSchedule.Frames(_slots, filled, classify ? kinds : null, seconds, t);
        var (red, blue) = boss is null ? (null, null) : DebuffReader.BossCounts(boss, Geometry.BossScale(_vp, _fit!, bossSize));
        var next = DebuffTracker.DuelTimer(DebuffTracker.Update(_debuffs, reads, red, blue, t, _hud.Visible == true), duelSeconds, t);
        if (boss is not null && _config.Debug)
            ExtraDebug?.Invoke(new("보스", boss.Copy(), $"빨강 {red?.ToString() ?? "?"} / 파랑 {blue?.ToString() ?? "?"}",
                $"표시: 빨강 {next.BossRed?.ToString() ?? "?"} / 파랑 {next.BossBlue?.ToString() ?? "?"}"));
        bool changed = next != _debuffs;
        _debuffs = next;
        return changed;
    }

    /// <summary>The timer pass: the timers under 2 s (<paramref name="timerBoxes"/>, each captured alone from <paramref name="at"/> on).
    /// The tracker gets every known debuff but only this pass's seconds: another box's older read would reset its end.</summary>
    bool ReadDebuffTimers(IReadOnlyList<Image> images, int at, IReadOnlyList<int> timerBoxes, int size, TimeSpan t)
    {
        var reader = new BuffReader(Geometry.BuffScale(_vp, size));
        var read = new Dictionary<int, double?>();
        for (int j = 0; j < timerBoxes.Count; j++)
            read[timerBoxes[j]] = reader.ReadTimer(images[at + j], new PixelRect(0, 0, images[at + j].Width, images[at + j].Height));
        _slots = DebuffSchedule.Timers(_slots, read, t);
        var reads = new List<DebuffReading>();
        double? duelSeconds = null;
        for (int i = 0; i < _slots.Filled; i++)
        {
            double? sec = read.TryGetValue(i, out var v) ? v : null;
            if (_slots.Kinds[i] == DebuffKind.결투자) (duelSeconds, sec) = (sec, null);
            if (_slots.Kinds[i] is { } k) reads.Add(new(k, sec));
        }
        var next = DebuffTracker.DuelTimer(DebuffTracker.Update(_debuffs, reads, null, null, t, _hud.Visible == true), duelSeconds, t);
        bool changed = next != _debuffs;
        _debuffs = next;
        return changed;
    }

    void Publish(TimeSpan t) =>
        SnapshotReady?.Invoke(new Snapshot(t, [.. _states], _awakeningChanged, _gaugeOn ? _gauge : null, [.. Enum.GetValues<SlotGroup>()], [.. _config.AdenSlots])
        {
            Buffs = _buffsOn ? [.. _buffs] : [],
            Debuffs = _debuffsOn ? _debuffs : null,
            Judgment = _judgmentOn ? _judgment : null,
            Raid = _raid,
            Meter = _meterOn ? _meter : null,
            Presence = _presence,
            Dungeon = _meterOn ? _dungeon : null,
            Amsu = _amsuOn ? _amsu : null,
            AmsuTimed = _amsuTimed,
        });

    /// <summary>The client area as it is on its monitor. Desktop Duplication gives a frame only when the screen changes,
    /// so it waits up to <see cref="ShotWait"/>.</summary>
    void TakeScreenshot(object token)
    {
        if (GameWindow.ClientRect(_hwnd)?.Intersect(GameWindow.MonitorBounds(_monitor)) is not { } area)
        {
            ScreenshotCaptured?.Invoke(token, null, default);
            return;
        }
        var img = new Image(area.Width, area.Height);
        var until = Stopwatch.GetTimestamp() + (long)(ShotWait.TotalSeconds * Stopwatch.Frequency);
        bool got;
        while (!(got = _source!.TryCapture([area], [img], TimeSpan.FromMilliseconds(100), out _)) && Stopwatch.GetTimestamp() < until) { }
        if (got) for (int i = 3; i < img.Bgra.Length; i += 4) img.Bgra[i] = 255;   // the desktop's alpha is undefined
        ScreenshotCaptured?.Invoke(token, got ? img : null, area);
    }

    void CaptureEmblem()
    {
        var rect = Geometry.ToScreen(Geometry.RefEmblem, _vp, _fit!);
        var img = new Image(rect.Width, rect.Height);
        if (_source!.TryCapture([rect], [img], TimeSpan.FromMilliseconds(100), out _)) EmblemCaptured?.Invoke(img);
    }

    // ── game window, viewport, fit ────────────────────────────────────────────────

    // A cast seen (ready → cooldown): a spend arms the gauge for its drop, and every cast is learned from and, in
    // Predicted, tells the filter the rise to expect. Nothing reads the keyboard, so a cast is known only once the
    // screen shows the cooldown: the gauge is read every frame from here (GaugePace).
    void Cast(SlotId slot, Config config, TimeSpan at)
    {
        _castAt = at;
        Arm(slot, config, at);
        CoreEffects.ApplyAfterCast(config.CoreWatches, slot, at, _scheduler.Watch);
        if (_gauge is not { } gauge || _hud.Visible != true) return;
        if (!_previewing) _gains.Cast(slot, at, gauge);   // a gauge read at a fit still being moved teaches nothing
        if (config.ReadMode == GaugeReadMode.Predicted && _gains.Expected(slot) is { } rise and > 0) _gaugeFilter.Expect(rise, at + GaugeGains.Window);
    }

    void Arm(SlotId slot, Config config, TimeSpan at)
    {
        if (_hud.Visible != true || config.GaugeRule is not { } rule || _gauge is not { } gauge) return;   // hidden: _gauge is stale
        if (!rule.SpendsAtOnce(slot))
        {
            if (rule.AfterSpend(slot, gauge) is { } left) _gaugeFilter.Arm(left, rule.SpendMargin(slot), at + GaugeFilter.ArmFor);
            return;
        }
        // Always spent: the cast drops the gauge at once and the read follows. Only a cast with the game in front is a use.
        if (_foreground && _spender.Press(rule, slot, gauge, at) is { } remains) (_gauge, _gaugeSpent) = (_gaugeFilter.Spend(remains, at), true);
    }

    // Keeps what the last character learned, then starts from what key learned before (gains.json).
    void SwitchGains(string? key)
    {
        SaveGains();
        _savedGains ??= _gainStore.Load();
        _gains = new(key is not null ? _savedGains.GetValueOrDefault(key) : null);
        (_gainsFor, _gainsSaved) = (key, _gains.Version);
    }

    void SaveGains()
    {
        // 테스트 모드: casts in a video teach nothing kept (the character may be the user's own).
        if (_testMode || _gainsFor is null || _savedGains is null || _gains.Version == _gainsSaved) return;
        _savedGains[_gainsFor] = _gains.Export();
        _gainsSaved = _gains.Version;
        WriteGains();
    }

    void ForgetGains()
    {
        (_gains, _gainsSaved) = (new(), 0);
        if (!_testMode && _gainsFor is not null && _savedGains?.Remove(_gainsFor) == true) WriteGains();
    }

    void WriteGains()
    {
        try { _gainStore.Save(_savedGains!); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[gains] save failed: {ex.Message}"); }
    }

    /// <summary>True when <paramref name="every"/> has passed since <paramref name="last"/>, or <paramref name="last"/> is the "never" sentinel (subtracting MinValue overflows).</summary>
    static bool Elapsed(TimeSpan now, TimeSpan last, TimeSpan every) => last == TimeSpan.MinValue || now - last >= every;

    // ── per-fit slot calibration (ready icon), stored next to the settings ─────────

    string CalibrationDir => Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "calibration", Geometry.ViewportKey(_vp));

    /// <summary>Stores the slot's ready icon, which <see cref="SlotCalibration"/> uses for Unreadable and for flashes.
    /// Outside the fit (<paramref name="trusted"/> false) a
    /// single-tinted icon is refused: in town or silenced every icon is one dark tint, which is a poor picture of the
    /// slot to match later frames against.</summary>
    void SaveCalibration(SlotId slot, Image crop, bool trusted, Config config)
    {
        var copy = crop.Copy();
        var cal = new SlotCalibration(copy);
        if (!trusted && cal.HueSpread < MinHueSpread) return;
        _cal[(int)slot] = cal;
        if (_testMode || _previewing) return;   // a video's slots, or a fit still being moved: memory only
        try
        {
            Directory.CreateDirectory(CalibrationDir);
            var path = CalibrationFile(slot, config);
            File.WriteAllBytes(path + ".tmp", copy.ToBytes());
            File.Move(path + ".tmp", path, overwrite: true);   // a crash mid-write never leaves a torn file
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[engine] calibration of {slot} not saved (kept in memory): {e.Message}");
        }
    }

    SlotCalibration? LoadCalibration(SlotId slot, Config config)
    {
        var path = CalibrationFile(slot, config);
        // 테스트 모드: the game's calibration isn't the video's; previewing: the fit on show isn't the one it was saved at.
        if (_testMode || _previewing || !File.Exists(path)) return null;
        try
        {
            var img = Image.FromBytes(File.ReadAllBytes(path));
            var expected = Rect(slot, _fit!, config);
            // Damaged, or saved at a different fit.
            return img is not null && img.Width == expected.Width && img.Height == expected.Height ? new SlotCalibration(img) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    PixelRect Rect(SlotId slot, Fit fit, Config config) => Geometry.Slot(slot, _vp, fit, config.AdenRects);

    static DarkCue Dark(SlotId slot, Config config) => config.AdenSlots.Contains(slot) ? config.AdenDark : DarkCue.None;

    /// <summary>A 아덴 slot's ready icon is the class's own (its frame differs per class), so switching classes neither reads
    /// nor overwrites another's; a 수동 조정's calibration has files of its own (…_adj). See <see cref="SlotCalibration.FileName"/>.</summary>
    string CalibrationFile(SlotId slot, Config config) => Path.Combine(CalibrationDir,
        SlotCalibration.FileName(slot, config.AdenRects?.GetValueOrDefault(slot), config.Character, _adjusted));

    /// <summary>The calibration files of the fit in use: 수동 조정's (…_adj) or the others'.</summary>
    IEnumerable<string> OwnCalibrationFiles() => (Directory.Exists(CalibrationDir) ? Directory.GetFiles(CalibrationDir, "*.bgra") : [])
        .Where(f => Path.GetFileNameWithoutExtension(f).EndsWith(SlotCalibration.AdjustSuffix, StringComparison.Ordinal) == _adjusted);

    void ReloadAden(Config config)
    {
        if (_fit is null) return;
        foreach (var slot in new[] { SlotId.아덴1, SlotId.아덴2 })
        {
            _cal[(int)slot] = LoadCalibration(slot, config);
            _states[(int)slot] = SlotState.Unknown(slot);
        }
        _buffers.Clear();
    }

    Image Buffer(PixelRect r)
    {
        if (!_buffers.TryGetValue(r, out var img)) _buffers[r] = img = new Image(r.Width, r.Height);
        return img;
    }

    void ResetSource()
    {
        _source?.Dispose();
        (_source, _seesBehind) = (null, false);
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));
    }
}
