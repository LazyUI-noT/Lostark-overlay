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
        if (Enum.TryParse(Flag("cooldown"), out CooldownStyle style)) settings.Overlay.CooldownStyle = style;
        if (Enum.TryParse(Flag("fill"), out FillFrom from)) settings.Overlay.FillFrom = from;
        if (Enum.TryParse(Flag("peer-colors"), out ColorSource source)) ClassProfiles.Edit(settings, ClassProfiles.Template).Members.Color = source;
        if (int.TryParse(Flag("support"), out int support)) (settings.Party.SupportFixed, settings.Party.SupportNumber) = (true, support);
        if (int.TryParse(Flag("my-number"), out int number)) settings.Party.MyNumber = number;
        if (int.TryParse(Flag("name-length"), out int nameLength)) settings.Party.NameLength = nameLength;
        if (Flag("unseen") is { } unseen)   // 설정 → 캐릭터 as after an update that added this class: NEW on its rows
            settings.SeenSupport = [.. ClassSupport.Keys(new CharacterService(settings, CharacterService.DefaultProfilesDir).Support(s => s.ToString()))
                .Where(k => !k.StartsWith(unseen + "/", StringComparison.Ordinal))];
        if (Enum.TryParse(Flag("theme"), out UiTheme theme)) settings.Theme = theme;
        if (args.Contains("--receive")) (settings.Party.ReceiveSize, settings.Party.ReceiveLayout) = (true, true);
        if (args.Contains("--side-buffs")) settings.Buffs.Side = true;
        if (args.Contains("--detach-buffs")) settings.Buffs.Detached = true;
        if (args.Contains("--debuffs") || args.Contains("--duelist")) settings.Debuffs.Raid = Raid.모르둠;
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
        if (args.Contains("--judgment")) { settings.Judgment.Set("워로드 · 고독한기사", true); (settings.Judgment.Left, settings.Judgment.Top) = (828, 588); }   // above the demo overlay
        if (args.Contains("--cores")) settings.SetCore("워로드 · 고독한기사", new CoreChoice { Apply = true, Sun = 3 }.WithSlot("버스트 캐넌", SlotId.일반3));
        if (Flag("cores") == "전투태세")   // 전투태세 active, 해 2 with 방패 돌진 on R (일반4)
        {
            settings.Characters["워로드 · 전투태세"] = new CharacterSettings { ClassFamily = "워리어", Class = "워로드", Subclass = "전투태세" };
            settings.ActiveCharacter = "워로드 · 전투태세";
            settings.SetCore("워로드 · 전투태세", new CoreChoice { Apply = true, Sun = 2 }.WithSlot("방패 돌진", SlotId.일반4));
        }
        if (args.Contains("--bar"))
        {
            var bar = GaugeBar.Edit(settings, "워로드", "고독한기사");
            (bar.Splits, bar.UnifiedColor, bar.SegmentColors, bar.SplitThickness) = ([0.25, 0.5, 0.75], false, ["#1E9BE6", "#2EC4B6", "#E8B53A", "#E8262B"], 2);
        }
        var demoCharacters = new CharacterService(settings, CharacterService.DefaultProfilesDir);
        ClassProfileMigration.EnsureOwn(settings, demoCharacters.Classes);   // as at the real start
        BuildUi(settings, demoCharacters, ApplyUi);
        _tray.SetParty("호스트 중 (3/4)");
        var demoFit = FitText(new Viewport(0, 0, 1920, 1080), new Fit(100), "");
        _settingsWindow.RefitRequested += () => _settingsWindow.SetFitResult(demoFit);

        var demo = new DemoSnapshots(args.Contains("--duelist"));
        void Push()
        {
            if (!demo.TryNext(out var self, out var peers)) return;
            _overlay.Update(self);
            _buffs.Update(self);
            _debuffs.Update(self);
            _judgment.Update(self);
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
        if (Version.TryParse(Flag("update-to"), out var offered))   // the update button as with that release found
            _settingsWindow.SetUpdate(new Release(offered, $"v{offered}", "", ""));
        if (Flag("edit-as") is { } entry) _settingsWindow.PickEntry(entry);
        if (args.Contains("--first-run")) { var first = ShowFirstRun(ApplyUi); first.FitRequested += () => first.SetFitResult(true, demoFit); }
        if (args.Contains("--debug") || args.Contains("--adjust")) { _debug.Capturable = true; _debug.Show(); }
        if (args.Contains("--adjust")) _debug.ShowAdjust();
        if (args.Contains("--edit")) SetEditMode(true);
        if (Enum.TryParse(Flag("pick"), out Part picked)) _settingsWindow.ShowBox(picked, plate: false);
        if (args.Contains("--tray-menu")) Dispatcher.BeginInvoke(_tray.DemoShowMenu, DispatcherPriority.ApplicationIdle);
        if (args.Contains("--join")) Dispatcher.BeginInvoke(() => JoinDialog.Ask(settings.Theme), DispatcherPriority.ApplicationIdle);
        if (args.Contains("--token")) Dispatcher.BeginInvoke(() => TokenDialog.ShowToken("demo-token-1234567890abcdef", settings.Party.HostPort, settings.Theme), DispatcherPriority.ApplicationIdle);
        if (args.Contains("--countdown")) _ = CountdownWindow.Run(new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight), capturable: true);
        // --render-to=<folder>: the shown overlay windows drawn together, each at its place, into <folder>\f0000.png … with their
        // transparency (no screen capture), then the demo exits. For README/post pictures and GIFs laid over a game frame.
        // --render-frames=N (1) every --render-every=ms (100) from --render-at=s (4.5); --render-hide=<text>: leave out windows
        // whose title has it (e.g. 디버프). frames.txt lists each frame's time in seconds.
        if (Flag("render-to") is { } renderDir) RenderFrames(renderDir, Flag);
    }

    void RenderFrames(string dir, Func<string, string?> flag)
    {
        int frames = int.TryParse(flag("render-frames"), out var f) ? f : 1;
        double every = double.TryParse(flag("render-every"), out var e) ? e : 100, at = double.TryParse(flag("render-at"), out var a) ? a : 4.5;
        string? hide = flag("render-hide");
        // --render-region=x,y,w,h: only that part of the screen (faster, so more frames a second); the whole screen otherwise.
        var region = flag("render-region")?.Split(',').Select(double.Parse).ToArray() is [var rx, var ry, var rw, var rh]
            ? new Rect(rx, ry, rw, rh) : new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        Directory.CreateDirectory(dir);
        var times = new List<string>();
        var clock = Stopwatch.StartNew();
        int n = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(at) };
        timer.Tick += (_, _) =>
        {
            timer.Interval = TimeSpan.FromMilliseconds(every);
            var screen = new System.Windows.Media.DrawingVisual();
            using (var dc = screen.RenderOpen())
                foreach (Window w in Others.Prepend(_overlay).Where(w => w.IsVisible && (hide is null || !w.Title.Contains(hide))))
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
}
