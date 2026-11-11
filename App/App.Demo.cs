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

/// <summary>--demo: the windows with sample data and the render-to-files mode, for screenshots and checks; --build-gauge.</summary>
public partial class App
{
    /// <summary>--build-gauge &lt;shots&gt; &lt;out&gt; [--region=x,y,w,h] [--no-ui]: gauge_empty.png and gauge_full.png (or empty.png, full.png) in &lt;shots&gt; (screenshots of the
    /// game at any resolution, HUD size or 21:9, resampled to the reference screen by <see cref="ReferenceShot"/>) → gauge.json,
    /// mask.png, emblem_empty.png and emblem_full.png in &lt;out&gt; (Profiles/aden/README.md). The search region, in reference
    /// pixels, defaults to the emblem and 40 px around it.</summary>
    static void BuildGauge(string[] args)
    {
        var paths = args.Where(a => !a.StartsWith("--")).ToArray();
        string message;
        try
        {
            if (paths.Length != 2) throw new ArgumentException("사용법: --build-gauge <스크린샷 폴더> <출력 폴더> [--region=x,y,w,h] [--no-ui]");
            var e = Geometry.RefEmblem;
            var region = args.FirstOrDefault(a => a.StartsWith("--region="))?[9..].Split(',').Select(int.Parse).ToArray() is [var x, var y, var w, var h]
                ? new PixelRect(x, y, w, h) : new PixelRect(e.X - 40, e.Y - 40, e.Width + 80, e.Height + 40);
            var (empty, full, found) = ReferenceShot.Normalize(Png.Load(Shot(paths[0], "empty")), Png.Load(Shot(paths[0], "full")));
            var (json, mask) = GaugeLoader.Build(empty, full, region);
            Directory.CreateDirectory(paths[1]);
            File.WriteAllText(Path.Combine(paths[1], GaugeLoader.JsonFile), json);
            Png.Save(Path.Combine(paths[1], GaugeLoader.MaskFile), mask);
            // The class's centre piece, empty and full, for telling another gauge class on screen (CharacterService.CheckClass).
            Png.Save(Path.Combine(paths[1], "emblem_empty.png"), ReferenceShot.Emblem(empty));
            Png.Save(Path.Combine(paths[1], "emblem_full.png"), ReferenceShot.Emblem(full));
            message = $"{found}.\n{paths[1]}에 {GaugeLoader.JsonFile}, {GaugeLoader.MaskFile}({mask.Width}×{mask.Height}), emblem_empty.png, emblem_full.png를 만들었습니다.\n\n{json}";
        }
        catch (Exception ex)   // a dev tool, also run by a script (--no-ui): reported, never a blocking crash dialog
        {
            message = $"{ex.GetType().Name}: {ex.Message}";
        }
        Report(args, "GaugeOverlay --build-gauge", message);
    }

