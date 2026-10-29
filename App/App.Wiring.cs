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

/// <summary>Start-up of the real app: settings, engine, party, windows, and every event between them (StartApp).</summary>
public partial class App
{
    void StartApp()
    {
        Trace.Listeners.Add(new TextWriterTraceListener(LogPath()));
        Trace.AutoFlush = true;

        var store = new SettingsStore(SettingsStore.DefaultPath);
        var settings = store.Load(out bool wasReset);
        var debounce = new SaveDebounce();
        var characters = new CharacterService(settings, CharacterService.DefaultProfilesDir);
        // Every shipped 직업각인 has settings of its own (a new one starts from its class); saved by the debounced writer.
        if (ClassProfileMigration.EnsureOwn(settings, characters.Classes)) debounce.Changed(Clock.Now());
        // 설정 → 캐릭터 marks what was added since: the first run of a version with the list records what ships now.
        // Saved by the debounced writer (retries a locked file); unsaved, the next start just records it again.
        if (settings.SeenSupport is null)
        {
            settings.SeenSupport = ClassSupport.Keys(characters.Support(slot => slot.ToString()));
            debounce.Changed(Clock.Now());
        }

        var engine = new Engine();
        var foreground = new ForegroundWatcher();
        var device = DeviceKeyFile.Load(DeviceKeyFile.DefaultPath);
        var party = new PartyService(device);
        _owned.AddRange([party, device, foreground, engine]);
        _gameForeground = foreground.IsGameForeground;
        engine.GameForeground = _gameForeground;   // calibration pictures and 스킬창 맞추기 only while the game is in front

        // 게임 화면 찍기 (설정's canvas): 설정 and the overlays (unless kept out of capture) step aside for the picture.
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
        _settingsWindow.TakeShot = _takeShot;   // after BuildUi: the window doesn't exist before it

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
            _tray.Notify($"클래스가 맞지 않아 기본으로 돌아갔습니다. 스킬창 다시 맞추기({SettingsWindow.KeyOrNone(_settings.Hotkeys.DetectCharacter)})나 트레이에서 클래스를 고르세요.");
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
                settings.Debuffs.Raid, settings.Buffs.Size, settings.ManualHud, settings.ManualScreen, settings.ActiveAdjusts(), wrongClass ? null : characters.CoreWatches,
                wrongClass || settings.ActiveCharacter is not { } active ? null : settings.StackSkillsOf(active),
                !wrongClass && settings.Judgment.On(settings.ActiveCharacter)
                    ? new JudgmentPlan(settings.Judgment.Source, settings.Judgment.FontSize, settings.Buffs.Size) : null));
            if (settings.SyncPlace()) _overlay.Replace();   // a 직업각인 has its own spot and size
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
            _judgment.Update(mine is not null ? new Player("", mine) : null);
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
        // Remembered for that 직업각인 (not the one active by the time this runs, if it changed).
        engine.StackSkillLearned += (character, slot) => Dispatcher.BeginInvoke(() =>
        {
            if (!settings.AddStackSkill(character, slot)) return;
            Configure();
            Save(now: true);
        });
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
            if (party.Token is { } t) TokenDialog.ShowToken(t, settings.Party.HostPort, settings.Theme);
            else _tray.Notify("파티를 열었습니다.");
        }
        async void JoinParty(string? token)
        {
            if (token is null) return;
            party.PublishLook(MyLook());
            if (await party.Join(token, PartyName())) _tray.Notify("파티에 참가했습니다.");
        }
        _tray.HostParty += HostParty;
        _tray.JoinParty += () => JoinParty(JoinDialog.Ask(settings.Theme));
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

        // 업데이트: checked once per start and installed straight away (user, 2026-10-29); 설정's button does the same by hand.
        string appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        UpdateInstaller.Cleanup(appDir, Updater.UpdateDir);   // what the last update left (the old exe and Profiles)
        var updater = new Updater();
        _owned.Add(updater);
        _settingsWindow.UpdateRequested += async release => await Install(release);
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

        // Download, verify, swap and restart into the new version. Nothing may escape it, or the app goes down mid-update.
        async Task Install(Release release)
        {
            try
            {
                _settingsWindow.SetUpdateBusy(true);
                if (await updater.InstallAsync(release, appDir) is { } why) { _settingsWindow.SetUpdateBusy(false); _tray.Notify(why); return; }
                (settings.Updates.WhatsNewTag, settings.Updates.WhatsNew) = (release.Tag, UpdateCheck.PlainNotes(release.Notes));   // shown once by the new version
                Save(now: true);
                Process.Start(new ProcessStartInfo(Path.Combine(appDir, UpdateInstaller.ExeName), "--updated") { UseShellExecute = false });
                Quit();
            }
            catch (Exception ex)
            {
                // Installed or not, this version keeps running (its exe was only renamed); a restart picks up whatever is in place.
                Trace.WriteLine($"[update] failed: {ex}");
                _settingsWindow.SetUpdateBusy(false);
                MessageBox.Show($"업데이트를 마치지 못했습니다. GaugeOverlay를 다시 실행해 주세요.\n\n{ex.Message}", "GaugeOverlay 업데이트",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // The first start after an update: 설정 opens again, as it was when the update was started from it (user, 2026-10-13),
        // with what changed over it, once. The notes are dropped if this isn't the version that was installed.
        void ShowWhatsNew()
        {
            var (tag, notes) = (settings.Updates.WhatsNewTag, settings.Updates.WhatsNew);
            if (tag.Length == 0) return;
            (settings.Updates.WhatsNewTag, settings.Updates.WhatsNew) = ("", "");
            Save(now: true);
            bool installed = Version.TryParse(tag.TrimStart('v'), out var version)
                && !UpdateCheck.IsNewer(version, Updater.Current) && !UpdateCheck.IsNewer(Updater.Current, version);
            Dispatcher.BeginInvoke(() =>
            {
                _settingsWindow.Show();
                _settingsWindow.Activate();
                if (installed)
                    MessageBox.Show(_settingsWindow, notes.Length > 0 ? $"{tag}(으)로 업데이트했습니다.\n\n{notes}" : $"{tag}(으)로 업데이트했습니다.",
                        "GaugeOverlay 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
            }, DispatcherPriority.ApplicationIdle);
        }

        async Task CheckForUpdate()
        {
            try
            {
                if ((await updater.CheckAsync()).Newer is not { } release) return;
                _settingsWindow.SetUpdate(release);
                _tray.Notify($"GaugeOverlay {release.Tag}(으)로 업데이트하는 중입니다. 끝나면 다시 켜집니다.");
                await Install(release);
            }
            catch (Exception ex) { Trace.WriteLine($"[update] check: {ex}"); }   // never shown: the app runs on as it is
        }
    }
}
