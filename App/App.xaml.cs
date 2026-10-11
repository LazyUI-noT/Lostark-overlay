using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using GaugeOverlay.App.Platform;
using GaugeOverlay.App.Services;
using GaugeOverlay.App.UI;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Net;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App;

/// <summary>Composition root: builds the few services by hand and wires them (integration step).</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        if (e.Args.Contains("--capture-test")) { Platform.CaptureSmokeTest.Run(this); return; }
        if (e.Args.Contains("--demo")) { StartDemo(e.Args); return; }
        if (e.Args.Contains("--party-test")) { PartySelfTest.Run(this); return; }
        if (e.Args.Contains("--build-gauge")) { BuildGauge(e.Args); Shutdown(); return; }

        // --updated: started by the old version after an update, which may still be exiting: wait for its lock.
        if (!AcquireSingleInstance(e.Args.Contains("--updated") ? UpdateHandover : TimeSpan.Zero)) { Shutdown(); return; }
        StartApp();
    }

    static readonly TimeSpan UpdateHandover = TimeSpan.FromSeconds(30);

    /// <summary>--build-gauge &lt;shots&gt; &lt;out&gt; [--region=x,y,w,h]: empty.png and full.png in &lt;shots&gt; (screenshots of the
    /// game at any resolution, HUD size or 21:9, resampled to the reference screen by <see cref="ReferenceShot"/>) → gauge.json,
    /// mask.png, emblem_empty.png and emblem_full.png in &lt;out&gt; (Profiles/aden/README.md). The search region, in reference
    /// pixels, defaults to the emblem and 40 px around it.</summary>
    static void BuildGauge(string[] args)
    {
        var paths = args.Where(a => !a.StartsWith("--")).ToArray();
        string message;
        try
        {
            if (paths.Length != 2) throw new ArgumentException("사용법: --build-gauge <스크린샷 폴더> <출력 폴더> [--region=x,y,w,h]");
            var e = Geometry.RefEmblem;
            var region = args.FirstOrDefault(a => a.StartsWith("--region="))?[9..].Split(',').Select(int.Parse).ToArray() is [var x, var y, var w, var h]
                ? new PixelRect(x, y, w, h) : new PixelRect(e.X - 40, e.Y - 40, e.Width + 80, e.Height + 40);
            var (empty, full, found) = ReferenceShot.Normalize(Png.Load(Path.Combine(paths[0], "empty.png")), Png.Load(Path.Combine(paths[0], "full.png")));
            var (json, mask) = GaugeLoader.Build(empty, full, region);
            Directory.CreateDirectory(paths[1]);
            File.WriteAllText(Path.Combine(paths[1], GaugeLoader.JsonFile), json);
            Png.Save(Path.Combine(paths[1], GaugeLoader.MaskFile), mask);
            // The class's centre piece, empty and full, for telling another gauge class on screen (CharacterService.CheckClass).
            Png.Save(Path.Combine(paths[1], "emblem_empty.png"), ReferenceShot.Emblem(empty));
            Png.Save(Path.Combine(paths[1], "emblem_full.png"), ReferenceShot.Emblem(full));
            message = $"{found}.\n{paths[1]}에 {GaugeLoader.JsonFile}, {GaugeLoader.MaskFile}({mask.Width}×{mask.Height}), emblem_empty.png, emblem_full.png를 만들었습니다.\n\n{json}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or FormatException or OverflowException or NotSupportedException or UnauthorizedAccessException)
        {
            message = ex.Message;
        }
        MessageBox.Show(message, "GaugeOverlay --build-gauge");
    }

    Mutex? _instance;
    readonly List<IDisposable> _owned = [];
    Action? _flush;   // final save on exit
    bool _errorShown;

    bool AcquireSingleInstance(TimeSpan wait)
    {
        var mutex = new Mutex(true, @"Local\GaugeOverlay.SingleInstance", out bool created);
        if (!created && wait > TimeSpan.Zero)
        {
            try { created = mutex.WaitOne(wait); }
            catch (AbandonedMutexException) { created = true; }   // the old process ended without releasing it: ours now
        }
        if (created) _instance = mutex; else mutex.Dispose();   // released on exit only if owned
        return created;
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Trace.WriteLine($"[app] unhandled: {e.Exception}");
        e.Handled = true;
        if (_tray is null)
        {
            // Failed before any UI exists: without this the process would run invisibly and hold the single-instance mutex.
            MessageBox.Show($"GaugeOverlay를 시작하지 못했습니다.\n\n{e.Exception.Message}\n\n로그: %APPDATA%\\GaugeOverlay\\logs", "GaugeOverlay", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        if (_errorShown) return;
        _errorShown = true;
        _tray.Notify("오류가 발생했습니다. 자세한 내용은 로그 파일(%APPDATA%\\GaugeOverlay\\logs)에 있습니다.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _flush?.Invoke(); }
        catch (Exception ex) { Trace.WriteLine($"[app] save on exit failed: {ex}"); }
        foreach (var d in _owned)
            try { d.Dispose(); }
            catch (Exception ex) { Trace.WriteLine($"[app] dispose failed: {ex}"); }
        _instance?.ReleaseMutex();
        base.OnExit(e);
    }

    // ── UI shared by the app and --demo: windows, tray, hotkeys, visibility ─────────────

    Settings _settings = null!;
    CharacterService _characters = null!;
    OverlayWindow _overlay = null!;
    OverlayWindow[] _peers = [];   // one per party slot
    OverlayWindow _buffs = null!;  // 버프 막대 따로 두기: your buff bars on their own
    OverlayWindow _debuffs = null!;   // 디버프: the chosen raid's debuffs and the swap count
    /// <summary>The windows besides yours that follow it: the party slots and the buff bars'.</summary>
    IEnumerable<OverlayWindow> Others => [.. _peers, _buffs, _debuffs];
    SettingsWindow _settingsWindow = null!;
    DebugWindow _debug = null!;
    Tray _tray = null!;
    Hotkeys _hotkeys = null!;
    Action _changed = null!;
    bool _gameForeground = true, _hudVisible = true;

    /// <param name="changed">Runs after any edit of the settings object (settings window, first run, tray).</param>
    void BuildUi(Settings settings, CharacterService characters, Action changed)
    {
        (_settings, _characters) = (settings, characters);
        _changed = changed;
        _overlay = new OverlayWindow(settings);
        _peers = [.. Enumerable.Range(0, Membership.MaxMembers - 1).Select(i => new OverlayWindow(settings, i))];
        _buffs = new OverlayWindow(settings, buffs: true);
        _debuffs = new OverlayWindow(settings, debuffs: true);
        _settingsWindow = new SettingsWindow(settings, characters);
        _debug = new DebugWindow();
        _tray = new Tray();
        _hotkeys = new Hotkeys();
        _owned.AddRange([_tray, _hotkeys]);

        _settingsWindow.SettingsChanged += changed;
        _settingsWindow.EditModeRequested += () => SetEditMode(true);
        _settingsWindow.IsVisibleChanged += (_, _) => UpdatePreview();
        _hotkeys.Pressed += action => Dispatcher.BeginInvoke(() =>
        {
            if (action == nameof(HotkeySettings.EditMode)) SetEditMode(!_overlay.EditMode);
            else if (action == nameof(HotkeySettings.ToggleOverlay)) ToggleOverlay();
            else if (HotkeySettings.ProfileOf(action) is { } profile) SwitchProfile(profile);
        });
        _tray.ProfileChosen += SwitchProfile;

        _tray.ToggleOverlay += ToggleOverlay;
        _tray.ToggleEditMode += () => SetEditMode(!_overlay.EditMode);
        _tray.OpenSettings += () => { _settingsWindow.Show(); _settingsWindow.Activate(); };
        _tray.OpenDebug += () => { _debug.Show(); _debug.Activate(); };
        _tray.OpenLayoutEditor += OpenLayoutEditor;
        _settingsWindow.LayoutEditorRequested += OpenLayoutEditor;
        _tray.CharacterPicked += (family, cls, sub) => { characters.Pick(family, cls, sub); _settingsWindow.Reload(); changed(); };
        _tray.DefaultPicked += () => { characters.PickDefault(); _settingsWindow.Reload(); changed(); };
        _tray.RaidChosen += r => { _settings.Debuffs.Raid = r; _settingsWindow.Reload(); changed(); };
        _tray.Exit += () => Shutdown();
    }

    /// <summary>Applies the settings to the UI side: overlay, hotkeys, tray.</summary>
    void ApplyUi()
    {
        _overlay.Refresh();
        _overlay.ApplyCaptureExclusion();
        foreach (var w in Others) { w.Refresh(); w.ApplyCaptureExclusion(); }
        StackParty();
        var failed = string.Join(", ", _hotkeys.Apply(_settings.Hotkeys));
        if (failed != _failedHotkeys && failed != "")
        {
            Trace.WriteLine($"[app] hotkey not registered: {failed}");
            _tray.Notify($"단축키를 쓸 수 없습니다 (겹치거나, 다른 프로그램이 쓰거나, 지원하지 않는 조합. 마우스 옆 버튼은 더 이상 지원하지 않습니다): {failed}");
        }
        _failedHotkeys = failed;
        _tray.SetHotkeys(_settings.Hotkeys);
        _tray.SetProfile(_settings.ActiveProfile);
        _tray.SetRaid(_settings.Debuffs.Raid);
        _tray.SetCharacters(_characters.ClassTree, _settings.ActiveCharacter);
        UpdateVisibility();   // 오버레이 표시
    }

    /// <summary>오버레이 프로필 (hotkey, tray): the windows go to that profile's spots and sizes, and it is saved.</summary>
    void SwitchProfile(int profile)
    {
        // mid-drag, the drop would land in the other profile's spots
        if (profile == _settings.ActiveProfile || _overlay.Dragging || Others.Any(w => w.Dragging)) return;
        _settings.SwitchProfile(profile);
        _overlay.Replace();
        foreach (var w in Others) w.Replace();
        StackParty();
        _settingsWindow.Reload();
        _editor?.ShowProfile(profile);   // its 적용 would otherwise bring back the profile it has open
        _changed();
        _tray.Notify($"오버레이 프로필 {profile + 1}");
    }

    /// <summary>미리보기: sample data while 설정 is open (it takes the foreground, so the overlay must show regardless),
    /// but not while the 레이아웃 편집기 is open, which draws the overlays itself.</summary>
    void UpdatePreview()
    {
        bool on = _settingsWindow.IsVisible && _editor is null;
        _overlay.Preview = on;
        foreach (var w in Others) w.Preview = on;
        UpdateVisibility();
    }

    LayoutEditorWindow? _editor;
    string _failedHotkeys = "";   // told once, not on every change
    Func<Task<(Image?, PixelRect)>>? _takeShot;   // 게임 화면 찍기; none in the demo
    static readonly TimeSpan ShotSettle = TimeSpan.FromMilliseconds(150), ShotTimeout = TimeSpan.FromSeconds(5);

    /// <summary>One editor at a time; 적용 moves the windows to the new spots and saves (through _changed).</summary>
    void OpenLayoutEditor()
    {
        if (_editor is null)
        {
            _editor = new LayoutEditorWindow(_settings);
            _editor.Applied += () =>
            {
                _overlay.Replace();
                foreach (var w in Others) w.Replace();
                StackParty();
                _settingsWindow.Reload();
                _changed();
            };
            _editor.ShotSaved += () => _changed();
            _editor.PrefsChanged += () => _changed();
            _editor.TakeShot = _takeShot;
            _editor.Closed += (_, _) => { _editor = null; _settingsWindow.EditorOpen = false; UpdatePreview(); };
            _settingsWindow.EditorOpen = true;
            UpdatePreview();
        }
        _editor.Show();
        _editor.Activate();
    }

    void SetEditMode(bool on)
    {
        _overlay.SetEditMode(on);
        foreach (var w in Others) w.SetEditMode(on);
        _tray.SetEditMode(on);
        UpdateVisibility();
        // Dragging boxes gave the overlay the foreground, which hides it (the game isn't in front): give it back to the game.
        if (!on) ReturnToGame();
    }

    void ToggleOverlay()
    {
        _settings.Overlay.Visible = !_settings.Overlay.Visible;
        _settingsWindow.Reload();   // its 오버레이 표시 box
        _changed();
        if (_settings.Overlay.Visible && !_overlay.EditMode) ReturnToGame();   // from the tray: the taskbar has the foreground
    }

    /// <summary>The game back in front (so the overlay shows), unless 설정 is open. Done a moment later: closing the tray menu
    /// hands the foreground back to the taskbar, which would undo a switch made from its click.</summary>
    void ReturnToGame()
    {
        // ponytail: fixed 200 ms for the menu to close; wait on its Closed event if that proves too short on slow PCs.
        var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        later.Tick += (_, _) => { later.Stop(); if (!_settingsWindow.IsVisible) GameWindow.TakeBackForeground(); };
        later.Start();
    }

    /// <summary>Each party member to its slot's window by 파티원 순서 (<see cref="PartySlots.Assign"/>); a slot nobody is
    /// in shows nothing. The settings window hears who is where.</summary>
    void ShowPeers(IReadOnlyList<Player> peers)
    {
        var party = _settings.Party;
        bool iAmSupport = _characters.Role == Role.서포터;
        var placed = PartySlots.Assign(peers, party.SlotNames, PartySlots.SupportSlot(party, iAmSupport),
            p => Emblems.Class(p.Snapshot.ClassId)?.Role == Role.서포터, _peers.Length);
        for (int i = 0; i < _peers.Length; i++) _peers[i].Update(placed[i]);
        StackParty();
        _settingsWindow.SetPartyOrder([.. placed.Select(p => p?.Name)]);
    }

    /// <summary>파티 묶기: the party windows one under another from the first one's spot, plates meeting, as one plate.</summary>
    void StackParty()
    {
        if (!_settings.Party.Together) return;
        for (int i = 1; i < _peers.Length; i++)
        {
            _peers[i - 1].UpdateLayout();
            var (left, top) = (_peers[0].Left, _peers[i - 1].Top + _peers[i - 1].PlateHeight);
            if ((_peers[i].Left, _peers[i].Top) != (left, top)) (_peers[i].Left, _peers[i].Top) = (left, top);
        }
    }

    void UpdateVisibility()
    {
        bool show = _overlay.EditMode || _overlay.Preview || (_settings.Overlay.Visible && _gameForeground && _hudVisible && _settings.FirstRunDone);
        _overlay.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        foreach (var w in _peers) w.Visibility = _overlay.Visibility;
        _buffs.Visibility = _settings.Buffs.Detached ? _overlay.Visibility : Visibility.Hidden;
        _debuffs.Visibility = _settings.Debuffs.Raid != Raid.없음 ? _overlay.Visibility : Visibility.Hidden;
        if (show) { _overlay.BringToFront(); foreach (var w in Others) w.BringToFront(); }
        _tray.SetOverlayVisible(_settings.Overlay.Visible);
    }

    FirstRunWindow ShowFirstRun(Action changed)
    {
        var first = new FirstRunWindow(_settings, _characters);
        first.SettingsChanged += changed;
        first.Completed += () => { _settingsWindow.Reload(); UpdateVisibility(); };
        first.Show();
        return first;
    }

    static string FitText(Viewport vp, Fit? fit, string message) =>
        fit is null ? message : $"맞춤 완료: {vp.Width}×{vp.Height}, HUD {fit.Hud:0}%";

    // ── normal startup: the composition root (ARCHITECTURE.md §3) ───────────────────────

    void StartApp()
    {
        Trace.Listeners.Add(new TextWriterTraceListener(LogPath()));
        Trace.AutoFlush = true;

        var store = new SettingsStore(SettingsStore.DefaultPath);
        var settings = store.Load(out bool wasReset);
        var debounce = new SaveDebounce();
        var characters = new CharacterService(settings, CharacterService.DefaultProfilesDir);

        var engine = new Engine();
        var foreground = new ForegroundWatcher();
        var device = DeviceKeyFile.Load(DeviceKeyFile.DefaultPath);
        var party = new PartyService(device);
        _owned.AddRange([party, device, foreground, engine]);
        _gameForeground = foreground.IsGameForeground;
        engine.GameForeground = _gameForeground;   // calibration pictures and 스킬창 맞추기 only while the game is in front

        // 게임 화면 찍기 (레이아웃 편집기): 설정 and the overlays (unless kept out of capture) step aside for the picture.
        engine.ScreenshotCaptured += (token, img, area) => ((TaskCompletionSource<(Image?, PixelRect)>)token).TrySetResult((img, area));
        _takeShot = async () =>
        {
            bool hide = !settings.Overlay.ExcludeFromCapture, settingsShown = _settingsWindow.IsVisible;
            if (settingsShown) _settingsWindow.Hide();
            if (hide) foreach (var w in Others.Append(_overlay)) w.Visibility = Visibility.Hidden;
            try
            {
                await Task.Delay(ShotSettle);   // until they are off the screen
                // Continuations run here (the UI thread), not on the engine thread that answers.
                var answer = new TaskCompletionSource<(Image?, PixelRect)>(TaskCreationOptions.RunContinuationsAsynchronously);
                engine.RequestScreenshot(answer);
                return await answer.Task.WaitAsync(ShotTimeout);
            }
            catch (TimeoutException) { return (null, default); }
            finally
            {
                if (settingsShown) _settingsWindow.Show();
                UpdateVisibility();
            }
        };

        Snapshot? latest = null;
        IReadOnlyList<Player> peers = [];
        string? colorsFor = settings.ActiveCharacter;   // the character whose look was last sent
        int pending = 0;   // coalesces snapshot posts so at most one waits on the UI thread
        var wrongClassTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };   // ClassWarning → 기본
        FirstRunWindow? first = null;
        var iconStore = new IconStore(IconStore.DefaultDir);   // 아이콘으로 표시 (LoadIcons below)
        var haveIcons = new HashSet<SlotId>();   // slots with an icon now: loaded or copied
        string? iconsFor = null;   // "클래스_직업각인" whose icons are loaded, "" for none; null before the first Configure
        bool copyingIcons = false, rereadingIcons = false, iconSaveFailed = false;
        int iconRun = 0;   // the engine's copy run now (Engine.CopyIcons); icons of an earlier run are dropped
        BuildUi(settings, characters, OnSettingsChanged);

        // A failed write (file locked, disk full) is logged and left pending, so the timer retries it.
        void Write()
        {
            try { store.Save(settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.WriteLine($"[app] settings save failed: {ex.Message}");
                debounce.Changed(Clock.Now());
            }
        }
        void Save(bool now)
        {
            if (now) { debounce.Flush(); Write(); }
            else debounce.Changed(Clock.Now());
        }
        _flush = () => Save(now: true);
        string PartyName() => string.IsNullOrWhiteSpace(settings.Party.DisplayName) ? "플레이어" : settings.Party.DisplayName.Trim();

        // Your colours, box size and layout as you see them, for members who receive them.
        Look MyLook() { var a = ClassProfiles.Mine(settings); return new Look(a.Colors, a.Width, a.Height, a.Layout); }

        // 게이지 읽기 예측 (beta) learns per class: a 초각스 of another class than the one set turns the gauge red and
        // predictions off until the class is put right.
        bool ClassWarning()
        {
            bool warn = settings.Gauge.ReadMode == GaugeReadMode.Predicted && characters.ClassMismatch;
            if (warn && !_overlay.Renderer.ClassWarning)
                _tray.Notify(characters.ShownClass is { } shown
                    ? $"스킬창 가운데가 {shown}(으)로 보입니다. 고른 클래스({characters.ActiveClass?.Name})를 확인하세요. 게이지 예측은 멈췄습니다."
                    : $"초각스 아이콘이 고른 클래스({characters.ActiveClass?.Name})와 다릅니다. 클래스를 확인하세요. 게이지 예측은 멈췄습니다.");
            _overlay.Renderer.ClassWarning = warn;
            // About 10 s of flashing with no fix: back to 기본, so a wrong class doesn't stay on.
            if (warn) { if (!wrongClassTimer.IsEnabled) wrongClassTimer.Start(); }
            else wrongClassTimer.Stop();
            return warn;
        }
        wrongClassTimer.Tick += (_, _) =>
        {
            wrongClassTimer.Stop();
            if (!_overlay.Renderer.ClassWarning) return;
            characters.PickDefault();
            _settingsWindow.Reload();
            _tray.Notify("클래스가 맞지 않아 기본으로 돌아갔습니다. 스킬창 다시 맞추기(Ctrl+Shift+D)나 트레이에서 클래스를 고르세요.");
            OnSettingsChanged();
        };

        // 아이콘으로 표시: each 직업각인's icons are saved (IconStore) and copied from the screen only on 아이콘 다시 읽기, or while
        // a slot drawn as an icon has none. With no 직업각인 they are copied for the session and not saved.
        (string Cls, string Sub)? IconKey() =>
            characters.Active is { Subclass.Length: > 0 } c && characters.ActiveClass is not null ? (c.Class, c.Subclass) : null;
        IReadOnlySet<SlotId> NeededIcons()
        {
            var look = ClassProfiles.Mine(settings);
            return SkillIcon.Needed(look.Effects?.Icons ?? new HashSet<SlotGroup>(), look.Groups, look.Hidden, characters.AdenSlots);
        }
        // The character or its 직업각인 changed: its saved icons are drawn at once, and a copy run starts only if some are missing.
        void LoadIcons()
        {
            var key = IconKey();
            string id = key is { } k ? $"{k.Cls}_{k.Sub}" : "";
            if (id == iconsFor) return;
            iconsFor = id;
            _overlay.Renderer.ClearIcons();
            haveIcons.Clear();
            rereadingIcons = false;
            if (key is { } loaded)
                foreach (var (slot, icon) in iconStore.Load(loaded.Cls, loaded.Sub)) { _overlay.Renderer.SetIcon(slot, icon); haveIcons.Add(slot); }
            copyingIcons = false;   // a run for the new 직업각인 starts fresh (UpdateIconCopy)
            engine.CopyIcons(false);
        }
        void UpdateIconCopy()
        {
            bool complete = SkillIcon.Complete(haveIcons, NeededIcons());
            if (complete && rereadingIcons) { rereadingIcons = false; _tray.Notify("아이콘을 모두 다시 읽었습니다."); }
            // Another class on screen (ClassWarning): its icons aren't this character's. A fresh run starts once it's put right.
            bool copy = !complete && !_overlay.Renderer.ClassWarning;
            if (copy == copyingIcons) return;
            copyingIcons = copy;
            iconRun = engine.CopyIcons(copy);
        }
        void OnIcon(SlotId slot, Image icon, int run)
        {
            if (!copyingIcons || run != iconRun) return;   // read before this run: another character's, or before a re-read
            _overlay.Renderer.SetIcon(slot, icon);
            haveIcons.Add(slot);
            if (IconKey() is { } k)
            {
                try { iconStore.Save(k.Cls, k.Sub, slot, icon); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.WriteLine($"[icons] save failed: {ex.Message}");
                    if (!iconSaveFailed) { iconSaveFailed = true; _tray.Notify("아이콘을 저장하지 못했습니다: " + ex.Message); }
                }
            }
            UpdateIconCopy();
        }
        // 아이콘 다시 읽기 (tray, 설정, hotkey): the 직업각인's saved icons are dropped and every slot is copied at its next ready read.
        void RereadIcons()
        {
            if (NeededIcons().Count == 0) { _tray.Notify("아이콘으로 표시가 켜진 칸이 없습니다. 설정 → 내 오버레이 → 스킬 칸에서 켜세요."); return; }
            if (IconKey() is { } k)
            {
                try { iconStore.Clear(k.Cls, k.Sub); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[icons] clear failed: {ex.Message}"); }
            }
            _overlay.Renderer.ClearIcons();
            haveIcons.Clear();
            rereadingIcons = true;
            copyingIcons = false;   // a new run, even if one was on
            engine.CopyIcons(false);
            UpdateIconCopy();
            _tray.Notify("아이콘을 다시 읽습니다. 스킬이 준비되면 칸마다 읽습니다.");
        }

        void Configure()
        {
            if (settings.ActiveCharacter != colorsFor)
            {
                colorsFor = settings.ActiveCharacter;
                party.PublishLook(MyLook());
            }
            bool wrongClass = ClassWarning();   // another class on screen: its gauge is not this character's to learn from
            LoadIcons();
            UpdateIconCopy();   // also when 아이콘으로 표시 was just turned on for a group with nothing saved, or the class put right
            engine.Configure(new Engine.Config(new Dictionary<string, Fit>(settings.Fits), characters.AdenSlots, _debug.IsVisible,
                ClassProfiles.Reads(settings, characters.AdenSlots), characters.Gauge, characters.ActiveClass?.AdenRects, characters.AdenDark,
                characters.GaugeRule, wrongClass ? GaugeReadMode.Agreed : settings.Gauge.ReadMode, wrongClass ? null : settings.ActiveCharacter,
                settings.Debuffs.Raid, settings.Buffs.Size, settings.ManualHud, settings.ManualScreen, settings.ActiveAdjusts()));
            if (settings.SyncPlace()) _overlay.Replace();   // a 개별 직업각인 has its own spot and size
            ApplyUi();
            Draw();
        }

        // Draws yourself and the party; publishing again is a no-op unless something changed.
        void Draw()
        {
            // The engine doesn't know the class: yours is added here, for the party and for your own gauge's glow rule.
            var mine = latest is { } s ? s with { ClassId = characters.ClassId } : null;
            if (mine is not null) party.Publish(mine, PartyName(), settings.Party.SendGroups(), settings.Party.SendGauge);
            _overlay.Update(mine is not null ? new Player("", mine) : null);
            _buffs.Update(mine is not null ? new Player("", mine) : null);
            _debuffs.Update(mine is not null ? new Player("", mine) : null);
            ShowPeers(peers);
        }

        void OnSettingsChanged() { Configure(); Save(now: false); }

        // Engine → UI (engine thread → dispatcher).
        engine.SnapshotReady += s =>
        {
            latest = s;
            if (Interlocked.Exchange(ref pending, 1) == 0)
                Dispatcher.BeginInvoke(() => { Interlocked.Exchange(ref pending, 0); Draw(); });
        };
        engine.GameRunningChanged += running => Dispatcher.BeginInvoke(() => _tray.SetGameRunning(running));
        engine.HudVisibleChanged += visible => Dispatcher.BeginInvoke(() => { _hudVisible = visible; UpdateVisibility(); });
        engine.FitNeeded += vp => Dispatcher.BeginInvoke(() =>
        {
            if (settings.FirstRunDone) _tray.Notify($"게임 화면 크기({vp.Width}×{vp.Height})에 맞춘 스킬창이 없습니다. 수련장에서 설정 → 스킬창 → 스킬창 맞추기를 눌러 주세요.");
        });
        engine.FitFinished += (vp, fit, message, detected, test) => Dispatcher.BeginInvoke(() =>
        {
            // 테스트 모드 (as the engine ran the fit, not as the flag is now: it may have been switched meanwhile): a fit of the
            // video in front is the engine's alone (memory); the game's saved fits stay as they are.
            if (detected is not null && !test) settings.DetectedScreen = detected;   // 직접 입력 starts from it
            if (fit is not null && !test) { settings.Fits[Geometry.ViewportKey(vp)] = fit; Configure(); Save(now: true); }
            var text = (test ? "테스트 모드: " : "") + FitText(vp, fit, message);
            _settingsWindow.SetFitResult(text);
            first?.SetFitResult(fit is not null, text);
            _tray.Notify(text);
        });
        engine.EmblemCaptured += emblem => Dispatcher.BeginInvoke(() =>
        {
            if (characters.OnEmblem(emblem) is { } note)
            {
                _settingsWindow.Reload();
                _tray.Notify(note);
            }
            characters.CheckClass(emblem);   // against the class now set; Configure (below) warns if it's another
            OnSettingsChanged();   // also saves a newly learned emblem
        });
        engine.IconSeen += (slot, icon, run) => Dispatcher.BeginInvoke(() => OnIcon(slot, icon, run));
        // A character picked by hand (tray, 설정 or first run, all through CharacterService.Pick): its emblem is learned.
        characters.Picked += engine.RequestEmblem;
        _settingsWindow.GainsResetRequested += () => { engine.ResetGains(); _tray.Notify($"{settings.ActiveCharacter}의 배운 게이지 값을 지웠습니다."); };
        // 스킬창 다시 맞추기 by hotkey (the tray and 설정 below): the same Refit.
        _hotkeys.Pressed += action => { if (action == nameof(HotkeySettings.DetectCharacter)) Dispatcher.BeginInvoke(() => Refit()); };
        engine.SubclassSlotCaptured += (slot, greyedSkills) => Dispatcher.BeginInvoke(() =>
        {
            if (characters.OnSubclassSlot(slot, greyedSkills) is not { } note) { OnSettingsChanged(); return; }   // ClassMismatch or the history may have changed
            _settingsWindow.Reload();
            _tray.Notify(note);
            OnSettingsChanged();
        });
        // Same coalescing as SnapshotReady: keep only the latest row per slot and post one drain at a time.
        var debugRows = new System.Collections.Concurrent.ConcurrentDictionary<SlotId, Engine.DebugRow>();
        int debugPending = 0;
        engine.Debug += row =>
        {
            debugRows[row.Slot] = row;
            if (Interlocked.Exchange(ref debugPending, 1) != 0) return;
            Dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref debugPending, 0);
                foreach (var slot in debugRows.Keys.ToList())
                    if (debugRows.TryRemove(slot, out var r)) _debug.Update(r.Slot, r.Crop, r.Reading, r.State);
            });
        };

        // Buff, debuff and boss rows of the debug window, coalesced the same way (latest per key).
        var extraRows = new System.Collections.Concurrent.ConcurrentDictionary<string, Engine.ExtraDebugRow>();
        int extraPending = 0;
        engine.ExtraDebug += row =>
        {
            extraRows[row.Key] = row;
            if (Interlocked.Exchange(ref extraPending, 1) != 0) return;
            Dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref extraPending, 0);
                foreach (var key in extraRows.Keys.ToList())
                    if (extraRows.TryRemove(key, out var r)) _debug.UpdateExtra(r.Key, r.Crop, r.Reading, r.State);
            });
        };

        // Platform → engine / UI.
        foreground.Changed += fg => { engine.GameForeground = fg; Dispatcher.BeginInvoke(() => { _gameForeground = fg; UpdateVisibility(); }); };
        // 테스트 모드 (tray or hotkey): a window (a video of the game) is watched instead of LOSTARK: the one picked from the tray's
        // list, or (picked zero: the hotkey, 맨 앞 창 따라가기) whichever was last in front.
        void SetTest(bool on, IntPtr picked, string? notice = null)
        {
            GameWindow.Picked = on ? picked : 0;
            GameWindow.TestMode = on;
            if (on && picked != 0) GameWindow.Show(picked);   // the overlay and the fit need it in front
            foreground.Recheck();
            if (on) engine.RequestDetect();   // the video's character
            _tray.SetTestMode(on, GameWindow.Picked);
            _tray.Notify(notice ?? (!on ? "테스트 모드를 껐습니다. 로스트아크 창을 찾습니다."
                : picked != 0 ? "테스트 모드: 고른 창을 게임으로 보고, 그 화면에서 스킬창을 찾습니다. 저장된 맞춤은 쓰지도 바꾸지도 않습니다."
                : "테스트 모드: 맨 앞에 있던 창(영상 플레이어 등)을 게임으로 보고, 그 화면에서 스킬창을 찾습니다. 저장된 맞춤은 쓰지도 바꾸지도 않습니다. 다른 창을 보려면 그 창을 클릭하거나 트레이 → 테스트 모드에서 고르세요."));
        }
        void ToggleTestMode() => SetTest(!(GameWindow.TestMode && GameWindow.Picked == 0), 0);
        _tray.ToggleTestMode += ToggleTestMode;
        _tray.TestWindowPicked += hwnd =>
        {
            if (GameWindow.Exists(hwnd)) SetTest(true, hwnd);
            else _tray.Notify("그 창은 이미 닫혔습니다. 트레이 → 테스트 모드를 다시 열어 고르세요.");
        };
        _tray.TestOff += () => SetTest(false, 0);
        _tray.TestMenuOpening += () => _tray.SetTestWindows(GameWindow.Choices(), GameWindow.TestMode, GameWindow.Picked);
        _hotkeys.Pressed += action => { if (action == nameof(HotkeySettings.TestMode)) SetTest(!GameWindow.TestMode, 0); };
        // The picked window closed: 테스트 모드 ends (minimised, the engine just pauses). Checked again here: the engine
        // raises it each step until then, and the user may have picked another window meanwhile.
        engine.PickedWindowClosed += hwnd => Dispatcher.BeginInvoke(() =>
        {
            if (GameWindow.TestMode && GameWindow.Picked == hwnd && !GameWindow.Exists(hwnd))
                SetTest(false, 0, "테스트 모드: 고른 창이 닫혀 테스트 모드를 껐습니다.");
        });

        // UI → settings / engine.
        // 스킬창 다시 맞추기 (tray, 설정, hotkey): fit again, since the 아덴 keys may have moved, then detect the character, even
        // when the fit fails. A fit needs the game in front: from the tray or 설정 the taskbar covers the skill bar (Engine.FitNow waits).
        // With a 수동 조정 applied to this game area it asks first: drop it and fit (자동), or keep it and only detect the class.
        void Refit()
        {
            if (engine.GameAreaKey is { } key && settings.AdjustActive(key))
            {
                if (MessageBox.Show("수동 조정을 버리고 스킬창을 다시 맞출까요?\n\n아니요: 수동 조정은 그대로 두고 클래스와 직업각인만 다시 확인합니다.",
                        "GaugeOverlay", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                {
                    GameWindow.Activate();
                    engine.RequestDetect();
                    return;
                }
                settings.DiscardAdjust(key);
                Configure();
                Save(now: true);
                _settingsWindow.Reload();
            }
            GameWindow.Activate(); engine.RequestFit(); engine.RequestDetect();
        }
        _settingsWindow.RefitRequested += Refit;
        WireAdjust(engine, settings, Configure, () => Save(now: true));
        _overlay.LayoutCommitted += () => { Save(now: true); _settingsWindow.Reload(); };   // 숫자 크기 range follows the box size
        foreach (var w in Others) w.LayoutCommitted += () => Save(now: true);
        _debug.IsVisibleChanged += (_, _) => Configure();
        _tray.Refit += Refit;
        // 직업각인 다시 감지 (tray, 설정, hotkey): the 초각스 is read again past the history (CharacterService.RedetectSubclass).
        void RedetectSubclass()
        {
            if (characters.RedetectSubclass() is { } why) { _tray.Notify(why); return; }
            GameWindow.Activate();
            engine.RequestSubclass();
            _tray.Notify("직업각인을 다시 읽습니다. 게임에서 초각스가 준비되면 읽습니다.");
        }
        _tray.DetectSubclass += RedetectSubclass;
        _settingsWindow.SubclassRedetectRequested += RedetectSubclass;
        _hotkeys.Pressed += action => { if (action == nameof(HotkeySettings.DetectSubclass)) Dispatcher.BeginInvoke(RedetectSubclass); };
        _tray.RereadIcons += RereadIcons;
        _settingsWindow.IconRereadRequested += RereadIcons;
        _hotkeys.Pressed += action => { if (action == nameof(HotkeySettings.RereadIcons)) Dispatcher.BeginInvoke(RereadIcons); };
        // 스킬 등록 (설정 and the tray): the ready 일반 slots of the next frame the bar is on screen; nothing within 3 s is no bar.
        DispatcherTimer? skillsWait = null;
        void SkillsResult(string text) { engine.CancelSkills(); skillsWait?.Stop(); skillsWait = null; _settingsWindow.SetSkillsResult(text); _tray.Notify(text); }
        void SubmitSkills()
        {
            if (characters.SkillsBlocked is { } why) { SkillsResult(why); return; }
            skillsWait?.Stop();
            skillsWait = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Normal,
                (_, _) => SkillsResult("스킬창이 보이지 않습니다. 게임 화면에 스킬창이 보일 때 다시 눌러 주세요."), Dispatcher);
            GameWindow.Activate();
            engine.RequestSkills();
        }
        engine.SkillsCaptured += ready => Dispatcher.BeginInvoke(() => { if (skillsWait is not null) SkillsResult(characters.SubmitSkills(ready)); });
        _settingsWindow.SkillsSubmitRequested += SubmitSkills;
        _tray.SubmitSkills += SubmitSkills;
        _settingsWindow.SkillsClearRequested += () => SkillsResult(characters.ClearSkills());

        // Party (background threads → dispatcher). Handlers read the latest values when they run, so the last one wins.
        party.PeersChanged += _ => Dispatcher.BeginInvoke(() => { peers = party.Peers; Draw(); });
        party.StatusChanged += _ => Dispatcher.BeginInvoke(() =>
        {
            _tray.SetParty(party.Status);
            _settingsWindow.SetPartyState(party.Status, party.Token, isHost: party.IsHost);
        });
        party.Notice += text => Dispatcher.BeginInvoke(() =>
        {
            _tray.Notify(text);
            _settingsWindow.SetPartyState(party.Status, party.Token, text, party.IsHost);
        });
        bool CopyToken()
        {
            if (party.Token is not { } t) return false;
            try { Clipboard.SetText(t); return true; }
            catch (System.Runtime.InteropServices.COMException) { return false; }
        }
        void CopyTokenWithNotice() => _tray.Notify(party.Token is null ? "토큰은 호스트만 복사할 수 있습니다."
            : CopyToken() ? "토큰을 복사했습니다." : "클립보드를 쓸 수 없습니다. 잠시 뒤 다시 눌러 주세요.");
        async void HostParty()
        {
            party.PublishLook(MyLook());
            if (!await party.Host(settings.Party.HostPort, PartyName())) return;
            if (party.Token is { } t) TokenDialog.ShowToken(t, settings.Party.HostPort);
            else _tray.Notify("파티를 열었습니다.");
        }
        async void JoinParty(string? token)
        {
            if (token is null) return;
            party.PublishLook(MyLook());
            if (await party.Join(token, PartyName())) _tray.Notify("파티에 참가했습니다.");
        }
        _tray.HostParty += HostParty;
        _tray.JoinParty += () => JoinParty(JoinDialog.Ask());
        _tray.CopyToken += CopyTokenWithNotice;
        _tray.LeaveParty += party.Leave;
        _settingsWindow.HostRequested += HostParty;
        _settingsWindow.JoinRequested += JoinParty;
        _settingsWindow.LeaveRequested += party.Leave;
        _settingsWindow.CopyTokenRequested += CopyTokenWithNotice;
        _settingsWindow.LookUpdateRequested += () => party.PublishLook(MyLook());
        _settingsWindow.KickRequested += name => _tray.Notify(party.Kick(name) ? $"{name}님을 내보냈습니다." : $"{name}님을 내보내지 못했습니다.");
        WireAdmission(party, settings, () => Save(now: false));

        // Saving: debounced edits are flushed by a light timer; everything is flushed on exit.
        new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => { if (debounce.Due(Clock.Now())) Write(); }, Dispatcher).Start();

        Configure();
        _overlay.Show();
        foreach (var w in Others) w.Show();
        UpdateVisibility();
        engine.Start();

        if (wasReset) _tray.Notify("설정 파일을 읽지 못해 기본값으로 시작했습니다.");
        if (!settings.FirstRunDone)
        {
            first = ShowFirstRun(OnSettingsChanged);
            first.FitRequested += () => { GameWindow.Activate(); engine.RequestFit(); };   // as Refit: the game in front
            first.Completed += () => Save(now: true);
        }

        // 업데이트: checked once per start; nothing is downloaded until the user presses the button in 설정.
        string appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        UpdateInstaller.Cleanup(appDir, Updater.UpdateDir);   // what the last update left (the old exe and Profiles)
        var updater = new Updater();
        _owned.Add(updater);
        _settingsWindow.UpdateRequested += async release =>
        {
            // An event handler: nothing may escape it, or the app goes down in the middle of an update.
            try
            {
                _settingsWindow.SetUpdateBusy(true);
                if (await updater.InstallAsync(release, appDir) is { } why) { _settingsWindow.SetUpdateBusy(false); _tray.Notify(why); return; }
                (settings.Updates.WhatsNewTag, settings.Updates.WhatsNew) = (release.Tag, UpdateCheck.PlainNotes(release.Notes));   // shown once by the new version
                Save(now: true);
                Process.Start(new ProcessStartInfo(Path.Combine(appDir, UpdateInstaller.ExeName), "--updated") { UseShellExecute = false });
                Shutdown();
            }
            catch (Exception ex)
            {
                // Installed or not, this version keeps running (its exe was only renamed); a restart picks up whatever is in place.
                Trace.WriteLine($"[update] failed: {ex}");
                _settingsWindow.SetUpdateBusy(false);
                MessageBox.Show($"업데이트를 마치지 못했습니다. GaugeOverlay를 다시 실행해 주세요.\n\n{ex.Message}", "GaugeOverlay 업데이트",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        _settingsWindow.CheckRequested += async () =>
        {
            try
            {
                var (ok, newer) = await updater.CheckAsync();
                _settingsWindow.SetCheckResult(ok, newer);
            }
            catch (Exception ex) { Trace.WriteLine($"[update] check: {ex}"); _settingsWindow.SetCheckResult(false, null); }
        };
        if (settings.Updates.Check) _ = CheckForUpdate();
        ShowWhatsNew();

        // The first start after an update: what changed, once. Dropped if this isn't the version that was installed.
        void ShowWhatsNew()
        {
            var (tag, notes) = (settings.Updates.WhatsNewTag, settings.Updates.WhatsNew);
            if (tag.Length == 0) return;
            (settings.Updates.WhatsNewTag, settings.Updates.WhatsNew) = ("", "");
            Save(now: true);
            if (!Version.TryParse(tag.TrimStart('v'), out var installed) || UpdateCheck.IsNewer(installed, Updater.Current) || UpdateCheck.IsNewer(Updater.Current, installed)) return;
            Dispatcher.BeginInvoke(() => MessageBox.Show(
                notes.Length > 0 ? $"{tag}(으)로 업데이트했습니다.\n\n{notes}" : $"{tag}(으)로 업데이트했습니다.",
                "GaugeOverlay 업데이트", MessageBoxButton.OK, MessageBoxImage.Information), DispatcherPriority.ApplicationIdle);
        }

        async Task CheckForUpdate()
        {
            try
            {
                if ((await updater.CheckAsync()).Newer is not { } release) return;
                _settingsWindow.SetUpdate(release);
                if (settings.Updates.Notified == release.Tag) return;   // told once per version
                settings.Updates.Notified = release.Tag;
                Save(now: false);
                _tray.Notify($"GaugeOverlay {release.Tag}이(가) 있습니다. 설정에서 업데이트할 수 있습니다.",
                    () => { _settingsWindow.Show(); _settingsWindow.Activate(); });
            }
            catch (Exception ex) { Trace.WriteLine($"[update] check: {ex}"); }   // never shown: the app runs on as it is
        }
    }

    static string LogPath()
    {
        var dir = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "GaugeOverlay.log");
        if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", overwrite: true);
        return path;
    }

    // ── --demo: the real UI driven by DemoSnapshots; no capture, no party, nothing saved ──────
    // Flags: --settings, --first-run, --debug, --edit, --editor (레이아웃 편집기), --full (its 전체 화면), --pick=<Part> (a part clicked, in the editor and the 오버레이 tab), --tab=<settings tab header>, --countdown, --cooldown=<CooldownStyle>, --fill=<FillFrom>,
    // --theme=<UiTheme>, --peer-colors=<ColorSource>, --support=<number 1–4>, --my-number=<1–4>, --name-length=<2–14>, --receive (파티원 크기·배치 받기),
    // --glow (계속 빛남 in chosen colours and 쿨 돈 뒤 지난 시간 on 일반스킬 and 초각스), --icons (아이콘으로 표시 on 일반스킬 and 초각스).
    void StartDemo(string[] args)
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(Path.GetTempPath(), "GaugeOverlay-demo.log")));
        Trace.AutoFlush = true;

        var settings = new Settings { FirstRunDone = !args.Contains("--first-run") };
        string? Flag(string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}="))?[(name.Length + 3)..];
        if (Enum.TryParse(Flag("cooldown"), out CooldownStyle style)) settings.Overlay.CooldownStyle = style;
        if (Enum.TryParse(Flag("fill"), out FillFrom from)) settings.Overlay.FillFrom = from;
        if (Enum.TryParse(Flag("peer-colors"), out ColorSource source)) ClassProfiles.Edit(settings, ClassProfiles.Template).Members.Color = source;
        if (int.TryParse(Flag("support"), out int support)) (settings.Party.SupportFixed, settings.Party.SupportNumber) = (true, support);
        if (int.TryParse(Flag("my-number"), out int number)) settings.Party.MyNumber = number;
        if (int.TryParse(Flag("name-length"), out int nameLength)) settings.Party.NameLength = nameLength;
        if (Enum.TryParse(Flag("theme"), out UiTheme theme)) settings.Theme = theme;
        if (args.Contains("--receive")) (settings.Party.ReceiveSize, settings.Party.ReceiveLayout) = (true, true);
        if (args.Contains("--side-buffs")) settings.Buffs.Side = true;
        if (args.Contains("--detach-buffs")) settings.Buffs.Detached = true;
        if (args.Contains("--debuffs")) settings.Debuffs.Raid = Raid.모르둠;
        if (args.Contains("--bound-party")) settings.Party.Together = true;
        if (args.Contains("--glow"))
        {
            var fx = ClassProfiles.Edit(settings, ClassProfiles.Template).Mine;
            (fx.Pulse, fx.CustomGlow, fx.ReadyCount) = ([.. Enum.GetValues<SlotGroup>()], true, [SlotGroup.일반스킬, SlotGroup.초각스]);
            (fx.PulseColor[SlotGroup.일반스킬], fx.PulseColor[SlotGroup.초각스]) = ("#FFD400", "#00E5FF");
        }
        if (args.Contains("--icons")) ClassProfiles.Edit(settings, ClassProfiles.Template).Mine.Icons = [SlotGroup.일반스킬, SlotGroup.초각스];
        if (args.Contains("--spread"))   // 각성기 placed apart, 일반스킬 hidden while cooling
        {
            var mine = ClassProfiles.Edit(settings, ClassProfiles.Template).Mine;
            var cells = FreeLayout.Default(settings.Gauge.Thickness);
            foreach (var part in new[] { Part.각성기, Part.변화 }) cells[part] = cells[part] with { X = cells[part].X - 160, Y = cells[part].Y - 120 };
            mine.Layout = cells;
            mine.HideOnCooldown.Add(SlotGroup.일반스킬);
        }
        settings.Characters["워로드 · 고독한기사"] = new CharacterSettings
        {
            ClassFamily = "워리어", Class = "워로드", Subclass = "고독한기사",
        };
        settings.ActiveCharacter = "워로드 · 고독한기사";
        if (args.Contains("--bar"))
        {
            var bar = GaugeBar.Edit(settings, "워로드", "고독한기사");
            (bar.Splits, bar.UnifiedColor, bar.SegmentColors, bar.SplitThickness) = ([0.25, 0.5, 0.75], false, ["#1E9BE6", "#2EC4B6", "#E8B53A", "#E8262B"], 2);
        }
        BuildUi(settings, new CharacterService(settings, CharacterService.DefaultProfilesDir), ApplyUi);
        _tray.SetParty("호스트 중 (3/4)");
        var demoFit = FitText(new Viewport(0, 0, 1920, 1080), new Fit(100), "");
        _settingsWindow.RefitRequested += () => _settingsWindow.SetFitResult(demoFit);

        var demo = new DemoSnapshots();
        void Push()
        {
            if (!demo.TryNext(out var self, out var peers)) return;
            _overlay.Update(self);
            _buffs.Update(self);
            _debuffs.Update(self);
            ShowPeers(peers);
            var now = Clock.Now();
            foreach (var st in self.Snapshot.Slots)
            {
                var (crop, reading) = DemoSnapshots.DebugRow(st, now);
                _debug.Update(st.Slot, crop, reading, st);
            }
        }
        Push();
        new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Normal, (_, _) => Push(), Dispatcher).Start();

        ApplyUi();
        _overlay.Show();
        foreach (var w in Others) w.Show();
        UpdateVisibility();
        if (args.Contains("--settings")) _settingsWindow.Show();
        if (Flag("tab") is { } tab) _settingsWindow.ShowTab(tab);
        if (args.Contains("--first-run")) { var first = ShowFirstRun(ApplyUi); first.FitRequested += () => first.SetFitResult(true, demoFit); }
        if (args.Contains("--debug") || args.Contains("--adjust")) { _debug.Capturable = true; _debug.Show(); }
        if (args.Contains("--adjust")) _debug.ShowAdjust();
        if (args.Contains("--edit")) SetEditMode(true);
        if (args.Contains("--editor")) OpenLayoutEditor();
        if (Enum.TryParse(Flag("pick"), out Part picked)) { _settingsWindow.ShowBox(picked, plate: false); _editor?.DemoPick(picked); }
        if (args.Contains("--full")) _editor?.DemoFullScreen();
        if (args.Contains("--countdown")) _ = CountdownWindow.Run(new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight), capturable: true);
    }
}
