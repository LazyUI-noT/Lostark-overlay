using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Platform;

/// <summary><c>GaugeOverlay --capture-test</c>: captures a few regions of the primary monitor through
/// <see cref="CaptureFactory"/> for about 3 s and writes the crops (.bgra) and a report to
/// %TEMP%\GaugeOverlay-capture-test\. A small window in the top-left corner changes colour every frame,
/// so Desktop Duplication has frames to deliver on an otherwise static desktop.</summary>
internal static partial class CaptureSmokeTest
{
    public static void Run(Application app)
    {
        string dir = Path.Combine(Path.GetTempPath(), "GaugeOverlay-capture-test");
        Directory.CreateDirectory(dir);

        var fill = new System.Windows.Controls.Border();
        var win = new Window
        {
            Left = 0, Top = 0, Width = 120, Height = 120, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            Topmost = true, ShowActivated = false, ShowInTaskbar = false, Content = fill,
        };
        int n = 0;
        CompositionTarget.Rendering += (_, _) => fill.Background = new SolidColorBrush(Color.FromRgb((byte)(n++ * 7), 0x80, (byte)(255 - n)));
        win.Show();

        Task.Run(() => Capture(dir)).ContinueWith(t => app.Dispatcher.Invoke(() =>
        {
            if (t.Exception is { } ex) File.WriteAllText(Path.Combine(dir, "report.txt"), "FAILED\n" + ex);
            app.Shutdown();
        }));
    }

    private static void Capture(string dir)
    {
        var report = new StringBuilder();
        IntPtr shell = GetShellWindow();
        var screen = GameWindow.MonitorBounds(GameWindow.MonitorOf(shell));
        using var source = CaptureFactory.Create(shell);
        report.AppendLine($"backend: {source.GetType().Name}");
        report.AppendLine($"border: false ({(source is WgcCapture ? "WGC with IsBorderRequired=false" : "Desktop Duplication never draws one")})");
        report.AppendLine($"WGC session started: {source is WgcCapture}");
        report.AppendLine($"OS: {Environment.OSVersion.Version}");
        report.AppendLine($"primary monitor: {screen}");

        var regions = new (string Name, PixelRect Rect)[]
        {
            ("animated", new(screen.X + 10, screen.Y + 10, 100, 100)),
            ("centre", new(screen.X + screen.Width / 2 - 80, screen.Y + screen.Height / 2 - 60, 160, 120)),
            ("bottom", new(screen.X + screen.Width / 2 - 200, screen.Bottom - 60, 400, 60)),
            ("barprobe", Letterbox.Probe(screen)),
        };
        var rects = regions.Select(r => r.Rect).ToArray();
        var images = rects.Select(r => new Image(r.Width, r.Height)).ToArray();

        // Phase 1 (1.5 s): block on each frame -> frame intervals.
        List<long> stamps = [];
        int timeouts = 0;
        var phase = Stopwatch.StartNew();
        while (phase.Elapsed < TimeSpan.FromSeconds(1.5))
        {
            if (source.TryCapture(rects, images, TimeSpan.FromMilliseconds(100), out long ts)) stamps.Add(ts);
            else timeouts++;
        }

        // Phase 2 (1.5 s): let a frame be ready, then take it with a zero timeout -> cost of one call.
        List<double> costs = [];
        List<double> latencies = [];
        List<long> polled = [];
        phase.Restart();
        while (phase.Elapsed < TimeSpan.FromSeconds(1.5))
        {
            Thread.Sleep(20);
            long t0 = Stopwatch.GetTimestamp();
            if (!source.TryCapture(rects, images, TimeSpan.Zero, out long ts)) continue;
            long t1 = Stopwatch.GetTimestamp();
            costs.Add(Ms(t1 - t0));
            latencies.Add(Ms(t1 - ts));
            polled.Add(ts);
        }

        int blocking = stamps.Count;
        var intervals = stamps.Zip(stamps.Skip(1), (a, b) => Ms(b - a)).ToList();
        stamps.AddRange(polled);
        report.AppendLine($"frames: {stamps.Count} (blocking phase {blocking}, timeouts {timeouts}; polled phase {polled.Count})");
        report.AppendLine($"frame interval ms (blocking phase): {Stats(intervals)}");
        report.AppendLine($"timestamps strictly increasing: {stamps.Zip(stamps.Skip(1)).All(p => p.Second > p.First)}");
        report.AppendLine($"call cost ms (frame ready, {rects.Length} regions, {rects.Sum(r => r.Width * r.Height)} px): {Stats(costs)}");
        report.AppendLine($"now - frame timestamp ms (same clock check): {Stats(latencies)}");

        for (int i = 0; i < regions.Length; i++)
        {
            var img = images[i];
            string file = $"{regions[i].Name}_{img.Width}x{img.Height}.bgra";
            File.WriteAllBytes(Path.Combine(dir, file), img.Bgra);
            int distinct = Enumerable.Range(0, img.Width * img.Height)
                .Select(p => BitConverter.ToInt32(img.Bgra, p * 4) & 0xFFFFFF).Distinct().Count();
            report.AppendLine($"region {regions[i].Name} {regions[i].Rect}: {distinct} distinct colours -> {file}");
        }
        var column = images[^1];
        report.AppendLine($"Letterbox.Viewport on the probe column (desktop, no bars expected): {Letterbox.Viewport(screen, column)}");
        File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static string Stats(List<double> v)
    {
        if (v.Count == 0) return "n/a";
        var s = v.Order().ToList();
        return $"min {s[0]:F2} median {s[s.Count / 2]:F2} max {s[^1]:F2} (n={s.Count})";
    }

    [LibraryImport("user32.dll")] private static partial IntPtr GetShellWindow();
}
