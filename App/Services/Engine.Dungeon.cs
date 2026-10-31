using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace GaugeOverlay.App.Services;

/// <summary>던전 알아보기 (spec § D): while the meter is on, the top-left panel is read every <see cref="DungeonEvery"/> with
/// Windows' own Korean OCR (on the thread pool, so the reads don't wait) and parsed by <see cref="DungeonPanel"/>. The last
/// panel seen (settled over reads by <see cref="DungeonPanel.Next"/>) is kept through a cutscene or the inventory over it, and dropped after <see cref="DungeonForget"/> without one.</summary>
public sealed partial class Engine
{
    static readonly TimeSpan DungeonEvery = TimeSpan.FromSeconds(3), DungeonForget = TimeSpan.FromSeconds(60);
    static readonly PixelRect RefDungeonPanel = new(0, 25, 450, 230);   // "N막 : name", [난이도], the gates, 재시작 (1080p, from the game area's top-left)
    static readonly Lazy<OcrEngine?> Ocr = new(() => OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ko")));
    TimeSpan _lastDungeon = TimeSpan.MinValue, _dungeonSeen;
    DungeonState _dungeonState = DungeonState.None;
    DungeonInfo? _dungeon => _dungeonState.Shown;
    int _dungeonBusy, _dungeonNew;

    /// <summary>Starts a read of the panel when one is due and none is running; true when a finished one changed what is known.</summary>
    bool StepDungeon(TimeSpan now)
    {
        if (_dungeon is not null && now - _dungeonSeen > DungeonForget) { _dungeonState = DungeonState.None; _dungeonNew = 1; }
        if (Ocr.Value is not null && Elapsed(now, _lastDungeon, DungeonEvery) && Interlocked.CompareExchange(ref _dungeonBusy, 1, 0) == 0)
        {
            _lastDungeon = now;
            double s = Geometry.Scale(_vp, _fit!);
            var r = new PixelRect(_vp.X, _vp.Y + (int)(RefDungeonPanel.Y * s), (int)(RefDungeonPanel.Width * s), (int)(RefDungeonPanel.Height * s));
            var img = new Image(r.Width, r.Height);
            if (_source!.TryCapture([r], [img], TimeSpan.FromMilliseconds(100), out _)) _ = Task.Run(() => ReadDungeon(img, now));
            else _dungeonBusy = 0;
        }
        return Interlocked.Exchange(ref _dungeonNew, 0) == 1;
    }

    // The dungeon panel and the boss name share the one OCR engine: one recognition at a time.
    static readonly SemaphoreSlim OcrGate = new(1, 1);

    static async Task<IReadOnlyList<string>> RecognizeLines(SoftwareBitmap bmp)
    {
        await OcrGate.WaitAsync();
        try { return [.. (await Ocr.Value!.RecognizeAsync(bmp)).Lines.Select(l => l.Text)]; }
        finally { OcrGate.Release(); }
    }

    async Task ReadDungeon(Image img, TimeSpan at)
    {
        long t0 = Stopwatch.GetTimestamp();
        try
        {
            var big = ImageOps.Resample(img, new PixelRect(0, 0, img.Width, img.Height), 2);   // OCR reads small Hangul far better at 2×
            using var bmp = SoftwareBitmap.CreateCopyFromBuffer(big.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, big.Width, big.Height, BitmapAlphaMode.Premultiplied);
            if (DungeonPanel.Parse(await RecognizeLines(bmp)) is not { } d) return;
            _dungeonSeen = at;
            var was = _dungeon;
            _dungeonState = DungeonPanel.Next(_dungeonState, d);   // the name read most wins over a misread
            if (_dungeon == was) return;
            Trace.WriteLine($"[dungeon] {_dungeon!.Kind} {_dungeon.Title} [{_dungeon.Difficulty}]");
            _dungeonNew = 1;
        }
        catch (Exception ex) { Trace.WriteLine($"[dungeon] OCR failed: {ex.Message}"); }
        finally { _dungeonBusy = 0; Perf.Add("던전 이름 (Windows OCR)", Stopwatch.GetTimestamp() - t0); }
    }
}
