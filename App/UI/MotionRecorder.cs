using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary><c>--demo --motion=</c>: frames of one window while a motion plays, with nothing taken from the screen. Each frame is
/// <c>PrintWindow</c> of the window (the way shot.ps1 grabs one), stamped with its time from the moment the motion was
/// started; the stamps, not a fixed rate, decide the clip (<see cref="MotionFrames"/>). Writes <c>f0000.png…</c>,
/// <c>frames.txt</c>, <c>list.txt</c> (for ffmpeg's concat demuxer) and <c>sheet.png</c> (a contact sheet) into a folder.</summary>
sealed class MotionRecorder(Func<nint> window, Func<double?>? probe = null)
{
    [DllImport("user32.dll")] static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    const uint RenderFullContent = 2;

    Point _origin;
    readonly List<(double Ms, Bitmap Frame, double? Probe)> _frames = [];

    /// <summary>Grabs for <paramref name="beforeMs"/> (the still start), runs <paramref name="start"/> (time 0) and grabs on for
    /// <paramref name="afterMs"/> more. Each turn yields at Background priority, so the animation's tick has run first.</summary>
    public async Task Record(Dispatcher dispatcher, Action start, double beforeMs, double afterMs)
    {
        var clock = Stopwatch.StartNew();
        bool started = false;
        while (true)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            if (!started && now >= beforeMs) { started = true; start(); }
            if (started && now - beforeMs > afterMs) break;
            double at = clock.Elapsed.TotalMilliseconds;
            if (Grab() is { } frame) _frames.Add(((at + clock.Elapsed.TotalMilliseconds) / 2 - beforeMs, frame, probe?.Invoke()));   // stamped mid-grab
            await Task.Delay(1);   // lets WinForms timers (the tray fade) run: a bare yield loop starves WM_TIMER
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
    }

    Bitmap? Grab()
    {
        var hwnd = window();
        if (hwnd == 0 || !GetWindowRect(hwnd, out var r) || r.R <= r.L || r.B <= r.T) return null;
        _origin = new Point(r.L, r.T);
        var bitmap = new Bitmap(r.R - r.L, r.B - r.T);
        using var g = Graphics.FromImage(bitmap);
        var dc = g.GetHdc();
        try { PrintWindow(hwnd, dc, RenderFullContent); }
        finally { g.ReleaseHdc(dc); }
        return bitmap;
    }

    /// <summary>Writes the frames and the sheet; <paramref name="marksMs"/> are the moments the sheet shows.
    /// <paramref name="crop"/>: the part of each frame that matters (window pixels), null for all of it.</summary>
    public void Save(string folder, double slow, IReadOnlyList<double> marksMs, Rectangle? cropOnScreen, bool startAtFirstFrame = false)
    {
        Rectangle? crop = cropOnScreen is { } c ? new Rectangle(c.X - _origin.X, c.Y - _origin.Y, c.Width, c.Height) : null;   // screen pixels to the frame's
        double start = AnimationStart(startAtFirstFrame);
        marksMs = [.. marksMs.Select(m => m + start)];   // the marks count from the motion's first visible change, not from the call (the page builds first)
        Directory.CreateDirectory(folder);
        var names = new List<string>();
        var stamps = new List<double>();
        var lines = new List<string>();
        for (int i = 0; i < _frames.Count; i++)
        {
            var (ms, frame, value) = _frames[i];
            string name = $"f{i:0000}.png";
            using (var shown = Cropped(frame, crop)) shown.Save(Path.Combine(folder, name), ImageFormat.Png);
            names.Add(name);
            stamps.Add(ms);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{name} {ms:0.0} ms (motion {ms - start:0.0}){(value is { } v ? $" probe {v:0.000}" : "")}"));
        }
        File.WriteAllLines(Path.Combine(folder, "frames.txt"), lines);
        File.WriteAllText(Path.Combine(folder, "list.txt"), MotionFrames.ConcatList(names, stamps, slow, tailMs: 400));
        Sheet(Path.Combine(folder, "sheet.png"), stamps, marksMs, crop, start);
        foreach (var f in _frames) f.Frame.Dispose();   // the recording is over once saved
        _frames.Clear();
    }

    void Sheet(string path, IReadOnlyList<double> stamps, IReadOnlyList<double> marksMs, Rectangle? crop, double start)
    {
        if (_frames.Count == 0) return;
        const int Columns = 4, CellWidth = 560, Label = 22;
        var picks = MotionFrames.Nearest(stamps, marksMs);
        using var first = Cropped(_frames[0].Frame, crop);
        int cellHeight = first.Height * CellWidth / first.Width + Label;
        int rows = (picks.Length + Columns - 1) / Columns;
        using var sheet = new Bitmap(Columns * CellWidth, rows * cellHeight);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(24, 24, 24));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using var font = new Font("Segoe UI", 10f);
        for (int k = 0; k < picks.Length; k++)
        {
            var (ms, frame, value) = _frames[picks[k]];
            var (x, y) = (k % Columns * CellWidth, k / Columns * cellHeight);
            using var shown = Cropped(frame, crop);
            g.DrawImage(shown, new Rectangle(x, y + Label, CellWidth, cellHeight - Label));
            g.DrawString(string.Create(CultureInfo.InvariantCulture, $"motion +{marksMs[k] - start:0} ms  (frame at {ms - start:0.0}{(value is { } v ? $", probe {v:0.00}" : "")})"), font, Brushes.White, x + 4, y + 2);
        }
        sheet.Save(path, ImageFormat.Png);
    }

    /// <summary>When the motion began, on the frames' clock: the first frame whose probe differs from the first frame's (the
    /// probed value starts to move), or the first frame when the window only appears with the motion (the tray). 0 with no probe.</summary>
    double AnimationStart(bool fromFirstFrame)
    {
        if (fromFirstFrame) return _frames.Count == 0 ? 0 : _frames[0].Ms;
        if (_frames.FirstOrDefault(f => f.Probe is not null).Probe is not { } baseline) return 0;   // the first probed frame: the probe reads null until its element exists
        var moved = _frames.FirstOrDefault(f => f.Probe is { } p && Math.Abs(p - baseline) > 0.004);
        return moved.Frame is null ? 0 : moved.Ms;
    }

    static Bitmap Cropped(Bitmap frame, Rectangle? crop) =>
        crop is { } c ? frame.Clone(Rectangle.Intersect(c, new Rectangle(Point.Empty, frame.Size)), frame.PixelFormat) : (Bitmap)frame.Clone();
}
