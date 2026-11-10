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
        CleanExtractFolders();

        var store = new SettingsStore(SettingsStore.DefaultPath);
        var settings = store.Load(out bool wasReset);
        var testStore = new SettingsStore(SettingsStore.TestPath);
        Settings? normalSettings = null;
        var debounce = new SaveDebounce();
        var characters = new CharacterService(settings, CharacterService.DefaultProfilesDir);
        var raidGates = RaidData.Load(CharacterService.DefaultProfilesDir);   // 설정 → 레이드's gates and their boss names, read once
        var bossNames = BossName.LoadTemplates(CharacterService.DefaultProfilesDir, raidGates);
        _raidGates = raidGates;
        // HP 변경 감지: a value raids.json now ships (or a newer one) replaces the learned one.
        if (HpChange.Prune(raidGates, settings.Raids)) debounce.Changed(Clock.Now());
        if (raidGates.Count > 0)   // no raids.json read: Found is empty, and every picture would go
            HpCrops.Clean(HpChange.Found(raidGates, settings.Raids).Select(f => (f.Gate.Id, f.Difficulty, f.To)));   // pictures of rows gone since
        // 대난투 체력: one switch for every raid with 대난투 (from 0.19); a file with the per-raid ones keeps its meaning.
        if (settings.Raids.MigrateShadowHp(raidGates)) debounce.Changed(Clock.Now());
        IReadOnlyList<FightHp> fightHp = [];   // the values this raid uses (Engine.FightHpChanged)
        // The gates with learned and this raid's HP values: what RaidData.ByHp and HiddenHp name a boss by.
        IReadOnlyList<RaidGate> HpGates() => HpChange.Effective(raidGates, settings.Raids, fightHp);
        // Every shipped 직업각인 has settings of its own (a new one starts from its class); saved by the debounced writer.
        if (ClassProfileMigration.EnsureOwn(settings, characters.Classes)) debounce.Changed(Clock.Now());
        // 설정 → 직업 인식 marks what was added since: the first run of a version with the list records what ships now.
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
        _partyService = party;
        _owned.AddRange([party, device, foreground, engine]);
        _gameForeground = foreground.IsGameForeground;
        engine.GameForeground = _gameForeground;   // calibration pictures and 스킬창 맞추기 only while the game is in front
        engine.InDungeonChanged += inside => Dispatcher.BeginInvoke(() => { _inDungeon = inside; UpdateVisibility(); });
        engine.ReadBehind = settings.Screens.ReadBehind;
        engine.ShownBehindChanged += shown => Dispatcher.BeginInvoke(() => { _gameBehind = shown; UpdateVisibility(); });
        engine.BehindChanged += status => Dispatcher.BeginInvoke(() => _settingsWindow.SetBehindStatus(status));
        engine.OtherAppForeground = foreground.IsOtherForeground;   // another app in front: nothing is read (outside 테스트 모드)
        foreground.OtherChanged += other => engine.OtherAppForeground = other;

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
        var meterHistory = new MeterHistory();
        var meterAlign = new MeterAlign();   // the party's figures on one 전분 second
        (MeterStatus? From, MeterView? View) meterView = (null, null);   // built once per engine meter step (1 Hz)
        var recorder = new FightRecorder();   // 전투 기록: the fight the meter follows
        (string? compareKey, MeterGhost? compareGhost) = (null, null);   // the meter's 비교 (CompareGhost)
        HiddenHpState? hidden = null;   // 종막 EX 2-3: its HP shows ???, estimated from the party's damage
        var hiddenBar = BarCountState.None;
        RaidStatus? hiddenStatus = null;   // the 줄 수 창 while it is estimated
        var records = new RecordStore(RecordStore.DefaultDir);
        var testRecords = new RecordStore(Path.Combine(RecordStore.DefaultDir, "test"));   // 테스트 모드's fights: apart, own count
        RecordStore StoreNow() => GameWindow.TestMode ? testRecords : records;
        bool overflowKept = settings.RecordOverflowKept;
        if (!overflowKept)   // before any save: the update from 0.19 bins nothing
            try
            {
                int kept = records.KeepOverflow(settings.RecordKeep) + testRecords.KeepOverflow(settings.RecordKeep);
                (settings.RecordOverflowKept, overflowKept) = (true, true);
                Save(now: false);
                Trace.WriteLine($"[records] {kept} fight(s) past 최근 {settings.RecordKeep} kept as 보관 after the update");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[records] update keep failed, tried next start: {ex.Message}"); }
        // Until that step has run, saves keep the most (50) so nothing it would have kept goes to the bin meanwhile.
        int KeepNow() => overflowKept ? settings.RecordKeep : RecordStore.MaxKeep;
        // 숙제 and who is being played (homework.plan.md): the game menu names the character; the game's fights count, 테스트 모드's don't.
        var homework = new HomeworkService(settings, raidGates, HomeworkData.Load(CharacterService.DefaultProfilesDir), [.. characters.Classes.Select(c => c.Name)],
            new HomeworkStore(HomeworkStore.DefaultPath), records, Dispatcher, () => Save(now: false), () => _settingsWindow.RefreshRecords());
        // 로아 API (lostark-api.plan.md): the 원정대 looked up on a 게임 메뉴 read (ApiPace) or on 새로고침; before the fights it stamps (Named).
        var api = new ApiService(settings, homework, Dispatcher, () => Save(now: false));
        Task lastSave = Task.CompletedTask;
        // A fight ended by a new 전분 record: its 클리어 / 실패 is settled by that record's boss once its bar is read (FightVerdict).
        // Only while that same record runs: a later record (left and came back) says nothing about this fight.
        (FightRecord Fight, Task<string?> Saving, RecordStore Store, int NextRecord)? toSettle = null;
        // --record-test (dev): with no 전분 reader yet, a stand-in clock (seconds since start) runs the recorder over what the
        // overlay reads while 테스트 모드 is on (footage), one record per test-mode run, saved when it is turned off. No figures,
        // so the DPS lines stay empty.
        bool recordTest = Environment.GetCommandLineArgs().Contains("--record-test");
        if (recordTest)
        {
            var standIn = FightClockState.None;
            var start = Clock.Now();
            new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) =>
            {
                var now = Clock.Now();
                if (!GameWindow.TestMode)
                {
                    if (recorder.Finish() is { } done)
                    {
                        Trace.WriteLine($"[record-test] saved: {done.Duration:0} s, {done.Events.Count} events");
                        SaveRecord(done);
                    }
                    (standIn, start) = (FightClockState.None, now);
                    return;
                }
                if (standIn.Timer is null) Trace.WriteLine("[record-test] 테스트 모드 on: recording");
                standIn = FightClock.Update(standIn, (int)(now - start).TotalSeconds, now);
                (recorder.Me, recorder.MyClass, recorder.MySubclass) = (PartyName(), characters.ClassId, characters.Active?.Subclass);
                recorder.InRaid = true;   // the stand-in clock records whatever is on screen
                if (recorder.Step(MeterStatus.None with { Clock = standIn }, [], [new(PartyName(), characters.ClassId, null, 1)], 0, "녹화 시험",
                    now, DateTime.Now) is { } fight) SaveRecord(fight);
            }, Dispatcher).Start();
        }
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
        bool firstRailRecord = settings.RailSeen is null;   // the window records it while it is built (RailNews)
        BuildUi(settings, characters, OnSettingsChanged);
        if (firstRailRecord && settings.RailSeen is not null) debounce.Changed(Clock.Now());   // saved, and flushed on exit
        _settingsWindow.DetectLive = engine.DetectLive;
        _settingsWindow.WatchFrames = engine.WatchFrames;
        _settingsWindow.TakeShot = _takeShot;   // after BuildUi: the window doesn't exist before it
        _settingsWindow.Records = records;
        _settingsWindow.TestRecords = testRecords;
        _settingsWindow.ImportedRecords = new RecordStore(Path.Combine(RecordStore.DefaultDir, "imported"));   // other users' fights: apart, never pruned
        // 휴지통 비우기 (user, 2026-11-10): every week at the chosen day and hour; a time passed while the app was off runs at start.
        void EmptyBins()
        {
            if (BinSchedule.Due(DateTime.Now, settings.BinEmptyDay, settings.BinEmptyHour, settings.BinEmptiedAt) is not { } at) return;
            bool done = true;
            foreach (var store in new[] { records, testRecords, _settingsWindow.ImportedRecords })
                try { Trace.WriteLine($"[records] bin emptied: {store.EmptyBin(at.ToUniversalTime())}"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { done = false; Trace.WriteLine($"[records] bin emptying failed: {ex.Message}"); }
            if (!done) return;   // a file held open: tried again next minute, not marked as run
            settings.BinEmptiedAt = at;
            Save(now: false);
            _settingsWindow.RefreshRecords();
        }
        EmptyBins();
        new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => EmptyBins(), Dispatcher).Start();
        _settingsWindow.Homework = homework;
        homework.Updated += _settingsWindow.HomeworkUpdated;
        _settingsWindow.ApiLookups = api;
        api.Updated += _settingsWindow.ApiUpdated;
        // 숙제's fallback: with the meter or 전투 기록 off, or the 전분 panel closed and no fight being recorded, the boss bar alone decides
        // (HomeworkWatch); a recorded fight never and it both: a record starting mid-fight switches the fallback off, which drops its watch.
        var fallback = new HomeworkFallback(homework, HpGates);
        engine.BossRead += fallback.Read;
        bool gameUp = false;   // a game closed on a low boss is not a kill: the desktop in front doesn't count as another app
        engine.GameRunningChanged += running => gameUp = running;
        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
            fallback.Step(!GameWindow.TestMode && Homework.SourceOf(settings.Meter.On, settings.Meter.Record, settings.Raids.BossHpRead, latest?.Meter?.PanelFound == true || recorder.Recording) == HomeworkSource.BossBar,
                !gameUp || !_hudVisible || foreground.IsOtherForeground, latest?.Daenantu == true, latest?.DaenantuKilled == true, RaidData.Find(raidGates, _raidSeen), Clock.Now()), Dispatcher).Start();
        _settingsWindow.RefreshRecords();
        _settingsWindow.GameArea = () => engine.GameAreaKey;
        _settingsWindow.PartyMove = (id, p) =>
        {
            bool done = party.SetParty(id, p);
            if (!done) _tray.Notify("그 파티는 이미 4명입니다.");
            return done;
        };

        // [Test]: while 테스트 모드 is on, the live settings are its own (settings.test.json) and saves go there; these are the
        // ones to go back to. settings.json is written on the way in, so a crash can't lose them.
        void EnterTestSettings()
        {
            Save(now: true);
            normalSettings = SettingsStore.Copy(settings);
            Settings? test = null;
            try { if (File.Exists(testStore.Path)) { test = testStore.Load(out bool broken); if (broken) test = null; } }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[app] test settings not read: {ex.Message}"); }
            settings.SwapIn(test ?? SettingsStore.Copy(settings));
            ClassProfileMigration.EnsureOwn(settings, characters.Classes);
            _settingsWindow.SettingsSwapped(test: true);
        }
        void LeaveTestSettings()
        {
            if (normalSettings is not { } normal) return;
            Save(now: true);
            settings.SwapIn(normal);
            normalSettings = null;
            Save(now: true);   // with what the app learned meanwhile
            _settingsWindow.SettingsSwapped(test: false);
        }

        // A failed write (file locked, disk full) is logged and left pending, so the timer retries it.
        void Write()
        {
            try
            {
                if (normalSettings is null) store.Save(settings);
                else
                {
                    testStore.Save(settings);
                    // What the app learned meanwhile also reaches settings.json, so a crash in 테스트 모드 can't lose it.
                    var merged = SettingsStore.Copy(settings);
                    merged.SwapIn(SettingsStore.Copy(normalSettings));
                    store.Save(merged);
                }
            }
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
        _flush = () =>
        {
            if (normalSettings is { } normal) { Save(now: true); settings.SwapIn(normal); normalSettings = null; }   // leaving in 테스트 모드
            Save(now: true);
            try { lastSave.Wait(SaveOnExit); }   // a fight saved just before closing finishes writing
            catch (AggregateException ex) { Trace.WriteLine($"[records] save before exit failed: {ex.InnerException?.Message}"); }
            if (recorder.Finish() is { } fight && StoreNow() is var store) store.Save(Named(TakeSkills(fight), store == testRecords), KeepNow());   // the fight under way when the app closes
        };
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
                    ? $"스킬창 가운데가 {Josa.By(shown)} 보입니다. 고른 클래스({characters.ActiveClass?.Name})를 확인하세요. 게이지 예측은 멈췄습니다."
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
            if (!settings.Overlay.SkillRead) return new HashSet<SlotId>();   // nothing is read to copy them from
            var look = ClassProfiles.Mine(settings);
            return SkillIcon.Needed(look.Effects?.Icons ?? new HashSet<SlotGroup>(), look.Groups, look.Hidden, characters.AdenSlots,
                settings.Meter.On && settings.Meter.Record ? ClassProfiles.Reads(settings, characters.AdenSlots).Skip : null);   // 전투 기록 draws them
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
            engine.Configure(new Engine.Config(new Dictionary<string, Fit>(settings.Fits), characters.AdenSlots, _settingsWindow.DiagnosticsShown,
                ClassProfiles.Reads(settings, characters.AdenSlots), characters.Gauge, characters.ActiveClass?.AdenRects, characters.AdenDark,
                characters.GaugeRule, wrongClass ? GaugeReadMode.Agreed : settings.Gauge.ReadMode, wrongClass ? null : settings.ActiveCharacter,
                settings.Buffs.Size, settings.ManualHud, settings.ManualScreen, settings.ActiveAdjusts(), wrongClass ? null : characters.CoreWatches,
                wrongClass || settings.ActiveCharacter is not { } active ? null : settings.StackSkillsOf(active),
                !wrongClass && settings.Judgment.On(settings.ActiveCharacter)
                    ? new JudgmentPlan(settings.Judgment.Source, settings.Judgment.FontSize, settings.Buffs.Size) : null,
                settings.Buffs.Count, settings.Debuffs.BossIconSize, settings.Raids.Clone(), raidGates, bossNames,
                settings.Meter.On, new Dictionary<string, PixelRect>(settings.Meter.Panels), settings.Amsu.On, settings.Amsu.Rate, settings.ReadsPerSecond, settings.Debuffs.Raid, settings.Buffs.AnyInRaidOnly, settings.Debuffs.BossCount,
                HomeworkRead: !(settings.Meter.On && settings.Meter.Record)));   // 숙제 without a recorded fight reads the bar itself
            engine.ReadBehind = settings.Screens.ReadBehind;
            if (settings.SyncPlace()) { _overlay.Replace(); ApplyDisplay(); }   // a 직업각인 has its own spot and size
            ApplyUi();
            Draw();
        }

        // Draws yourself and the party; publishing again is a no-op unless something changed.
        void Draw()
        {
            // The engine doesn't know the class: yours is added here, for the party and for your own gauge's glow rule.
            var mine = latest is { } s ? s with { ClassId = characters.ClassId } : null;
            homework.Playing(characters.Active?.Class, characters.Active?.Subclass);   // returns at once unless they changed
            PlayingClass(GameWindow.TestMode ? null : characters.Active?.Class);   // for the skill map, read off the UI thread
            _meterReadAt = WindowLinger.LastRead(mine?.Meter);
            _presence = mine?.Presence ?? RaidPresence.None;
            if (mine is not null) _settingsWindow.SetDetect(mine);   // 감지 중 on the map, only redrawn while it shows
            _countReadAt = WindowLinger.Seen(_countReadAt, mine?.Raid?.Count is not null, Clock.Now());
            CheckInUse();
            if (mine is not null) party.Publish(mine, PartyName(), ClassProfiles.SentGroups(settings), settings.Party.SendGauge);
            if (mine?.Meter is { } seen) meterHistory.Casts.Observe(mine.Slots, seen.Clock, Clock.Now());   // every frame: a cast is an edge
            if (mine?.Meter is { } status && !ReferenceEquals(status, meterView.From)) meterView = (status, StepMeter(status));
            else if (mine?.Meter is null && meterView.From is not null) (meterHistory, meterAlign, meterView) = (new(), new(), (null, null));   // off: graphs start over
            if (mine is not null && mine.Meter is not null) mine = mine with { MeterView = meterView.View };
            if (mine?.Meter is not null || recordTest && mine is not null)
            {
                var now = Clock.Now();
                recorder.ObserveMine(mine, now);
            }
            else if (recorder.Finish() is { } fight)   // the meter went off
            {
                Trace.WriteLine($"[records] fight ended ({fight.Gate ?? "?"}, {fight.Duration:0}s): meter off");
                SaveRecord(fight);
            }
            _overlay.Update(mine is not null ? new Player("", mine) : null);
            _buffs.Update(mine is not null ? new Player("", mine) : null);
            _debuffs.Update(mine is not null ? new Player("", mine) : null);
            _judgment.Update(mine is not null ? new Player("", mine) : null);
            _amsu.Update(mine is not null ? new Player("", mine) : null);
            _raid.Update(mine is not null ? new Player("", hiddenStatus is not null && mine.Meter is not null ? mine with { Raid = hiddenStatus } : mine) : null);
            _meter.Update(mine is not null ? new Player("", mine) : null);
            bool overGame = _overlay.Visibility == Visibility.Visible || _meter.Visibility == Visibility.Visible;
            _shadow.Update(mine?.Shadow is { } shadow ? (shadow.Text, shadow.Bar) : null, overGame);
            _shadowDealt.Update(mine?.Shadow is { } gimmick ? (EasyRead.Of(gimmick.Dealt), gimmick.DealtLabel) : null, overGame);
            _bossHp.Update(mine?.BossHp is { } boss ? (boss.Text, boss.Label) : null, overGame);
            ShowPeers(peers);
        }

        // The meter's second: my figures to the party (내 피해량 보내기), then the view from mine and everyone's.
        MeterView StepMeter(MeterStatus status)
        {
            var role = characters.Role == Role.서포터 ? MeterRole.서포터 : MeterRole.딜러;
            if (settings.Meter.Send && MeterTracker.Mine(status, role) is { } own) party.SendMeter(own with { ClassId = characters.ClassId, AtkBuffed = meterHistory.MyAtkBuffed });   // my own 공증, for 버프 분배
            var (samples, me, host) = party.Meter();
            if (!settings.Meter.TakesParty) samples = [];   // not sharing, or not receiving: nobody else's figures, in the meter or the record
            _meter.Renderer.MeterMe = me;
            int myParty = party.Members.FirstOrDefault(m => m.Id == me)?.Party ?? 1;
            // A 4-man raid (the gate by its HP, else the one recognised; any guardian raid): the buffs' party share too.
            bool small = (RaidData.ByHp(HpGates(), status.Boss.Max)?.Gate ?? RaidData.Find(raidGates, _raidSeen))?.SmallParty == true
                || latest?.Dungeon?.Kind == DungeonKind.Guardian;
            var view = MeterView.Build(status, role, PartyName(), characters.ClassId, samples, me, host, settings.Meter, meterHistory, Clock.Now(), myParty, meterAlign, latest?.Buffs, small)
                with { BossName = HeaderBoss(status.Boss.Max), Skill = MeterSkill.Line(settings, ClassProfiles.ActiveKey(settings), meterHistory, status.Clock.Timer, s => KeyNames.OfSlot(settings, s)),
                    Ghost = CompareGhost(status.Boss.Max) };
            RecordStep(status, role, samples, me, myParty, view.BossTaken);
            StepHidden(status, Everyone(status, role, samples, me, myParty), myParty);
            return view;
        }

        // Everyone's figures this second: the party's, and mine from this PC under my party name and party.
        List<MeterSample> Everyone(MeterStatus status, MeterRole role, IReadOnlyList<MeterSample> samples, ulong me, int myParty)
        {
            string myName = party.Members.FirstOrDefault(m => m.Id == me)?.Name ?? PartyName();
            var all = samples.Where(s => s.Id != me).ToList();
            if (MeterTracker.Mine(status, role) is { } own) all.Add(own with { Id = me, Name = myName, ClassId = characters.ClassId, Party = myParty });
            return all;
        }

        // 종막 EX 2-3's estimated HP (HiddenHp) into the 줄 수 창, while its gate's 줄 수 창 is on: "≈62% 1.2조", glowing at its % alerts.
        void StepHidden(MeterStatus status, List<MeterSample> all, int myParty)
        {
            var r = settings.Raids;
            var gates = HpGates();
            foreach (var (gateId, p, assist) in HiddenHp.Calibration(gates, status, all))   // 2-1 cleared: its 조력 피해량 per party
            {
                var target = r.Gate(gateId);
                if (Math.Abs((p == 1 ? target.Assist1 : target.Assist2) - assist) < 0.05) continue;
                if (p == 1) target.Assist1 = assist; else target.Assist2 = assist;
                Trace.WriteLine($"[raid] {gateId}: 2-1 조력 피해량 {p}파티 {assist:0.#}억");
                Save(now: false);
                _settingsWindow.RefreshRaid();
            }
            // The ??? gate this fight may reach (its row's figures): the one under way, else any (only EX 2-3 is one).
            var gate23 = hidden?.Gate ?? gates.FirstOrDefault(g => g.HiddenHp.Count > 0);
            var row = gate23 is null ? null : r.Gates.GetValueOrDefault(gate23.Id);
            // The 조력 비율: what 2-1 shows (both parties' figures and its 에스더 plan) at this difficulty (2-2's HP gives it), else the user's.
            double share = gate23 is null ? r.AssistShare
                : HiddenHp.Share(gates, gate23, hidden?.Difficulty ?? RaidData.ByHp(gates, status.Boss.Max)?.Difficulty, r);
            var options = new HiddenHpOptions(share, r.OtherParty, row?.Esthers ?? [], row?.Assist1 ?? 0, row?.Assist2 ?? 0);
            var now = Clock.Now();
            var was = hidden;
            hidden = HiddenHp.Step(hidden, gates, status,
                HiddenHp.RaidDamage(all, myParty, share, r.OtherParty, options.Assist1, options.Assist2), options, now);
            if (was is null != hidden is null) Trace.WriteLine(hidden is { } on ? $"[raid] {on.Gate.Id} ({on.Difficulty}): HP estimated" : "[raid] HP estimate ended");
            if (hidden is not { } est || !r.CountOn(est.Gate.Id))
            {
                (hiddenStatus, hiddenBar) = (null, BarCountState.None);
                return;
            }
            hiddenBar = BarAlert.Update(hiddenBar, (int)Math.Ceiling(est.Percent), now, BarAlert.Thresholds(r.AlertsOf(est.Gate), r.OffsetOf(est.Gate.Id)));
            bool glow = r.Glow && BarAlert.Glowing(hiddenBar, r.GlowUntil, r.GlowSeconds, r.GlowBars, now);
            string left = est.Left >= 1_000_000_000_000 ? $"{est.Left / 1e12:0.00}조" : $"{est.Left / 1e8:0}억";
            string gimmick = est.Phase is GimmickPhase.Waiting or GimmickPhase.Paused ? " · 25% 대기" : "";   // at 25%: waiting for the 10% jump
            hiddenStatus = new RaidStatus(est.Gate, null, glow, null, est.Base is null ? "≈?" : $"≈{est.Percent:0}% {left}{gimmick}");
        }

        // 전투 기록's second: everyone's figures (mine from this PC, as the meter shows them) and who is in the fight.
        void RecordStep(MeterStatus status, MeterRole role, IReadOnlyList<MeterSample> samples, ulong me, int myParty, long bossTaken)
        {
            if (!settings.Meter.Record)   // 전투 기록 off: a fight under way is dropped, not saved
            {
                if (recorder.Finish() is { } dropped) Trace.WriteLine($"[records] fight dropped ({dropped.Gate ?? "?"}, {dropped.Duration:0}s): 전투 기록 off");
                return;
            }
            // Any dungeon but a guardian raid (user, 2026-11-03): its panel read, or unread with a boss bar (saved as 알 수 없음,
            // named in 편집). The 전분 timer alone (a dummy, the field) starts nothing.
            bool inRaid = latest?.Dungeon is { } dungeon ? dungeon.Kind != DungeonKind.Guardian : status.Boss.Max is not null;
            if (inRaid != recorder.InRaid)
                Trace.WriteLine($"[records] in raid: {inRaid} (dungeon {latest?.Dungeon?.Kind.ToString() ?? "unread"}, boss bar {(status.Boss.Max is null ? "unread" : "read")})");
            recorder.InRaid = inRaid;
            recorder.ShadowOn = latest?.Daenantu == true;
            recorder.AmsuOn = settings.Amsu.On;   // the bombs are recorded (and their gain shown) only while read
            recorder.PauseLimit = TimeSpan.FromSeconds(Math.Clamp(settings.RecordPauseSeconds, Settings.MinRecordPause, Settings.MaxRecordPause));
            // My name as the party knows it (a taken name gets a number there), so no member's lane is mine.
            string myName = party.Members.FirstOrDefault(m => m.Id == me)?.Name ?? PartyName();
            (recorder.Me, recorder.MyClass, recorder.MySubclass) = (myName, characters.ClassId, characters.Active?.Subclass);
            var all = Everyone(status, role, samples, me, myParty);
            List<RecordMember> members =
            [
                new(myName, characters.ClassId, null, myParty),
                .. party.Members.Where(m => m.Id != me).Select(m => new RecordMember(m.Name, peers.FirstOrDefault(p => p.Name == m.Name)?.Snapshot.ClassId ?? 0, null, m.Party)),
            ];
            string? gate = RaidData.Find(raidGates, _raidSeen) is { } g ? RaidData.Label(g) : null;
            bool wasRecording = recorder.Recording;
            var ended = recorder.Step(status, all, members, bossTaken, gate, Clock.Now(), DateTime.Now);
            if (!wasRecording && recorder.Recording) Trace.WriteLine($"[records] fight started ({gate ?? "gate unread"}, 전분 {status.Clock.Fight})");
            if (ended is { } fight)
            {
                Trace.WriteLine($"[records] fight ended ({fight.Gate ?? "?"}, {fight.Duration:0}s, {(fight.Cleared switch { true => "클리어", false => "실패", _ => "결과 모름" })}, "
                    + $"{fight.Events.Count} events, {fight.Samples.Count} samples){(recorder.Restarted ? ": new 전분 record" : "")}");
                var saving = SaveRecord(fight);
                toSettle = recorder.Restarted ? (fight, saving, StoreNow(), status.Clock.Fight) : null;
            }
            else Settle(status);
        }

        // Written once, off the UI thread, when a fight ends (spec § D Cost budget).
        // The class played, for the file name and the list; a 테스트 모드 fight is [Test] until 편집 gives it one (user, 2026-11-05).
        // With the character's 전투력 and item level from the API (lostark-api.plan.md phase 5); a [Test] fight's class isn't played.
        FightRecord Named(FightRecord fight, bool test)
        {
            var named = homework.Name(fight with { MyClassName = test ? RecordCharacter.TestClass : characters.Active?.Class });
            return test ? named : RecordPower.Stamp(named, settings.CombatPower, api.Levels);
        }

        // The dungeon panel last read gives the difficulty (and the name, when no known gate was seen); guardian raids are daily
        // content and aren't kept.
        // The meter header's boss and difficulty: by its max HP (raids.json, then the bosses learned from fights), else the
        // recognised gate with the dungeon panel's difficulty.
        string? HeaderBoss(long? max)
        {
            static string With(string name, string? level) => string.IsNullOrEmpty(level) ? name : $"{name} [{level}]";
            if (RaidData.ByHp(HpGates(), max) is var (g, level)) return With(g.Bosses.FirstOrDefault() ?? RaidData.Label(g), level);
            if (max is { } hp && settings.BossBook.TryGetValue(hp, out var learned)) return With(learned.Name, learned.Difficulty);
            return RaidData.Find(raidGates, _raidSeen) is { } seen ? With(seen.Bosses.FirstOrDefault() ?? RaidData.Label(seen), latest?.Dungeon?.Difficulty) : null;
        }

        // The live fight's gate and difficulty as SaveRecord will name it: by its max HP, else the boss book and the dungeon panel.
        (string? Gate, string? Difficulty) LiveRaid(long? max) =>
            RaidData.ByHp(HpGates(), max) is var (g, level) ? (RaidData.Label(g), level)
            : BossBook.Resolve(settings.BossBook, max, latest?.Dungeon, RaidData.Find(raidGates, _raidSeen) is { } seen ? RaidData.Label(seen) : null);

        // 비교 (lostark-api.plan.md phase 6): the saved fight of this raid and character, read off the UI thread when the
        // character, the raid, the pick or the record folder change (a deleted or edited fight drops at once); not per frame.
        MeterGhost? CompareGhost(long? max)
        {
            var (gate, difficulty) = LiveRaid(max);
            string? who = homework.Current;
            string? chosen = who is not null && gate is not null ? settings.MeterCompare.GetValueOrDefault(MeterCompare.Key(who, gate, difficulty)) : null;
            var store = StoreNow();
            string key = $"{who}|{gate}|{difficulty}|{chosen}|{store == testRecords}|{store.Changed}";
            if (key == compareKey) return compareGhost;
            (compareKey, compareGhost) = (key, null);
            _ = Task.Run(() =>
            {
                try
                {
                    var ghost = MeterCompare.Pick(store.List(), who, gate, difficulty, chosen) is { } pick ? new MeterGhost(MeterCompare.Ghost(pick.Record), pick.How) : null;
                    Dispatcher.BeginInvoke(() => { if (compareKey == key) compareGhost = ghost; });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.WriteLine($"[meter] 비교 not read: {ex.Message}");
                    Dispatcher.BeginInvoke(() => { if (compareKey == key) compareKey = null; });   // the next step tries again
                }
            });
            return null;
        }

        // Returns the saved file's name once written (null: not saved).
        Task<string?> SaveRecord(FightRecord fight)
        {
            var dungeon = latest?.Dungeon;
            if (dungeon?.Kind == DungeonKind.Guardian) { Trace.WriteLine("[records] guardian raid: not saved"); return Task.FromResult<string?>(null); }
            long? hp = BossBook.FightHp(fight.Boss);   // one per gate and difficulty: names the fight once learned
            if (RaidData.ByHp(HpGates(), hp) is var (shipped, level))   // a gate raids.json knows by HP: its own name
                return Store(fight with { Gate = RaidData.Label(shipped), Difficulty = level });
            // A video's fight names nothing for the game: boss names are learned only from real fights.
            if (!GameWindow.TestMode && BossBook.ToLearn(settings.BossBook, hp, dungeon) is { } learned)
            {
                settings.BossBook[hp!.Value] = learned;
                Trace.WriteLine($"[records] boss HP {hp} learned as {learned.Name} [{learned.Difficulty}]");
                Save(now: false);
            }
            var (name, difficulty) = BossBook.Resolve(settings.BossBook, hp, dungeon, fight.Gate);
            return Store(fight with { Gate = name, Difficulty = difficulty });
        }

        Task<string?> Store(FightRecord fight)
        {
            var store = StoreNow();   // read now: test mode may end before the save runs
            fight = TakeSkills(fight);   // the slots' skills, read during the fight
            var saving = Task.Run(() =>
            {
                string? file = null;
                try
                {
                    var named = Named(fight, store == testRecords);
                    // The next part of the last saved fight's gate (2-2 after 2-1): that file becomes the whole gate, so 2-1 is
                    // never a record of its own (user, 2026-11-10).
                    // Its own character's last one: another character's fight in between doesn't break the gate.
                    var last = store.List().FirstOrDefault(x => RecordStore.IsHeld(x.File) != settings.RecordAutoSave
                        && x.Record is { } r && RecordCharacter.Of(r) == RecordCharacter.Of(named));
                    var merged = last.Record is { } before ? FightRegions.Continued(before, named) : null;
                    if (merged is not null) Trace.WriteLine($"[records] {last.File} goes on: saved as {merged.Gate}");
                    if (settings.RecordAutoSave) file = merged is not null ? EditOrSave(store, last.File, merged, named) : store.Save(named, KeepNow());
                    else { store.Hold(merged ?? named); file = RecordStore.Held; }   // 자동 저장 off: only the last fight, until 보관 or the next one
                    if (store == records) homework.Saved(named);   // a clear counts even when the fight isn't kept
                    Trace.WriteLine($"[records] saved {file}");
                    _settingsWindow.RefreshRecords();
                    return file;
                }
                // Any error: the task's result is all that is kept, so one not caught here would vanish with it.
                catch (Exception ex)
                {
                    Trace.WriteLine(file is null ? $"[records] save failed ({fight.Gate ?? "?"}): {ex.GetType().Name}: {ex.Message}"
                        : $"[records] saved {file}, then failed: {ex.GetType().Name}: {ex.Message}");
                    return file;
                }
            });
            lastSave = saving;
            return saving;
        }

        // The merge's 2-1 file gone meanwhile (deleted or renamed in 전투 기록 as the fight ended): this part saved on its own
        // instead, never lost (csharp-reviewer, 2026-11-10).
        string EditOrSave(RecordStore store, string file, FightRecord merged, FightRecord named)
        {
            try { return store.Edit(file, merged, KeepNow()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.WriteLine($"[records] {file} not editable before the merge ({ex.GetType().Name}): saved apart");
                return store.Save(named, KeepNow());
            }
        }

        // By the new boss's max HP only: the recognised gate name still holds the last boss until the new name is read.
        void Settle(MeterStatus status)
        {
            if (toSettle is not { } s) return;
            if (status.Clock.Fight != s.NextRecord) { toSettle = null; return; }
            // Not until the new boss has taken damage: the last boss's bar may still be read while the gate changes.
            if (status.Boss.Max is not { } max || BossDamage.Taken(status.Boss) <= 0 || !s.Saving.IsCompleted) return;
            toSettle = null;
            if (s.Saving.Result is not { } file || FightVerdict.ByNext(s.Fight, max, null, HpGates()) is not { } cleared) return;
            if (cleared == s.Fight.Cleared) return;
            Trace.WriteLine($"[records] {file}: {(cleared ? "클리어" : "실패")} by the next record's boss");
            _ = Task.Run(() =>
            {
                try
                {
                    if (s.Store.SetCleared(file, cleared) is { } settled && s.Store == records)
                    {
                        if (cleared) homework.Saved(settled);
                        else homework.Failed(settled);   // saved as 클리어 (the bar gone), then the same boss again
                    }
                    _settingsWindow.RefreshRecords();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[records] result not written: {ex.Message}"); }
            });
        }

        void OnSettingsChanged() { Configure(); Save(now: false); }

        // Engine → UI (engine thread → dispatcher).
        engine.SnapshotReady += s =>
        {
            latest = s;
            if (Interlocked.Exchange(ref pending, 1) == 0)
                Dispatcher.BeginInvoke(() => { Interlocked.Exchange(ref pending, 0); Perf.Run("앱 처리 (미터 · 기록 · 파티 보내기)", Draw); });
        };
        engine.GameRunningChanged += running => Dispatcher.BeginInvoke(() => { _tray.SetGameRunning(running); _settingsWindow.SetGameRunning(running); if (running) ApplyDisplay(); });
        engine.RaidRecognised += id => Dispatcher.BeginInvoke(() =>
        {
            _raidSeen = id;
            _tray.SetRaid(settings.Raids, raidGates, id);
            _settingsWindow.SetRaidSeen(id, latest?.Dungeon?.Difficulty);
        });
        // HP 변경 감지: this raid's values; confirmed ones are kept (a video's fight changes nothing for the game).
        engine.FightHpChanged += list => Dispatcher.BeginInvoke(() =>
        {
            var before = fightHp;   // what this raid had: a value new to the list is a raid that read it (HP 변경 감지's count)
            fightHp = list;
            if (!GameWindow.TestMode && HpChange.Keep(raidGates, settings.Raids, list, DateTime.UtcNow, before))
            {
                Trace.WriteLine("[raid] HP change kept");
                Configure();   // the engine's copy of the 레이드 settings, for after this raid
                Save(now: false);
            }
            _settingsWindow.SetFightHp(list);
        });
        // Dev: 클래스 인식 자동 on a replay's video too (class-detect.plan.md).
        bool classDev = Environment.GetEnvironmentVariable("GAUGE_CLASS_DETECT") == "1";
        engine.HudVisibleChanged += visible => Dispatcher.BeginInvoke(() =>
        {
            _hudVisible = visible;
            UpdateVisibility();
            // 클래스 인식 자동: the bar back outside a dungeon, the next ready 초각스 tells the class.
            if (visible && ClassDetect.OnBarBack(settings.ClassDetect, _inDungeon, GameWindow.TestMode, classDev) && characters.WatchClass()) engine.RequestSubclass();
        });
        engine.FitNeeded += vp => Dispatcher.BeginInvoke(() =>
        {
            if (settings.FirstRunDone) _tray.Notify($"게임 화면 크기({vp.Width}×{vp.Height})에 맞춘 스킬창이 없습니다. 모든 스킬이 준비된 상태에서 설정 → 스킬창 → 스킬창 맞추기를 눌러 주세요.");
        });
        engine.FitFinished += (vp, fit, message, detected, test, canRevert) => Dispatcher.BeginInvoke(() =>
        {
            // 테스트 모드 (as the engine ran the fit, not as the flag is now: it may have been switched meanwhile): a fit of the
            // video in front is the engine's alone (memory); the game's saved fits stay as they are.
            if (detected is not null && !test) settings.DetectedScreen = detected;   // 직접 입력 starts from it
            if (fit is not null && !test) { settings.Fits[Geometry.ViewportKey(vp)] = fit; Configure(); Save(now: true); }
            // A 테스트 모드 fit request says what its fine-tune did ("자동 맞춤 82.5% · +3, −1 px"), and 되돌리기 what it put back.
            var text = (test ? "테스트 모드: " : "") + (test && fit is not null ? message : FitText(vp, fit, message));
            _settingsWindow.SetFitResult(text, canRevert);
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
        // 전분 위치 찾기: a spot the engine found is kept for that game-area size (설정 → DPS 미터기 shows it).
        engine.MeterPanelFound += (key, spot) => Dispatcher.BeginInvoke(() =>
        {
            if (settings.Meter.Panels.TryGetValue(key, out var was) && Math.Abs(was.X - spot.X) <= 2 && Math.Abs(was.Y - spot.Y) <= 2 && was.Width == spot.Width) return;   // the same spot, a pixel off
            settings.Meter.Panels[key] = spot;
            Configure();
            Save(now: true);
            _settingsWindow.Reload();
        });
        // A character picked by hand (tray, 설정 or first run, all through CharacterService.Pick): its emblem is learned.
        characters.Picked += engine.RequestEmblem;
        _settingsWindow.GainsResetRequested += () => { engine.ResetGains(); _tray.Notify($"{settings.ActiveCharacter}의 배운 게이지 값을 지웠습니다."); };
        // 스킬창 다시 맞추기 by hotkey (the tray and 설정 below): the same Refit.
        _hotkeys.Pressed += action => { if (action == nameof(HotkeySettings.DetectCharacter)) Dispatcher.BeginInvoke(() => Refit()); };
        engine.SubclassSlotCaptured += (slot, greyedSkills, ordinary) => Dispatcher.BeginInvoke(async () =>
        {
            if (characters.ClassSearchDue(GameWindow.TestMode && !classDev))
            {
                string? last = characters.ActiveClass?.Name;
                bool greyed = greyedSkills is not null;
                try
                {
                    // Off the UI thread: a full search and the 일반 slots' vote take a moment.
                    var (found, slots) = await Task.Run(() => Perf.RunCpu("클래스 인식", () =>
                    {
                        var f = SkillClassFinder.Find(IconPrint.Framings(slot), SkillLibrary.Shipped, last, greyed);
                        bool ask = !greyed && f.Class is not null && f.Class != last;   // the usual case, the same class, votes on nothing
                        return (f, ask ? SkillClassFinder.Confirm(ordinary.Select(o => IconPrint.Framings(o)), SkillLibrary.Shipped, f.Class!) : SkillClassFinder.Verdict.Unsure);
                    }));
                    Trace.WriteLine($"[class] {found.Class ?? "?"}/{found.Subclass ?? "-"} {(found.Fast ? "fast" : "full")} {found.Score:0.000} lead {found.Lead:0.000} slots {slots}{(greyed ? " greyed" : "")} (was {last ?? "기본"})");
                    if (characters.OnClassFound(found, slots, greyed) is { } changed)
                    {
                        _settingsWindow.Reload();
                        _tray.Notify(changed);
                    }
                    if (characters.Watching) _ = Task.Delay(ClassDetect.RetryAfter).ContinueWith(_ => Dispatcher.BeginInvoke(() => { if (characters.Watching) engine.RequestSubclass(); }));
                }
                // The search is optional: the engine has already spent this capture, so a failure here must not lose the 직업각인 request below.
                catch (Exception ex) when (ex is not OutOfMemoryException) { Trace.WriteLine($"[class] search failed: {ex}"); }
            }
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
                    if (debugRows.TryRemove(slot, out var r)) _settingsWindow.UpdateDiagnostics(r.Slot, r.Crop, r.Reading, r.State);
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
                    if (extraRows.TryRemove(key, out var r)) _settingsWindow.UpdateDiagnostics(r.Key, r.Crop, r.Reading, r.State);
            });
        };

        // Platform → engine / UI.
        foreground.Changed += fg => { engine.GameForeground = fg; Dispatcher.BeginInvoke(() => { _gameForeground = fg; UpdateVisibility(); if (fg && settings.Screens.Display == DisplayMap.GameDisplay) ApplyDisplay(); }); };
        foreground.OwnChanged += own => Dispatcher.BeginInvoke(() => { _ownForeground = own; UpdateVisibility(); });
        // 설정 in front shows the overlays like the game (켜기); its handle exists once it is first shown.
        void Own() { foreground.OwnWindow = new System.Windows.Interop.WindowInteropHelper(_settingsWindow).Handle; foreground.Recheck(); }
        _settingsWindow.SourceInitialized += (_, _) => Own();
        Own();   // already shown (0 until then)
        // 테스트 모드 (tray or hotkey): a window (a video of the game) is watched instead of LOSTARK: the one picked from the tray's
        // list, or (picked zero: the hotkey, 맨 앞 창 따라가기) whichever was last in front.
        void SetTest(bool on, IntPtr picked, string? notice = null)
        {
            if (on && !GameWindow.TestMode) EnterTestSettings();
            else if (!on && GameWindow.TestMode) LeaveTestSettings();
            GameWindow.Picked = on ? picked : 0;
            GameWindow.TestMode = on;
            if (on && picked != 0 && settings.Screens.ReadBehind) GameWindow.Restore(picked);   // read as it is: the user keeps working
            else if (on && picked != 0) GameWindow.Show(picked);   // the overlay and the fit need it in front
            foreground.Recheck();
            ApplyDisplay();   // after the mode and pick: "the game's display" is the picked window's now
            if (on) engine.RequestDetect();   // the video's character
            _tray.SetTestMode(on, GameWindow.Picked);
            _tray.Notify(notice ?? (!on ? "테스트 모드를 껐습니다. 로스트아크 창을 찾습니다."
                : picked != 0 ? "테스트 모드: 고른 창을 게임으로 보고, 그 화면에서 스킬창을 찾습니다. 저장된 맞춤은 쓰지도 바꾸지도 않습니다."
                : "테스트 모드: 맨 앞에 있던 창(영상 플레이어 등)을 게임으로 보고, 그 화면에서 스킬창을 찾습니다. 저장된 맞춤은 쓰지도 바꾸지도 않습니다. 다른 창을 보려면 그 창을 클릭하거나 트레이 → 테스트 모드에서 고르세요."));
        }
        void ToggleTestMode()
        {
            // 창 밖에서도 읽기: the window in front now is pinned, so working in another app doesn't change what is read.
            if (settings.Screens.ReadBehind) SetTest(!GameWindow.TestMode, GameWindow.TestMode ? 0 : GameWindow.Front);
            else SetTest(!(GameWindow.TestMode && GameWindow.Picked == 0), 0);
        }
        _tray.ToggleTestMode += ToggleTestMode;
        _tray.TestWindowPicked += hwnd =>
        {
            if (GameWindow.Exists(hwnd)) SetTest(true, hwnd);
            else _tray.Notify("그 창은 이미 닫혔습니다. 트레이 → 테스트 모드를 다시 열어 고르세요.");
        };
        _tray.TestOff += () => SetTest(false, 0);
        _tray.ReinforcedToggled += on => { GameWindow.Reinforced = on; _tray.SetReinforced(on); };
        if (Environment.GetEnvironmentVariable("GAUGE_TEST_PROCESS") is { Length: > 0 } testProcess)   // dev: 테스트 모드 follows that process, no keys (replay.ps1 -Background)
        {
            var watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            watch.Tick += (_, _) =>
            {
                var hwnd = GameWindow.MainWindowOf(testProcess);
                if (hwnd != 0 && (!GameWindow.TestMode || GameWindow.Picked != hwnd)) SetTest(true, hwnd);
            };
            watch.Start();
        }
        if (Environment.GetEnvironmentVariable("GAUGE_REINFORCED") is "1") { GameWindow.Reinforced = true; _tray.SetReinforced(true); }   // dev: replays
        _tray.TestMenuOpening += () => _tray.SetTestWindows(GameWindow.Choices(), GameWindow.TestMode, GameWindow.Picked);
        _hotkeys.Pressed += action =>
        {
            if (action != nameof(HotkeySettings.TestMode)) return;
            bool on = !GameWindow.TestMode;
            SetTest(on, on && settings.Screens.ReadBehind ? GameWindow.Front : 0);   // pinned, as ToggleTestMode
        };
        // The picked window closed: 테스트 모드 stays on, nothing read until another is picked (user, 2026-11-05: it never turns
        // itself off). Told once: the engine raises this each step, and the user may have picked another window meanwhile.
        IntPtr closedTold = 0;
        engine.PickedWindowClosed += hwnd => Dispatcher.BeginInvoke(() =>
        {
            if (closedTold == hwnd || !GameWindow.TestMode || GameWindow.Picked != hwnd || GameWindow.Exists(hwnd)) return;
            closedTold = hwnd;
            _tray.Notify("테스트 모드: 고른 창이 닫혔습니다. 트레이 → 테스트 모드에서 다른 창을 고르거나 끄세요.");
        });

        // UI → settings / engine.
        // 스킬창 다시 맞추기 (tray, 설정, hotkey): fit again, since the 아덴 keys may have moved, then detect the character, even
        // when the fit fails. A fit needs the game in front: from the tray or 설정 the taskbar covers the skill bar (Engine.FitNow waits).
        // With a 수동 조정 applied to this game area it asks first: drop it and fit (자동), or keep it and only detect the class.
        void Refit()
        {
            characters.ForgetRequests();   // a 직업각인 read asked for earlier and never captured isn't this refit's
            if (engine.GameAreaKey is { } key && settings.AdjustActive(key))
            {
                if (MessageBox.Show("수동 조정을 버리고 스킬창을 다시 맞출까요?\n\n아니요: 수동 조정은 그대로 두고 클래스와 직업각인만 다시 확인합니다.",
                        "GaugeOverlay", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                {
                    FrontForRead(engine);
                    engine.RequestDetect();
                    return;
                }
                settings.DiscardAdjust(key);
                Configure();
                Save(now: true);
                _settingsWindow.Reload();
            }
            FrontForRead(engine); engine.RequestFit(); engine.RequestDetect();
        }
        _settingsWindow.RefitRequested += Refit;
        _settingsWindow.RevertFitRequested += () => { FrontForRead(engine); engine.RevertTestFit(); };   // recalibrated off the video in front
        WireAdjust(engine, settings, Configure, () => Save(now: true));
        PlayingClass(GameWindow.TestMode ? null : characters.Active?.Class);
        WireSkills(engine);   // the fight's skill map (App.Skills)
        _overlay.LayoutCommitted += () => { Save(now: true); _settingsWindow.Reload(); };   // 숫자 크기 range follows the box size
        foreach (var w in Others) w.LayoutCommitted += () => Save(now: true);
        _settingsWindow.DiagnosticsShownChanged += Configure;   // the table's rows are sent only while it is on screen
        _settingsWindow.IsVisibleChanged += (_, _) => Configure();
        _tray.Refit += Refit;
        // 직업각인 다시 읽기 (tray, 설정, hotkey): the 초각스 is read again past the history (CharacterService.RedetectSubclass).
        void RedetectSubclass()
        {
            if (characters.RedetectSubclass() is { } why) { _tray.Notify(why); return; }
            FrontForRead(engine);
            engine.RequestSubclass();
            _tray.Notify("직업각인을 다시 읽습니다. 게임에서 초각스가 준비되면 읽습니다.");
        }
        _tray.DetectSubclass += RedetectSubclass;
        _settingsWindow.SubclassRedetectRequested += RedetectSubclass;
        // 직업각인 등록 (설정 → 직업 인식): the next ready 초각스 is saved as the 직업각인's icon (CharacterService.RegisterSubclass).
        _settingsWindow.SubclassRegisterRequested += () =>
        {
            if (characters.RegisterSubclass() is { } why) { _tray.Notify(why); return; }
            FrontForRead(engine);
            engine.RequestSubclass();
            _tray.Notify("초각스가 준비되면 그 아이콘을 지금 직업각인으로 등록합니다.");
        };
        engine.LoadingEnded += () => Dispatcher.BeginInvoke(() => homework.Loaded(Clock.Now()));   // a switch picked in the grid happens now
        // The ESC menu (Engine.Menu.cs): who is played, the grid's cards, and their 숙제 tags under them.
        string? menuLogged = null;
        (MenuRead Read, Viewport Vp)? openMenu = null;   // the last open menu the tags were drawn for
        engine.MenuSeen += (read, vp, t) => Dispatcher.BeginInvoke(() =>
        {
            homework.Menu(read, t);
            if (read.Open) api.Lookup(homework.Current, refresh: false);   // the first read of a run, then every 10 min
            else (openMenu, _replayTags) = (null, null);   // a lookup landing later mustn't draw on a closed menu
            if (!read.Open && !_homeworkTags.IsVisible) return;
            var tagged = homework.GridTags(read);
            string seen = $"{read.Open} {read.Current} {read.Cards.Count} {tagged.Count}";
            if (seen != menuLogged) Trace.WriteLine($"[menu] open {read.Open}, playing {read.Current ?? "-"}, picked {read.Picked ?? "-"}, cards: {string.Join(" / ", read.Cards.Select(c => $"{c.Name} {c.ItemLevel?.ToString() ?? "?"}"))}{(read.PlayingCard is null ? "" : " + 접속 중")}; tags on {tagged.Count}");
            menuLogged = seen;
            if (!read.Open) ShowTags([], [], vp);   // while open the tags follow the pixels (MenuRecalled): a read is old by the time it lands
        });
        // Cards known by their pixels: the tags at once, while the OCR takes its time (Engine.Menu.cs).
        // 전투력 beside the cards' and the portrait's item level (lostark-api.plan.md phase 4): its own switch, not 숙제 표시.
        Role RoleOfSubclass(string key) => MenuPower.RoleIn(characters.Classes, key);
        engine.MenuRecalled += (read, vp, _) => Dispatcher.BeginInvoke(() => ShowFor(read, vp));
        // A lookup that lands while the menu is open: its 전투력 now, not at the next MenuRecalled (an unchanged menu may send none).
        api.Updated += () => { if (openMenu is var (read, vp)) ShowFor(read, vp); };
        void ShowFor(MenuRead read, Viewport vp)
        {
            var (tagged, power) = (homework.GridTags(read), homework.PowerTags(read, RoleOfSubclass));
            openMenu = read.Open ? (read, vp) : null;
            // Kept for a return to the game (an unchanged open menu sends no new MenuRecalled), worked out again then: values may have changed.
            _replayTags = tagged.Count + power.Count > 0 ? () => ShowFor(read, vp) : null;
            ShowTags(tagged, power, vp);
        }
        void ShowTags(IReadOnlyList<(MenuCard Card, IReadOnlyList<HomeworkTag> Tags)> tagged, IReadOnlyList<PowerTag> power, Viewport vp)
        {
            _homeworkTags.Update(vp, [.. tagged.Select(x =>
                (Geometry.MenuArea(vp, new PixelRect((int)x.Card.X, (int)x.Card.Y, (int)x.Card.W, (int)(x.Card.Bottom - x.Card.Y))), x.Tags))],
                [.. power.Select(t => t with { Line = Geometry.MenuArea(vp, t.Line) })], allowed: GameSeen);
        }
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
            FrontForRead(engine);
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
        void CopyTokenWithNotice() => _tray.Notify(party.Token is null ? "참가 코드는 호스트만 복사할 수 있습니다."
            : CopyToken() ? "참가 코드를 복사했습니다." : "클립보드를 쓸 수 없습니다. 잠시 뒤 다시 눌러 주세요.");
        async void HostParty()
        {
            party.PublishLook(MyLook());
            if (!await party.Host(settings.Party.HostPort, PartyName())) return;
            if (party.Token is { } t) TokenDialog.ShowToken(t, settings.Party.HostPort, settings.Theme);
            else _tray.Notify("공유 방을 열었습니다.");
        }
        async void JoinParty(string? token)
        {
            if (token is null) return;
            party.PublishLook(MyLook());
            if (await party.Join(token, PartyName())) _tray.Notify("공유 방에 참가했습니다.");
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
        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => CheckInUse(), Dispatcher).Start();   // hides them once reads stop

        Configure();
        _overlay.Show();
        foreach (var w in Others) w.Show();
        UpdateVisibility();
        engine.Start();
        ApplyDisplay();

        if (wasReset) _tray.Notify("설정 파일을 읽지 못해 기본값으로 시작했습니다.");
        if (!settings.FirstRunDone)
        {
            first = ShowFirstRun(OnSettingsChanged);
            first.FitRequested += () => { FrontForRead(engine); engine.RequestFit(); };   // as Refit: the game in front
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
                var (ok, newer) = await updater.CheckAsync(settings.Updates.Channel);
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
                if (UpdateCheck.IsDowngrade(release, Updater.CurrentTag)) BackUpSettings(release.Tag);
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

        // Back to an older (stable) version: it may not read what this one saved, so the file is kept beside it as it is
        // ("settings.before-v0.15.3.json"). The older version then opens settings.json as far as it understands it.
        void BackUpSettings(string tag)
        {
            try
            {
                Save(now: true);
                File.Copy(SettingsStore.DefaultPath, Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, $"settings.before-{tag}.json"), overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[update] settings backup: {ex.Message}"); }
        }

        // The first start after an update: 설정 opens again, as it was when the update was started from it (user, 2026-10-13),
        // with what changed over it, once. The notes are dropped if this isn't the version that was installed.
        void ShowWhatsNew()
        {
            var (tag, notes) = (settings.Updates.WhatsNewTag, settings.Updates.WhatsNew);
            if (tag.Length == 0) return;
            (settings.Updates.WhatsNewTag, settings.Updates.WhatsNew) = ("", "");
            Save(now: true);
            bool installed = tag == Updater.CurrentTag;
            Dispatcher.BeginInvoke(() =>
            {
                _settingsWindow.Show();
                _settingsWindow.Activate();
                if (installed)
                    MessageBox.Show(_settingsWindow, notes.Length > 0 ? $"{Josa.By(tag)} 업데이트했습니다.\n\n{notes}" : $"{Josa.By(tag)} 업데이트했습니다.",
                        "GaugeOverlay 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
            }, DispatcherPriority.ApplicationIdle);
        }

        async Task CheckForUpdate()
        {
            try
            {
                if ((await updater.CheckAsync(settings.Updates.Channel)).Newer is not { } release) return;
                _settingsWindow.SetUpdate(release);
                if (UpdateCheck.IsDowngrade(release, Updater.CurrentTag)) return;   // going back waits for the button
                _tray.Notify($"GaugeOverlay {Josa.By(release.Tag)} 업데이트하는 중입니다. 끝나면 다시 켜집니다.");
                await Install(release);
            }
            catch (Exception ex) { Trace.WriteLine($"[update] check: {ex}"); }   // never shown: the app runs on as it is
        }
    }
}
