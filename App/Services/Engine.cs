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
    public sealed record Config(IReadOnlyDictionary<string, Fit> Fits, IReadOnlyCollection<SlotId> AdenSlots, bool Debug, ReadPlan Reads,
        GaugeDef? Gauge = null, IReadOnlyDictionary<SlotId, PixelRect>? AdenRects = null, DarkCue AdenDark = DarkCue.None, GaugeRule? GaugeRule = null,
        GaugeReadMode ReadMode = GaugeReadMode.Agreed, string? Character = null, Raid Raid = Raid.없음, int DebuffSize = 100, double? ManualHud = null,
        ManualScreen? ManualScreen = null, IReadOnlyDictionary<string, Fit>? Adjusts = null)
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
    /// without 직접 입력 the 해상도 and 21:9 it was found at (직접 입력 starts from them).</summary>
    public event Action<Viewport, Fit?, string, ManualScreen?, bool>? FitFinished;   // last: found in 테스트 모드 (never saved)
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
    double? _gauge;
    GaugeDef? _gaugeFrom;      // the definition _gauge was read with (one instance per 직업각인: CharacterService caches it)
    readonly GaugeFilter _gaugeFilter = new();   // drops the shine the game sweeps over the gauge
    readonly ImmediateSpend _spender = new();   // which presses drop the gauge at once (도화가, 바드)
    bool _gaugeSpent;   // the gauge was set by a press: publish it without waiting for a read
    GaugeGains _gains = new();   // what each cast adds to the gauge (GaugeReadMode.Predicted, beta), for _gainsFor
    readonly GaugeGainsStore _gainStore = new(GaugeGainsStore.DefaultPath);
    Dictionary<string, IReadOnlyDictionary<string, Gain>>? _savedGains;   // gains.json, read at the first character
    string? _gainsFor;
    int _gainsSaved;   // _gains.Version last written
    TimeSpan _gainsSavedAt = TimeSpan.MinValue;
    static readonly TimeSpan GainsSaveEvery = TimeSpan.FromMinutes(1);
    IReadOnlyDictionary<SlotId, PixelRect>? _rectsFrom;   // the 아덴 rects the calibrations were loaded for
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
    readonly TestFits _testFits = new();
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

    /// <summary>Runs 스킬창 맞추기 on the next frame. The user should be in the 수련장 with every cooldown reset.</summary>
    public void RequestFit() => _fitRequested = true;

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
                FitFinished?.Invoke(_vp, null, "게임이 실행 중이 아닙니다. 게임을 켜고 다시 시도해 주세요.", null, _testMode);
            }
            if (Interlocked.Exchange(ref _shotFor, null) is { } missed) ScreenshotCaptured?.Invoke(missed, null, default);
            Thread.Sleep(1000);
            return;
        }
        if (Interlocked.Exchange(ref _shotFor, null) is { } token) { TakeScreenshot(token); return; }
        var now = Clock.Now();

        if (Elapsed(now, _lastViewport, ViewportEvery)) UpdateViewport(now);
        if (_fitRequested)
        {
            _fitRequested = false;
            var kept = _vp;
            _previewing = false;   // the debug window's preview starts over on the new fit
            UpdateViewport(now, fitting: true);
            if (!RunFit() && _vp != kept) (_vp, _fit) = (kept, null);   // a failed fit keeps the game area its saved fit is for
            return;
        }
        if (!EnsureFit()) { Thread.Sleep(200); return; }
        PreviewStep();
        if (_fit is null) return;   // a preview just ended: the saved fit is resolved next step
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
        _gaugeFilter.Mode = config.ReadMode;
        if (config.AdenRects != _rectsFrom) { _rectsFrom = config.AdenRects; ReloadAden(config); }   // another class: its own 아덴 spots
        (_buffsOn, _gaugeOn, _debuffsOn) = (config.Reads.BuffSize is not null, gaugeDef is not null, config.Raid != Raid.없음);
        // 위치 조정 tries a 버프 크기 live: both rows read at it until 적용 stores it in 설정 or the panel closes.
        int? previewSize = _previewing ? _previewBuffSize : null;
        int? buffSize = poll && config.Reads.BuffSize is { } set ? previewSize ?? set : null;
        int debuffSize = previewSize ?? config.DebuffSize;
        bool debuffs = poll && _debuffsOn, boss = debuffs && Elapsed(now, _lastBoss, BossEvery);
        if (!Elapsed(now, _lastGauge, _gaugeFilter.Armed(now) ? GaugePace.Fast : GaugePace.Every(now, _castAt))) gaugeDef = null;
        bool preview = _previewing && Elapsed(now, _lastPreview, PreviewEvery);
        if (slots.Count == 0 && !wantLabel && buffSize is null && gaugeDef is null && !debuffs && !preview)
        {
            if (Advance(now) | _gaugeSpent) { _gaugeSpent = false; Publish(now); }
            Thread.Sleep(4);
            return;
        }

        var rects = new List<PixelRect>(slots.Count + 3);
        foreach (var s in slots) rects.Add(Rect(s, _fit!, config));
        if (wantLabel) rects.Add(Geometry.ToScreen(Geometry.RefAwakeningLabel, _vp, _fit!));
        if (gaugeDef is not null) rects.Add(Geometry.ToScreen(gaugeDef.Ref, _vp, _fit!));
        if (buffSize is { } size) rects.Add(Geometry.BuffArea(_vp, _fit!, size));
        int debuffAt = rects.Count;
        if (debuffs) rects.Add(Geometry.DebuffArea(_vp, _fit!, debuffSize));
        if (boss) rects.Add(Geometry.BossRow(_vp, _fit!));
        int previewAt = rects.Count;   // the debug window's pictures, in the same capture as the reads
        if (preview) { rects.Add(FitSolver.Band(_vp)); rects.Add(Geometry.BossBand(_vp)); }

        var images = rects.Select(Buffer).ToList();
        if (!_source!.TryCapture(rects, images, TimeSpan.FromMilliseconds(50), out long ts))
        {
            now = Clock.Now();
            if (Advance(now) | _gaugeSpent) { _gaugeSpent = false; Publish(now); }
            Thread.Sleep(50);   // capture unavailable (lock screen, UAC) or a static screen: don't spin
            return;
        }
        var t = Clock.FromTimestamp(ts);
        bool changed = ReadSlots(slots, images, t, config) | _gaugeSpent;
        _gaugeSpent = false;
        if (buffSize is not null || debuffs) _lastPoll = t;
        if (boss) _lastBoss = t;
        if (gaugeDef is not null) _lastGauge = t;
        if (gaugeDef is not null && _hud.Visible == true)   // loading screens and cutscenes would read as empty
        {
            double v = _gaugeFilter.Add(gaugeDef.Read(images[slots.Count + (wantLabel ? 1 : 0)]), t);
            changed |= v != _gauge;
            _gauge = v;
            if (!_previewing) _gains.Seen(v, t);
            if (Elapsed(now, _gainsSavedAt, GainsSaveEvery)) { SaveGains(); _gainsSavedAt = now; }
        }
        else if (_hud.Visible == false) _gaugeFilter.Reset();   // reads from before a loading screen are stale
        if (buffSize is { } read) changed |= ReadBuffs(images[debuffAt - 1], read, t);
        if (debuffs) changed |= ReadDebuffs(images[debuffAt], boss ? images[debuffAt + 1] : null, debuffSize, t);
        if (wantLabel)
        {
            _lastLabel = t;
            bool seen = AwakeningLabel.IsShown(images[slots.Count]), shown = AwakeningLabel.Hold(seen, _awakeningChanged, _labelMissed);
            _labelMissed = !seen;
            changed |= shown != _awakeningChanged;
            _awakeningChanged = shown;
        }
        changed |= Advance(t);
        if (changed) Publish(t);
        if (preview)
        {
            _lastPreview = t;
            // ponytail: two copies per frame (~21 MB at 4K, 5 fps, only while the debug window is open); pool them if GC shows.
            PreviewFrame?.Invoke(_gen, images[previewAt].Copy(), rects[previewAt], images[previewAt + 1].Copy(), rects[previewAt + 1]);
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
            var next = CooldownTracker.Update(prev, reading);
            if (next != prev) { _states[(int)slot] = next; changed = true; }
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
        // Only while the bar is certainly on screen in this very frame: Desktop Duplication also captures loading
        // screens, the login screen and windows covering the game.
        if (shown == true && _foreground)
        {
            foreach (int i in uncalibratedReady) SaveCalibration(slots[i], images[i], trusted: false, config);
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
    bool ReadBuffs(Image area, int size, TimeSpan t)
    {
        var reader = new BuffReader(Geometry.BuffScale(_vp, size));
        var origin = Geometry.BuffArea(_vp, _fit!, size);
        PixelRect Local(PixelRect r) => r with { X = r.X - origin.X, Y = r.Y - origin.Y };
        var readings = new List<BuffReading>();
        var boxes = Geometry.BuffBoxes(_vp, _fit!, size);
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

    /// <summary>Finds your debuffs in the captured debuff row (<see cref="Geometry.DebuffArea"/>) and, when captured, the
    /// boss's counts in <paramref name="boss"/> (<see cref="Geometry.BossRow"/>). Returns true if anything changed.</summary>
    bool ReadDebuffs(Image area, Image? boss, int size, TimeSpan t)
    {
        var reader = new BuffReader(Geometry.BuffScale(_vp, size));
        var origin = Geometry.DebuffArea(_vp, _fit!, size);
        PixelRect Local(PixelRect r) => r with { X = r.X - origin.X, Y = r.Y - origin.Y };
        var reads = new List<DebuffReading>();
        var boxes = Geometry.DebuffBoxes(_vp, _fit!, size);
        for (int i = 0; i < boxes.Length; i++)
        {
            var kind = DebuffReader.Classify(area, Local(boxes[i].Icon));
            double? seconds = kind is DebuffKind.번개 or DebuffKind.교체 ? reader.ReadTimer(area, Local(boxes[i].Timer)) : null;
            if (kind is { } k) reads.Add(new(k, seconds));
            BoxDebug($"디버프 {i + 1}", area, Local(boxes[i].Icon), Local(boxes[i].Timer), kind?.ToString(), seconds);
        }
        var (red, blue) = boss is null ? (null, null) : DebuffReader.BossCounts(boss, Geometry.BossScale(_vp, _fit!));
        var next = DebuffTracker.Update(_debuffs, reads, red, blue, t, _hud.Visible == true);
        if (boss is not null && _config.Debug)
            ExtraDebug?.Invoke(new("보스", boss.Copy(), $"빨강 {red?.ToString() ?? "?"} / 파랑 {blue?.ToString() ?? "?"}",
                $"표시: 빨강 {next.BossRed?.ToString() ?? "?"} / 파랑 {next.BossBlue?.ToString() ?? "?"}"));
        bool changed = next != _debuffs;
        _debuffs = next;
        return changed;
    }

    void Publish(TimeSpan t) =>
        SnapshotReady?.Invoke(new Snapshot(t, [.. _states], _awakeningChanged, _gaugeOn ? _gauge : null, [.. Enum.GetValues<SlotGroup>()], [.. _config.AdenSlots])
        {
            Buffs = _buffsOn ? [.. _buffs] : [],
            Debuffs = _debuffsOn ? _debuffs : null,
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

    bool EnsureGame()
    {
        // In test mode the window can change at any click; a switch of mode drops the cached one either way.
        bool test = GameWindow.TestMode;
        var hwnd = !test && test == _testMode && _hwnd != 0 && GameWindow.ClientRect(_hwnd) is not null ? _hwnd : GameWindow.Find(test);
        // The window picked for 테스트 모드 is gone (also when it closed while minimised, already not running): App ends the mode.
        if (test && GameWindow.Picked is var picked && picked != 0 && !GameWindow.Exists(picked)) PickedWindowClosed?.Invoke(picked);
        if (test != _testMode)
        {
            // In or out of 테스트 모드: the video's fit and calibration (memory only) and the game's saved ones don't mix.
            _testFits.Clear();
            (_vp, _fit, _fitNeededRaised, _lastViewport) = (default, null, false, TimeSpan.MinValue);
            Array.Clear(_cal);
            for (int i = 0; i < _states.Length; i++) _states[i] = SlotState.Unknown(SlotInfo.All[i]);
            _buffs = [.. Enum.GetValues<BuffKind>().Select(k => BuffState.Absent(k, TimeSpan.Zero))];
            (_debuffs, _gauge, _gaugeSpent) = (DebuffStatus.None, null, false);
            _gaugeFilter.Reset();
            // What casts add to the gauge: the game's is saved before a video plays, and what the video taught is dropped after.
            if (!_testMode) SaveGains();
            _gains = new(_gainsFor is not null ? _savedGains?.GetValueOrDefault(_gainsFor) : null);
            _gainsSaved = _gains.Version;
        }
        _testMode = test;
        // 테스트 모드: a minimised window pauses everything (its capture would be stale), as if it weren't there.
        bool running = hwnd != 0 && (!test || GameWindow.ClientRect(hwnd) is not null);
        if (running != _running) { _running = running; GameRunningChanged?.Invoke(running); }
        if (!running) { ResetSource(); return false; }
        // Desktop Duplication covers one monitor: a window moved to another one needs a new source.
        var monitor = GameWindow.MonitorOf(hwnd);
        if (hwnd != _hwnd || monitor != _monitor || _source is null)
        {
            ResetSource();
            (_hwnd, _monitor) = (hwnd, monitor);
            _source = CaptureFactory.Create(hwnd);
            _lastViewport = TimeSpan.MinValue;
        }
        return true;
    }

    /// <param name="fitting">스킬창 맞추기 is about to run: the game area as it is now wins over a kept one.</param>
    void UpdateViewport(TimeSpan now, bool fitting = false)
    {
        if (GameWindow.ClientRect(_hwnd) is not { } client) return;
        var manual = _testMode ? null : _config.Manual(client);   // 직접 입력 describes the game, not a video of it
        Viewport vp;
        if (manual is { } m) vp = Geometry.ManualViewport(client, m.Screen);   // 직접 입력: no black-bar probe
        else
        {
            var probe = Letterbox.Probe(client);
            var column = Buffer(probe);
            if (!_source!.TryCapture([probe], [column], TimeSpan.FromMilliseconds(100), out _)) return;
            vp = Letterbox.Viewport(client, column, _testMode ? Letterbox.VideoNoise : 0);
        }
        _lastViewport = now;
        bool resized = (client.Width, client.Height) != (_client.Width, _client.Height);
        _client = client;
        if (vp == _vp) return;
        // Cutscenes add black bars too: on the same window size, keep a fitted viewport unless the new one has its own fit.
        // So does a 직접 입력 area: it replaces a fitted one once a fit is found there (스킬창 맞추기 adopts it to look).
        // In 테스트 모드 the fits are the video's (a fade to black or a dark scene in it is the same case).
        var fits = _config.Fits;
        bool Fitted(Viewport v) => _testMode ? _testFits.For(v) is not null
            : fits.ContainsKey(Geometry.ViewportKey(v)) || _config.Adjusts?.ContainsKey(Geometry.ViewportKey(v)) == true;
        if (!fitting && !resized && Fitted(_vp) && !Fitted(vp)) return;
        _vp = vp;
        Volatile.Write(ref _areaKey, Geometry.ViewportKey(vp));
        _fit = null;          // re-resolved for the new size
        _fitNeededRaised = false;
    }

    bool EnsureFit()
    {
        if (_vp.Height == 0) return false;
        // 설정 switched to or from 수동 조정 (or one was dropped): the fit in use isn't the one the mode now gives.
        if (_fit is not null && !_testMode && !_previewing && (_config.Adjusts?.ContainsKey(Geometry.ViewportKey(_vp)) == true) != _adjusted) _fit = null;
        if (_fit is not null) return true;
        if (_testMode)
        {
            // The video in front: its own fit, looked for on it while it is in front (TestFits), never the game's saved one.
            if (_testFits.For(_vp) is { } found) { ApplyFit(found); return true; }
            if (_foreground && _testFits.ShouldTry(_vp, Clock.Now()))
            {
                _quietFit = true;
                try { RunFit(); }
                finally { _quietFit = false; }
            }
            // 위치 조정 opened with nothing found: start from where HUD 100 puts the bar, for the user to move it (a drop
            // clears it again, 적용 keeps it as the video's fit).
            if (_fit is null && Volatile.Read(ref _ask) is not null)
            {
                ApplyFit(new Fit(BareHud));
                if (!_bareNoticed) { _bareNoticed = true; PreviewWithoutFit?.Invoke(); }   // once per session, not per game-area change
            }
            return _fit is not null;
        }
        // 수동 조정 applied for this game area wins; its slot calibration is kept apart (CalibrationFile).
        Fit? adjust = null;
        _adjusted = _config.Adjusts?.TryGetValue(Geometry.ViewportKey(_vp), out adjust) == true;
        if (adjust is not null) { ApplyFit(adjust); return true; }
        if (!_config.Fits.TryGetValue(Geometry.ViewportKey(_vp), out var fit))
        {
            if (!_fitNeededRaised) { _fitNeededRaised = true; FitNeeded?.Invoke(_vp); }
            return false;
        }
        ApplyFit(fit);
        return true;
    }

    void ApplyFit(Fit fit)
    {
        Array.Clear(_cal);
        _fit = fit;
        _digits = new DigitReader(Geometry.Scale(_vp, fit));
        _buffers.Clear();
        for (int i = 0; i < _cal.Length; i++) _cal[i] = LoadCalibration(SlotInfo.All[i], _config);    }

    /// <summary>스킬창 맞추기; true when a fit was found.</summary>
    bool RunFit()
    {
        try { return FitNow(); }
        catch (Exception ex)
        {
            Trace.WriteLine($"[engine] fit failed: {ex}");
            return Failed($"스킬창을 맞추지 못했습니다: {ex.Message}");
        }
    }

    /// <summary>A fit not found, told to App unless it was looked for unasked (테스트 모드, <see cref="_quietFit"/>).</summary>
    bool Failed(string message)
    {
        if (!_quietFit) FitFinished?.Invoke(_vp, null, message, null, _testMode);
        return false;
    }

    bool FitNow()
    {
        if (_vp.Height == 0) return Failed("게임 화면을 찾지 못했습니다. 게임을 창 모드(전체 창)로 실행해 주세요.");
        // The request came from the tray menu or 설정, which leave the taskbar over the skill bar: App brings the game to
        // the front, and the capture waits for it and for the taskbar to slide away.
        for (int i = 0; i < FitWaitSteps && !_foreground; i++) Thread.Sleep(100);
        if (!_foreground) return Failed("게임 창을 앞으로 가져오지 못했습니다. 게임 화면을 한 번 누른 뒤 다시 시도해 주세요.");
        if (!_quietFit) Thread.Sleep(TaskbarSettleMs);   // a quiet try runs with the video already in front, no taskbar to wait out
        var band = FitSolver.Band(_vp);
        var img = new Image(band.Width, band.Height);
        if (!_source!.TryCapture([band], [img], TimeSpan.FromMilliseconds(500), out _))
            return Failed("화면을 캡처하지 못했습니다. 게임 창이 보이는 상태에서 다시 시도해 주세요.");
        // 테스트 모드: always read off the video (직접 입력 describes the game), and its calibration stays in memory.
        var (fit, note, detected) = !_testMode && _config.Manual(_client) is { } m ? SolveManual(img, band, m.Hud, m.Screen) : SolveAuto(img, band);
        if (fit is null) return Failed(note);

        if (_testMode) _testFits.Set(_vp, fit);
        _adjusted = false;   // found on screen (App drops a 수동 조정 of this area before asking)
        int ready = Recalibrate(fit);
        FitFinished?.Invoke(_vp, fit, $"스킬창을 맞췄습니다 (HUD {fit.Hud:0}%, 준비된 칸 {ready}/{SlotInfo.All.Length}개 기준 저장).{note}", detected, _testMode);
        return true;
    }

    /// <summary>Puts <paramref name="fit"/> to use and calibrates every slot that reads ready now (cooldowns reset), replacing the
    /// saved calibration of the fit in use (수동 조정's or the others', <see cref="OwnCalibrationFiles"/>; none in 테스트 모드).
    /// Returns how many slots were ready.</summary>
    int Recalibrate(Fit fit)
    {
        if (!_testMode) foreach (var file in OwnCalibrationFiles().ToList()) File.Delete(file);
        ApplyFit(fit);
        var slots = SlotInfo.All;
        var rects = slots.Select(s => Rect(s, fit, _config)).ToList();
        var crops = rects.Select(r => new Image(r.Width, r.Height)).ToList();
        int ready = 0;
        if (_source!.TryCapture(rects, crops, TimeSpan.FromMilliseconds(500), out long ts))
            for (int i = 0; i < slots.Length; i++)
                if (SlotClassifier.Read(slots[i], crops[i], Clock.FromTimestamp(ts), null, _digits!, Dark(slots[i], _config)).Kind == ReadKind.Ready)
                {
                    SaveCalibration(slots[i], crops[i], trusted: true, _config);
                    ready++;
                }
        return ready;
    }

    (Fit? Fit, string Note, ManualScreen? Detected) SolveAuto(Image band, PixelRect rect)
    {
        if (FitSolver.Solve(band, rect, _vp, _testMode) is not { } fit)
            return (null, "HUD 크기를 찾지 못했습니다. HP 바가 보이는지 확인하거나, 설정 → 스킬창에서 직접 입력을 켜 보세요. 저장된 맞춤은 그대로 둡니다.", null);
        return (fit, "", Geometry.Detected(_vp, _client));
    }

    /// <summary>직접 입력: the bar is checked where the given values put it, then searched wider; not found, nothing is guessed.</summary>
    (Fit? Fit, string Note, ManualScreen? Detected) SolveManual(Image band, PixelRect rect, double hud, ManualScreen screen)
    {
        string given = $"HUD {hud:0}%, {screen.Width}×{screen.Height}{(screen.Wide ? ", 21:9" : "")}";
        var r = FitSolver.SolveManual(band, rect, _vp, hud);
        if (r.Fit is null)
            return (null, $"직접 입력한 값({given})으로 스킬창을 찾지 못했습니다. 값을 확인하거나 자동으로 맞추기를 눌러 주세요. 저장된 맞춤은 그대로 둡니다.", null);
        string note = r.Moved > 0 ? $" 입력한 값의 자리에서 {r.Moved}px 옮겨 맞췄습니다." : "";
        if ((_client.Width, _client.Height) != (screen.Width, screen.Height))
            note += $" 게임 창({_client.Width}×{_client.Height})이 입력한 해상도와 달라 창 크기에 맞춰 계산했습니다.";
        return (r.Fit, note, null);
    }

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

    /// <summary>A 아덴 slot a class moves (발키리) keeps its ready icon in a file of its own, so switching classes doesn't
    /// overwrite the other's.</summary>
    /// A 수동 조정's calibration has files of its own (…_adj), so 자동 and 직접 입력 keep theirs.
    string CalibrationFile(SlotId slot, Config config) => Path.Combine(CalibrationDir,
        (config.AdenRects?.GetValueOrDefault(slot) is { Width: > 0 } r ? $"{slot}_{r.X}_{r.Y}" : slot.ToString()) + (_adjusted ? AdjustSuffix : "") + ".bgra");

    const string AdjustSuffix = "_adj";

    /// <summary>The calibration files of the fit in use: 수동 조정's (…_adj) or the others'.</summary>
    IEnumerable<string> OwnCalibrationFiles() => (Directory.Exists(CalibrationDir) ? Directory.GetFiles(CalibrationDir, "*.bgra") : [])
        .Where(f => Path.GetFileNameWithoutExtension(f).EndsWith(AdjustSuffix, StringComparison.Ordinal) == _adjusted);

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
        _source = null;
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));
    }
}
