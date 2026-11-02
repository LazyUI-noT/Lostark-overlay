using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace GaugeOverlay.App.Services;

/// <summary>던전 알아보기 (spec § D): while the meter is on, the top-left panel is captured with the step's other reads, paced by
/// <see cref="DungeonPace"/> (3 s, 10 s once settled), and read with Windows' own Korean OCR on the thread pool, so the reads
/// don't wait; <see cref="DungeonPanel"/> parses it. The last panel seen (settled over reads by <see cref="DungeonPanel.Next"/>)
/// is kept through a cutscene or the inventory over it, and dropped after <see cref="DungeonForget"/> without one.</summary>
public sealed partial class Engine
{
    static readonly TimeSpan DungeonForget = TimeSpan.FromSeconds(60);
    static readonly PixelRect RefDungeonPanel = new(0, 25, 450, 230);   // "N막 : name", [난이도], the gates, 재시작 (1080p, from the game area's top-left)
    static readonly Lazy<OcrEngine?> Ocr = new(() => OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ko")));
    TimeSpan _lastDungeon = TimeSpan.MinValue, _dungeonSeen;
    DungeonState _dungeonState = DungeonState.None;
    DungeonPace _dungeonPace = DungeonPace.Start;   // stepped on the pool (ReadDungeon) and woken on the engine thread: under _paceLock
    readonly object _paceLock = new();
    DungeonInfo? _dungeon => _dungeonState.Shown;
    int _dungeonBusy, _dungeonNew;

    /// <summary>The HUD is back after a loading screen: maybe another dungeon, so read fast again.</summary>
    void WakeDungeon() { lock (_paceLock) _dungeonPace = _dungeonPace.Wake(); }

    /// <summary>True when a finished read changed what is known, or the panel was forgotten.</summary>
    bool DungeonChanged(TimeSpan now)
    {
        if (_dungeon is not null && now - _dungeonSeen > DungeonForget) { _dungeonState = DungeonState.None; _dungeonNew = 1; }
        return Interlocked.Exchange(ref _dungeonNew, 0) == 1;
    }

    /// <summary>The panel's rect when a read is due and none is running; captured with the step's other reads.</summary>
    PixelRect? DungeonRect(TimeSpan now)
    {
        // Only in a dungeon (the in-game meter, or a boss bar for players who hide it): 지옥 and the field show neither.
        if (Ocr.Value is null || !_presence.InDungeon(now) || !Elapsed(now, _lastDungeon, _dungeonPace.Every) || Volatile.Read(ref _dungeonBusy) == 1) return null;
        double s = Geometry.Scale(_vp, _fit!);
        var r = new PixelRect(_vp.X, _vp.Y + (int)(RefDungeonPanel.Y * s), (int)(RefDungeonPanel.Width * s), (int)(RefDungeonPanel.Height * s));
        return r.Inside(_vp) ? r : null;   // outside, it would fail the shared capture and every read in it
    }

    /// <summary>Starts the OCR of a captured panel (a copy: the capture buffer is reused next step).</summary>
    void StartDungeon(Image captured, TimeSpan t)
    {
        if (Interlocked.CompareExchange(ref _dungeonBusy, 1, 0) != 0) return;
        _lastDungeon = t;
        var img = captured.Copy();
        _ = Task.Run(() => ReadDungeon(img, t));
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
            var was = _dungeon;
            if (DungeonPanel.Parse(await RecognizeLines(bmp)) is { } d)
            {
                _dungeonSeen = at;
                _dungeonState = DungeonPanel.Next(_dungeonState, d);   // the name read most wins over a misread
            }
            lock (_paceLock) _dungeonPace = _dungeonPace.Read(_dungeon);   // the settled panel, so one misread or a cutscene doesn't speed it up
            if (_dungeon == was) return;
            Trace.WriteLine($"[dungeon] {_dungeon?.Kind} {_dungeon?.Title} [{_dungeon?.Difficulty}]");
            _dungeonNew = 1;
        }
        catch (Exception ex) { Trace.WriteLine($"[dungeon] OCR failed: {ex.Message}"); }
        finally { _dungeonBusy = 0; Perf.Add("던전 이름 (Windows OCR)", Stopwatch.GetTimestamp() - t0); }
    }
}
