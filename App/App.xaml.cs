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
        if (e.Args.FirstOrDefault(a => a.StartsWith("--capture-test=")) is { } window) { Platform.CaptureSmokeTest.RunWindow(this, window["--capture-test=".Length..]); return; }
        if (e.Args.Contains("--demo")) { StartDemo(e.Args); return; }
        if (e.Args.Contains("--party-test")) { PartySelfTest.Run(this); return; }
        if (e.Args.Contains("--build-gauge")) { BuildGauge(e.Args); Shutdown(); return; }
        if (e.Args.Contains("--check-submission")) { CheckSubmission(e.Args); Shutdown(); return; }
        if (e.Args.Contains("--trace-gauge")) { TraceGauge(e.Args); Shutdown(); return; }

        // --updated: started by the old version after an update, which may still be exiting: wait for its lock.
        if (!AcquireSingleInstance(e.Args.Contains("--updated") ? UpdateHandover : TimeSpan.Zero)) { Shutdown(); return; }
        StartApp();
    }

    static readonly TimeSpan UpdateHandover = TimeSpan.FromSeconds(30);
    static readonly TimeSpan SaveOnExit = TimeSpan.FromSeconds(3);   // a fight record still being written when the app closes

    /// <summary>An intake folder's gauge_&lt;kind&gt;.png (_NEED.md), else the older &lt;kind&gt;.png.</summary>
    static string Shot(string dir, string kind) =>
        File.Exists(Path.Combine(dir, $"gauge_{kind}.png")) ? Path.Combine(dir, $"gauge_{kind}.png") : Path.Combine(dir, $"{kind}.png");

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
    OverlayWindow _judgment = null!;   // 단심 (설정 → 기타): 심판's cycle
    OverlayWindow _raid = null!;   // 설정 → 레이드: the boss bar count
    OverlayWindow _meter = null!;   // DPS 미터기
    PartyService? _partyService;   // set by StartApp (null in the demo)
    IReadOnlyList<RaidGate> _raidGates = [];   // Profiles/raids.json
    string? _raidSeen;   // the gate 자동 인식 recognises now
    /// <summary>The windows besides yours that follow it: the party slots and the buff bars'.</summary>
    IEnumerable<OverlayWindow> Others => [.. _peers, _buffs, _debuffs, _judgment, _raid, _meter];
    SettingsWindow _settingsWindow = null!;
    AdjustLayer _layer = null!;
    Tray _tray = null!;
    Hotkeys _hotkeys = null!;
    Action _changed = null!;
    bool _gameForeground = true, _hudVisible = true, _ownForeground;   // own: one of the app's windows (설정) is in front

    /// <param name="changed">Runs after any edit of the settings object (settings window, first run, tray).</param>
    void BuildUi(Settings settings, CharacterService characters, Action changed)
    {
        (_settings, _characters) = (settings, characters);
        _changed = changed;
        _overlay = new OverlayWindow(settings);
        _peers = [.. Enumerable.Range(0, Membership.PartySize - 1).Select(i => new OverlayWindow(settings, i))];
        _buffs = new OverlayWindow(settings, buffs: true);
        _debuffs = new OverlayWindow(settings, debuffs: true);
        _judgment = new OverlayWindow(settings, judgment: true);
        _raid = new OverlayWindow(settings, raid: true);
        _meter = new OverlayWindow(settings, meter: true);
        _settingsWindow = new SettingsWindow(settings, characters);
        _layer = new AdjustLayer();
        _tray = new Tray();
        _hotkeys = new Hotkeys();
        _owned.AddRange([_tray, _hotkeys]);

        _settingsWindow.SettingsChanged += changed;
        _settingsWindow.EditModeRequested += () => SetEditMode(true);
        _settingsWindow.IsVisibleChanged += (_, _) => UpdatePreview();
        _settingsWindow.PickedChanged += ApplyPreviewTarget;
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
        _tray.OpenDiagnostics += _settingsWindow.ShowDiagnostics;
        _tray.PartToggled += (part, on) => { TrayToggles.Show(_settings, part, on); _settingsWindow.Reload(); changed(); };
        _settingsWindow.ProfileRequested += SwitchProfile;
        _settingsWindow.LayoutChanged += () =>
        {
            _overlay.Replace();
            foreach (var w in Others) w.Replace();
            StackParty();
        };
        _tray.CharacterPicked += (family, cls, sub) => { characters.Pick(family, cls, sub); _settingsWindow.Reload(); changed(); };
        _tray.DefaultPicked += () => { characters.PickDefault(); _settingsWindow.Reload(); changed(); };
        _tray.RaidPicked += id =>
        {
            (_settings.Raids.Auto, _settings.Raids.ManualGate) = (id is null, id ?? _settings.Raids.ManualGate);
            _settingsWindow.Reload();
            changed();
        };
        _tray.CountToggled += on =>
        {
            _settings.Raids.CountEnabled = on;
            _settingsWindow.Reload();
            changed();
        };
        _tray.Exit += Quit;
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
        _tray.SetTheme(_settings.Theme);
        _tray.SetProfile(_settings.ActiveProfile);
        _tray.SetRaid(_settings.Raids, _raidGates, _raidSeen);
        _tray.SetCharacters(_characters.ClassTree, _settings.ActiveCharacter, _settings.FavoriteClasses);
        _tray.SetParts(part => TrayToggles.IsShown(_settings, part));
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
        _changed();
        _tray.Notify($"오버레이 프로필 {profile + 1}");
    }

    string _failedHotkeys = "";   // told once, not on every change
    Func<Task<(Image?, PixelRect)>>? _takeShot;   // 게임 화면 찍기; none in the demo
    static readonly TimeSpan ShotSettle = TimeSpan.FromMilliseconds(150), ShotTimeout = TimeSpan.FromSeconds(5);

    /// <summary>종료 and an update's restart.</summary>
    void Quit() => Shutdown();

    void SetEditMode(bool on)
    {
        _overlay.SetEditMode(on);
        ApplyPreviewTarget();   // none while editing; back after
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
        if (_partyService is { } ps) _settingsWindow.SetPartyMembers(ps.Members, ps.IsHost);
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
        var o = _settings.Overlay;
        bool show = OverlayVisibility.Show(editMode: _overlay.EditMode, mode: o.Mode(), ownForeground: _ownForeground,
            gameForeground: _gameForeground, hudVisible: _hudVisible, hideWithoutHud: o.HideWithoutHud, firstRunDone: _settings.FirstRunDone);
        _overlay.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        foreach (var w in _peers) w.Visibility = _settings.Party.ShowMembers || _overlay.EditMode ? _overlay.Visibility : Visibility.Hidden;   // edit mode places them
        _buffs.Visibility = _settings.Buffs.Detached ? _overlay.Visibility : Visibility.Hidden;
        _debuffs.Visibility = _settings.Raids.DebuffShown() ? _overlay.Visibility : Visibility.Hidden;
        _raid.Visibility = _settings.Raids.CountShown() ? _overlay.Visibility : Visibility.Hidden;
        // The meter reads the 전분 panel, not the skill bar: a flash that hides the bar (HUD unseen) mustn't blink it out.
        bool meter = OverlayVisibility.Show(editMode: _overlay.EditMode, mode: o.Mode(), ownForeground: _ownForeground,
            gameForeground: _gameForeground, hudVisible: true, hideWithoutHud: false, firstRunDone: _settings.FirstRunDone);
        _meter.Visibility = _settings.Meter.On && _settings.Meter.Show && meter ? Visibility.Visible : Visibility.Hidden;
        _judgment.Visibility = _settings.Judgment.On(_settings.ActiveCharacter) ? _overlay.Visibility : Visibility.Hidden;
        if (show) { _overlay.BringToFront(); foreach (var w in Others) w.BringToFront(); }
        else if (_meter.Visibility == Visibility.Visible) _meter.BringToFront();
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

    static string LogPath()
    {
        var dir = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "GaugeOverlay.log");
        if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", overwrite: true);
        return path;
    }

    // ── --demo: the real UI driven by DemoSnapshots; no capture, no party, nothing saved ──────
    // Flags: --settings, --first-run, --debug, --edit, --pick=<Part> (a part's box options in 설정), --tab=<settings tab header>, --countdown, --cooldown=<CooldownStyle>, --fill=<FillFrom>,
    // --theme=<UiTheme>, --peer-colors=<ColorSource>, --support=<number 1–4>, --my-number=<1–4>, --name-length=<2–14>, --receive (파티원 크기·배치 받기),
    // --glow (계속 빛남 in chosen colours and 쿨 돈 뒤 지난 시간 on 일반스킬 and 초각스), --icons (아이콘으로 표시 on 일반스킬 and 초각스),
    // --saved-icons (the icons saved for the active 직업각인, from the real icons folder),
    // --saved-icons (the icons saved for the active 직업각인, from the real icons folder),
    // --unseen=<클래스> (설정 → 캐릭터 shows that class as newly added),
    // --cores (코어 빌드 applied to 워로드 · 고독한기사: 해 3, 버스트 캐넌 in 일반3),
    // --edit-as=<클래스 · 직업각인> (설정 → 편집 중 picks that 직업각인, so the overlay shows its sample; with --settings),
    // --duelist (the debuff window shows the 결투자 from each loop's start; implies --debuffs), --reduced (the debuff window drawn as under 애니메이션 끔),
    // --tray-menu (the tray menu opened), --join (파티 참가 dialog), --token (파티 호스트 token dialog).
}
