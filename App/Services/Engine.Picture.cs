using System.Diagnostics;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>테스트 모드's game area: the video picture inside the picked window (PictureFinder), so a player in a normal window
/// works like the game's screen. Kept per window size relative to it (TestFits): a moved window takes it along at once.</summary>
public sealed partial class Engine
{
    /// <summary>테스트 모드 found no picture in the window and uses the whole of it; once per test session.</summary>
    public event Action? PictureNotFound;

    bool _pictureNoticed;   // engine thread only: PictureNotFound told in this test session

    /// <summary>Candidates tried with the fit solver per look: the best few (each is one FitSolver.Solve on a band).</summary>
    const int PictureTries = 4;

    /// <summary>The game area in <paramref name="client"/>: the kept picture when a fit locked it or it isn't time to look
    /// again; else looked for on a capture of the window, keeping the first candidate the skill bar is found on (locked) or
    /// the best one (tentative). Between looks or on a failed capture with nothing kept, the whole window for now (tentative,
    /// unsaved): reading never pauses for the look.</summary>
    Viewport TestArea(PixelRect client, TimeSpan now, bool fitting)
    {
        var size = (client.Width, client.Height);
        if (fitting) _testFits.DropPicture(size);   // 스킬창 맞추기 also looks for the picture again (theater ↔ normal view)
        var kept = _testFits.Picture(size);
        if (kept is { Locked: true } locked) return TestFits.Area(client, locked.Rel);
        var wholeArea = new Viewport(client.X, client.Y, client.Width, client.Height);
        if (!_testFits.ShouldFind(now)) return kept is { } k ? TestFits.Area(client, k.Rel) : wholeArea;

        var img = new Image(client.Width, client.Height);
        if (!_source!.TryCapture([client], [img], TimeSpan.FromMilliseconds(200), out _)) return kept is { } t ? TestFits.Area(client, t.Rel) : wholeArea;
        // ponytail: the finder runs on a half-size copy (thresholds measured at 1/4 of 1080p); a full-size pass if edges matter.
        var half = ImageOps.Resample(img, new(0, 0, img.Width, img.Height), 0.5);
        var found = PictureFinder.Candidates(half)
            .Select(r => Clamp(new PixelRect(r.X * 2, r.Y * 2, r.Width * 2, r.Height * 2), client)).ToList();
        if (found.Count == 0)
        {
            if (!_pictureNoticed) { _pictureNoticed = true; PictureNotFound?.Invoke(); }
            var whole = new PixelRect(0, 0, client.Width, client.Height);
            _testFits.SetPicture(size, whole, locked: false);
            return TestFits.Area(client, whole);
        }
        foreach (var rel in found.Take(PictureTries))
        {
            var vp = TestFits.Area(client, rel);
            var band = FitSolver.Band(vp);
            var inImage = new PixelRect(band.X - client.X, band.Y - client.Y, band.Width, band.Height);
            if (inImage.X < 0 || inImage.Y < 0 || inImage.Right > img.Width || inImage.Bottom > img.Height) continue;
            if (FitSolver.Solve(img.Crop(inImage), band, vp, video: true) is null) continue;
            _testFits.SetPicture(size, rel, locked: true);
            return vp;
        }
        _testFits.SetPicture(size, found[0], locked: false);
        return TestFits.Area(client, found[0]);
    }

    /// <summary>테스트 모드 settled the video's own 버프 크기 (BuffSizeFinder): the rows are read at it from now on.</summary>
    public event Action<int>? TestBuffSizeFound;

    static readonly TimeSpan BuffSizeEvery = TimeSpan.FromSeconds(1);
    TimeSpan _lastBuffSize = TimeSpan.MinValue;

    /// <summary>Once a second until it settles (or 위치 조정 sets it): the video's 버프 크기 from the buffs on screen, two or more
    /// at a time, the same size twice in a row (TestFits.NoteBuffSize). Not while 위치 조정 previews its own size.</summary>
    void StepTestBuffSize(TimeSpan now)
    {
        if (_testFits.BuffSizeKnown || _previewing || !Elapsed(now, _lastBuffSize, BuffSizeEvery)) return;
        _lastBuffSize = now;
        var rect = BuffSizeFinder.Area(_vp, _fit!);
        var img = Buffer(rect);
        if (!_source!.TryCapture([rect], [img], TimeSpan.FromMilliseconds(50), out _)) return;
        if (BuffSizeFinder.Find(img, rect, _vp, _fit!) is not { Matched: >= 2 } found || !_testFits.NoteBuffSize(found.Size)) return;
        Trace.WriteLine($"[engine] 테스트 모드: the video's 버프 크기 is {found.Size}% ({found.Matched} buffs)");
        TestBuffSizeFound?.Invoke(found.Size);
    }

    /// <summary><paramref name="r"/> (doubled from the half-size copy) kept inside the window.</summary>
    static PixelRect Clamp(PixelRect r, PixelRect client)
    {
        int w = Math.Min(r.Width, client.Width), h = Math.Min(r.Height, client.Height);
        return new(Math.Clamp(r.X, 0, client.Width - w), Math.Clamp(r.Y, 0, client.Height - h), w, h);
    }
}