    void StartDemo(string[] args)
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(Path.GetTempPath(), "GaugeOverlay-demo.log")));
        Trace.AutoFlush = true;

        // --from=<settings.json> [--profile=N]: a copy of a real settings file (never saved here), with overlay profile N, for screenshots.
        var settingsFile = args.FirstOrDefault(a => a.StartsWith("--from=", StringComparison.Ordinal))?["--from=".Length..];
        var settings = settingsFile is not null ? new SettingsStore(settingsFile).Load(out _) : new Settings { FirstRunDone = !args.Contains("--first-run") };
        if (settingsFile is not null && int.TryParse(args.FirstOrDefault(a => a.StartsWith("--profile=", StringComparison.Ordinal))?["--profile=".Length..], out int profile))
            settings.SwitchProfile(profile - 1);
        string? Flag(string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}="))?[(name.Length + 3)..];
        // 쿨 진행 표시 is per class: the flags set 기본 and every 개별 class, as an older file's global value moves.
        IEnumerable<MineProfile> Mines() => settings.Classes.Where(e => e.Key == ClassProfiles.Template || e.Value.Own).Select(e => e.Value.Mine)
            .Prepend(ClassProfiles.Edit(settings, ClassProfiles.Template).Mine).Distinct();
        if (Enum.TryParse(Flag("cooldown"), out CooldownStyle style)) foreach (var m in Mines()) m.CooldownStyle = style;
        if (Enum.TryParse(Flag("fill"), out FillFrom from)) foreach (var m in Mines()) m.FillFrom = from;
        if (Enum.TryParse(Flag("peer-colors"), out ColorSource source)) ClassProfiles.Edit(settings, ClassProfiles.Template).Members.Color = source;
        if (int.TryParse(Flag("support"), out int support)) (settings.Party.SupportFixed, settings.Party.SupportNumber) = (true, support);
        if (int.TryParse(Flag("my-number"), out int number)) settings.Party.MyNumber = number;
        if (int.TryParse(Flag("name-length"), out int nameLength)) settings.Party.NameLength = nameLength;
        if (Flag("unseen") is { } unseen)   // 설정 → 직업 인식 as after an update that added this class: NEW on its rows
            settings.SeenSupport = [.. ClassSupport.Keys(new CharacterService(settings, CharacterService.DefaultProfilesDir).Support(s => s.ToString()))
                .Where(k => !k.StartsWith(unseen + "/", StringComparison.Ordinal))];
        if (Enum.TryParse(Flag("theme"), out UiTheme theme)) settings.Theme = theme;
        if (args.Contains("--receive")) (settings.Party.ReceiveSize, settings.Party.ReceiveLayout) = (true, true);
        if (args.Contains("--side-buffs")) settings.Buffs.Side = true;
        if (args.Contains("--detach-buffs")) settings.Buffs.Detached = true;
        if (args.Contains("--debuffs") || args.Contains("--duelist"))
        {
            settings.Debuffs.Raid = Raid.모르둠;
        }
        if (args.Contains("--meter")) (settings.Meter.On, settings.Meter.Scope) = (true, MeterScope.파티별);
        if (Enum.TryParse<MeterLook>(Flag("meter-look"), out var look)) (settings.Meter.Look, settings.Meter.Graph) = (look, true);
        if (Enum.TryParse<MeterRange>(Flag("meter-range"), out var range)) settings.Meter.Range = range;
        if (args.Contains("--meter-solo"))   // my DPS only, no buff rows (the map's GIFs: user, 2026-11-10)
            (settings.Meter.Scope, settings.Meter.BuffBars, settings.Meter.BuffShareOff, settings.Meter.Other) = (MeterScope.나, false, true, false);
        if (Flag("meter-skill") is { } skill)
        {
            DemoSnapshots.MeterSkillShown = new(skill, skill == "none" ? null : 3.2);
            foreach (string? key in new[] { null, "워로드 · 고독한기사", RecordCharacter.ClassOf(DemoSnapshots.DemoRecords()[0]) })   // 기본, the demo's 편집 중, its fights
            {
                var w = ClassProfiles.EditWindows(settings, key);
                (w.MeterSkill, w.MeterSkillName) = (SlotId.일반1, skill);
            }
        }
        if (args.Contains("--raid")) (settings.Raids.Gate(RaidSettings.MordumGate).CountWindow, settings.Raids.Gate(RaidSettings.MordumGate).CountAuto) = (true, false);
        OverlayRenderer.DemoReducedMotion = args.Contains("--reduced");
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
        if (Flag("as")?.Split(':') is [var asClass, var asSub, var asFamily])   // --as=바드:절실한구원:마법사: another 직업각인 (its gauge's splits)
        {
            settings.Characters[$"{asClass} · {asSub}"] = new CharacterSettings { ClassFamily = asFamily, Class = asClass, Subclass = asSub };
            settings.ActiveCharacter = $"{asClass} · {asSub}";
        }
        if (args.Any(a => a == "--amsu" || a.StartsWith("--amsu="))) { settings.Amsu.On = true; (settings.Amsu.Left, settings.Amsu.Top) = (828, 500); }
        if (Enum.TryParse(Flag("amsu-look"), out AmsuLook amsuLook)) settings.Amsu.Look = amsuLook;
        if (args.Contains("--judgment")) { settings.Judgment.Set("워로드 · 고독한기사", true); (settings.Judgment.Left, settings.Judgment.Top) = (828, 588); }   // above the demo overlay
        if (args.Contains("--judgment-icon")) { settings.Judgment.Set("워로드 · 고독한기사", true); settings.Judgment.Display = JudgmentDisplay.ReadyIcon; }
        if (args.Contains("--cores")) settings.SetCore("워로드 · 고독한기사", new CoreChoice { Apply = true, Sun = 3 }.WithSlot("버스트 캐넌", SlotId.일반3));
        if (Flag("cores") == "전투태세")   // 전투태세 active, 해 2 with 방패 돌진 on R (일반4)
        {
            settings.Characters["워로드 · 전투태세"] = new CharacterSettings { ClassFamily = "워리어", Class = "워로드", Subclass = "전투태세" };
            settings.ActiveCharacter = "워로드 · 전투태세";
            settings.SetCore("워로드 · 전투태세", new CoreChoice { Apply = true, Sun = 2 }.WithSlot("방패 돌진", SlotId.일반4));
        }
        if (Enum.TryParse(Flag("only-part"), out OverlayPart only))   // one window alone, for tools/make-map-gifs.ps1 (with --render-to)
        {
            foreach (var part in Enum.GetValues<OverlayPart>()) TrayToggles.Show(settings, part, part == only);
            foreach (var kind in Enum.GetValues<BuffKind>()) settings.Buffs.Edit(kind).Visible = only == OverlayPart.Buffs;   // the demo draws another key's bars
        }
        if (args.Contains("--bar"))
        {
            var bar = GaugeBar.Edit(settings, "워로드", "고독한기사");
            (bar.Splits, bar.UnifiedColor, bar.SegmentColors, bar.SplitThickness) = ([0.25, 0.5, 0.75], false, ["#1E9BE6", "#2EC4B6", "#E8B53A", "#E8262B"], 2);
        }
        var demoCharacters = new CharacterService(settings, CharacterService.DefaultProfilesDir);
        ClassProfileMigration.EnsureOwn(settings, demoCharacters.Classes);   // as at the real start
        BuildUi(settings, demoCharacters, ApplyUi);
        if (args.Contains("--saved-icons"))   // the active 직업각인's saved icons (IconStore), as the live start draws them
            foreach (var (slot, icon) in new IconStore(IconStore.DefaultDir).Load(settings.Characters[settings.ActiveCharacter].Class, settings.Characters[settings.ActiveCharacter].Subclass))
                _overlay.Renderer.SetIcon(slot, icon);
        if (args.Contains("--records"))   // two made-up fights in a folder of their own, never the real records
        {
            var dir = Path.Combine(Path.GetTempPath(), "GaugeOverlay-demo-records");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var store = new RecordStore(dir);
            foreach (var r in DemoSnapshots.DemoRecords()) store.Save(r);
            if (Flag("records-from") is { } extra)   // copies of saved fights beside them, to look at a real one
                foreach (var file in Directory.GetFiles(extra, "*.json.gz")) File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            if (args.Contains("--records-bin"))   // every fight to the 휴지통: its chip is the only one, so it opens picked
                foreach (var (file, _) in store.List()) store.Delete(file);
            _settingsWindow.Records = store;
            _settingsWindow.RefreshRecords();        }
        _tray.SetParty("호스트 중 (3/4)");
        var demoFit = FitText(new Viewport(0, 0, 1920, 1080), new Fit(100), "");
        _settingsWindow.RefitRequested += () => _settingsWindow.SetFitResult(demoFit);

        var demo = new DemoSnapshots(args.Contains("--duelist"), Flag("amsu") switch
        {
            "warn" => (3, true),
            "unseen" => (13, false),
            _ when args.Contains("--amsu") => (13, true),
            _ => ((double, bool)?)null,
        });
        _meter.Renderer.MeterMe = 1;   // DemoSnapshots' meter: "나" is id 1
        void Push()
        {
            if (!demo.TryNext(out var self, out var peers)) return;
            _overlay.Update(self);
            _buffs.Update(self);
            _debuffs.Update(self);
            _judgment.Update(self);
            _amsu.Update(self);
            _raid.Update(self);
            _meter.Update(self);
            ShowPeers(peers);
            var now = Clock.Now();
            foreach (var st in self.Snapshot.Slots)
            {
                var (crop, reading) = DemoSnapshots.DebugRow(st, now);
                _settingsWindow.UpdateDiagnostics(st.Slot, crop, reading, st);
            }
        }
        Push();
        if (Flag("render-sim") is null) new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Normal, (_, _) => Push(), Dispatcher).Start();

        ApplyUi();
        _overlay.Show();
        foreach (var w in Others) w.Show();
        (_meterInUse, _countInUse) = (true, true);   // the demo's figures are always "read": --meter and --raid show their windows
        UpdateVisibility();
        if (args.Contains("--grid-tags"))   // 숙제 tags as under the grid's cards and 전투력 beside the levels, at goal.png's spots
            _homeworkTags.Update(new Viewport(0, 0, 1920, 1080), [(new PixelRect(846, 426, 245, 19), [new HomeworkTag("세나메", HomeworkColor.Purple, false, 0), new HomeworkTag("종하↓", HomeworkColor.Orange, true, 0)])],
                [new PowerTag(new PixelRect(972, 426, 0, 12), "2,345", Role.딜러), new PowerTag(new PixelRect(440, 704, 0, 11), "5,004", Role.서포터)], allowed: true);
        if (args.Contains("--no-key")) CredentialVault.PretendNoKey();   // the key gate as a new install without a key sees it
        if (args.Contains("--homework-demo")) DemoHomework(settings);
        if (args.Contains("--settings")) _settingsWindow.Show();
        if (new[] { "map-open", "map-pin", "map-fail", "map-seen", "map-preview", "map-scroll", "map-line", "map-cut" }.Any(f => Flag(f) is not null))
            _settingsWindow.DemoMap(Flag("map-open"), Flag("map-pin"), Flag("map-fail"), Flag("map-seen"), Flag("map-preview"), Flag("map-scroll"), Flag("map-line"), Flag("map-cut"));
        if (Flag("hp-table") is { } hpScroll) _settingsWindow.DemoHpTable(hpScroll);
        if (args.Contains("--resources")) _settingsWindow.DemoResources();
        if (args.Contains("--map-hover-test")) _settingsWindow.DemoHoverTest();
        if (Flag("tab") is { } tab) _settingsWindow.ShowTab(tab);
        if (Flag("record-view") is { } view) _settingsWindow.DemoOpenRecord(view == "compare", view == "run");   // with --records: open | compare | run (a fight joined from its files)
        if (Version.TryParse(Flag("update-to"), out var offered))   // the update button as with that release found
            _settingsWindow.SetUpdate(new Release(offered, $"v{offered}", "", ""));
        if (Flag("edit-as") is { } entry) _settingsWindow.PickEntry(entry);
        if (args.Contains("--first-run")) { var first = ShowFirstRun(ApplyUi); first.FitRequested += () => first.SetFitResult(true, demoFit); }
        if (args.Contains("--debug")) _settingsWindow.ShowDiagnostics();
        if (args.Contains("--adjust"))   // the layer over the primary screen, as over a 1920×1080 game at HUD 100 (no engine: edits go nowhere)
        {
            _layer.Capturable = true;
            _layer.Open(1, new Viewport(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight), new Fit(100), 120, false,
                settings.Buffs.Count, settings.Debuffs.BossIconSize, null);
        }
        if (args.Contains("--edit")) SetEditMode(true);
        // As a click on the preview: the box's (or with "plate", 창 전체's) options in the inspector.
        if (Enum.TryParse(Flag("pick"), out Part picked)) _settingsWindow.Inspect(new("Skills", picked, null));
        else if (Flag("pick") == "plate") _settingsWindow.Inspect(new("Plate", null, null));
        if (args.Contains("--tray-menu")) Dispatcher.BeginInvoke(_tray.DemoShowMenu, DispatcherPriority.ApplicationIdle);
        if (args.Contains("--join")) Dispatcher.BeginInvoke(() => JoinDialog.Ask(settings.Theme), DispatcherPriority.ApplicationIdle);
        if (args.Contains("--token")) Dispatcher.BeginInvoke(() => TokenDialog.ShowToken("demo-token-1234567890abcdef", settings.Party.HostPort, settings.Theme), DispatcherPriority.ApplicationIdle);
        if (args.Contains("--countdown")) _ = CountdownWindow.Run(new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight), capturable: true);
        // --render-to=<folder>: the shown overlay windows drawn together, each at its place, into <folder>\f0000.png … with their
        // transparency (no screen capture), then the demo exits. For README/post pictures and GIFs laid over a game frame.
        // --render-frames=N (1) every --render-every=ms (100) from --render-at=s (4.5); --render-hide=<text>: leave out windows
        // whose title has it (e.g. 디버프). frames.txt lists each frame's time in seconds.
        if (Flag("render-to") is { } renderDir) RenderFrames(renderDir, Flag, Flag("render-sim") is null ? null : PreviewPlayer.SampleFor(demoCharacters), settings.Meter.Scope, PreviewPlayer.Gauge(settings, demoCharacters));
    }

    /// <param name="sim">--render-sim=on: frames from the 설정 previews' script (<see cref="PreviewSim"/>, 자동), each drawn as at
    /// its exact time (start + n × every), not the wall clock: runs for each style of a window give the same moments, so the
    /// map's GIF rows play in sync (user, 2026-11-10).</param>
    void RenderFrames(string dir, Func<string, string?> flag, Player? sim = null, MeterScope meterScope = MeterScope.공대, GaugePlay? gauge = null)
    {
        int frames = int.TryParse(flag("render-frames"), out var f) ? f : 1;
        double every = double.TryParse(flag("render-every"), out var e) ? e : 100, at = double.TryParse(flag("render-at"), out var a) ? a : 4.5;
        string? hide = flag("render-hide");
        bool gaugeLoop = flag("gauge-loop") is not null;   // --gauge-loop=on: the gauge on GaugePlay.Looped, not 자동's clicks
        var windows = Enum.TryParse(flag("only-part"), out OverlayPart only) ? WindowsOf(only) : [.. Others.Prepend(_overlay)];
        // --render-region=x,y,w,h: only that part of the screen (faster, so more frames a second); the whole screen otherwise.
        var region = flag("render-region")?.Split(',').Select(double.Parse).ToArray() is [var rx, var ry, var rw, var rh]
            ? new Rect(rx, ry, rw, rh) : new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        Directory.CreateDirectory(dir);
        var times = new List<string>();
        var clock = Stopwatch.StartNew();
        int n = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(at) };
        var start = Clock.Now();
        var script = sim is null ? null : new PreviewSim(sim.Snapshot, start);
        timer.Tick += (_, _) =>
        {
            timer.Interval = TimeSpan.FromMilliseconds(every);
            if (script is not null)
            {
                var t = start + TimeSpan.FromMilliseconds(every * n);
                script.Auto(t, gauge ?? GaugePlay.Plain);
                var shot = script.At(t) with { MeterView = DemoSnapshots.DemoMeter(SimMeterFrom + (int)(every * n / 1000), meterScope) };
                if (gaugeLoop && shot.Gauge is not null) shot = shot with { Gauge = (gauge ?? GaugePlay.Plain).Looped(every * n / 1000) };   // the map's 아덴 GIF
                var p = new Player("", shot);
                foreach (var w in windows) w.Renderer.Update(p, t);
                foreach (var w in windows) w.UpdateLayout();
            }
            var screen = new System.Windows.Media.DrawingVisual();
            using (var dc = screen.RenderOpen())
                foreach (Window w in windows.Where(w => w.IsVisible && (hide is null || !w.Title.Contains(hide))))
                {
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth), (int)Math.Ceiling(w.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bmp.Render(w);
                    dc.DrawImage(bmp, new Rect(w.Left - region.X, w.Top - region.Y, bmp.PixelWidth, bmp.PixelHeight));
                }
            var full = new System.Windows.Media.Imaging.RenderTargetBitmap((int)region.Width, (int)region.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            full.Render(screen);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(full));
            using (var file = File.Create(Path.Combine(dir, $"f{n:0000}.png"))) png.Save(file);
            times.Add(clock.Elapsed.TotalSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            if (++n < frames) return;
            timer.Stop();
            File.WriteAllLines(Path.Combine(dir, "frames.txt"), times);
            Shutdown();
        };
        timer.Start();
    }
    /// <summary>--render-sim: the 전분 second the meter starts at.</summary>
    const int SimMeterFrom = 60;

    /// <summary>The windows that draw <paramref name="part"/> (skills, gauge and undetached buffs share the main one).</summary>
    OverlayWindow[] WindowsOf(OverlayPart part) => part switch
    {
        OverlayPart.Debuffs => [_debuffs], OverlayPart.Judgment => [_judgment], OverlayPart.Amsu => [_amsu],
        OverlayPart.BarCount => [_raid], OverlayPart.Meter => [_meter], OverlayPart.Party => [.. _peers],
        _ => [_overlay, _buffs],
    };

    /// <summary>--homework-demo: 설정 → 숙제 with a character, three picks and a week's clears (one of them 확인필요), in a temp file.</summary>
    void DemoHomework(Settings settings)
    {
        var gates = RaidData.Load(CharacterService.DefaultProfilesDir);
        var shipped = HomeworkData.Load(CharacterService.DefaultProfilesDir);
        var picks = Homework.Allocate(1700, shipped);
        settings.Homework.Characters =
        [
            new HomeworkCharacter { Name = "데모", ClassName = "워로드", ItemLevel = 1700, Picks = [.. picks] },
            new HomeworkCharacter { Name = "밝기리", ClassName = "발키리", ItemLevel = 1660, FromCard = true, Picks = [.. Homework.Allocate(1660, shipped)] },   // read off its card
        ];
        var store = new HomeworkStore(Path.Combine(Path.GetTempPath(), "GaugeOverlay-demo-homework.json"));
        File.Delete(Path.Combine(Path.GetTempPath(), "GaugeOverlay-demo-homework.json"));
        var now = DateTime.Now;
        if (picks.Count >= 2)
        {
            store.Add(new HomeworkClear("데모", picks[0].Raid, picks[0].Difficulty, now), now);
            store.Add(new HomeworkClear("데모", picks[1].Raid, picks[1].Difficulty, now, Unsure: true), now);
        }
        _settingsWindow.Homework = new HomeworkService(settings, gates, shipped, [], store, new RecordStore(Path.Combine(Path.GetTempPath(), "GaugeOverlay-demo-records")), Dispatcher, () => { }, () => { }, () => true);
    }
}
