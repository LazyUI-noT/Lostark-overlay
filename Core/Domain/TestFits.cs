namespace GaugeOverlay.Core.Domain;

/// <summary>테스트 모드's fits: the window in front is a video of the game, at whatever resolution and HUD size it was recorded
/// (a 4K recording on a 1080p screen is just another HUD scale), so the game's own saved fits don't apply. A fit is looked for
/// on the video and kept here, in memory only, per picture size (a moved window keeps its fit), until test mode ends. So is
/// where the picture lies in a player window (PictureFinder), per window size and relative to it: tentative (looked for again
/// every <see cref="Retry"/>) until a fit locks it. Engine thread only.</summary>
public sealed class TestFits
{
    /// <summary>How often a game area with no fit yet is tried again (the video's HUD may be hidden for a moment).</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(2);

    readonly Dictionary<Viewport, Fit> _fits = [];
    readonly Dictionary<Viewport, Fit> _before = [];       // what 되돌리기 puts back after a fit request's fine-tune
    readonly Dictionary<Viewport, TimeSpan> _tried = [];   // per area, so one flickering between two isn't tried on every flip

    readonly Dictionary<(int W, int H), (PixelRect Rel, bool Locked)> _pictures = [];
    TimeSpan? _lastLook;   // one clock for every window size: a drag of the window's edge isn't a new look per size

    /// <summary>Fits are kept by the picture's size: where it lies on screen doesn't change the HUD in it.</summary>
    static Viewport Key(Viewport vp) => vp with { X = 0, Y = 0 };

    public Fit? For(Viewport vp) => _fits.GetValueOrDefault(Key(vp));

    /// <summary>The video's 버프 크기 (null: 설정's), for this test session only: found on it (<see cref="NoteBuffSize"/>) or set
    /// by 위치 조정's 적용, which wins.</summary>
    public int? BuffSize { get; set; }

    /// <summary>The size is settled: nothing more to look for.</summary>
    public bool BuffSizeKnown => BuffSize is not null;

    int? _sizeSeen;   // the last size found, waiting for a second find to agree

    /// <summary>A size found on the video (BuffSizeFinder, two or more buffs); true when it settles it: the same size twice in
    /// a row. Ignored once the size is known.</summary>
    public bool NoteBuffSize(int size)
    {
        if (BuffSizeKnown) return false;
        if (_sizeSeen != size) { _sizeSeen = size; return false; }
        BuffSize = size;
        return true;
    }

    /// <summary>A fit put to use another way (a quiet try, 위치 조정's 적용): nothing to go back to.</summary>
    public void Set(Viewport vp, Fit fit)
    {
        _fits[Key(vp)] = fit;
        _before.Remove(Key(vp));
    }

    /// <summary>A fit request's fine-tuned fit; <paramref name="before"/> is what <see cref="Revert"/> puts back (null: nothing).</summary>
    public void SetFound(Viewport vp, Fit fit, Fit? before)
    {
        Set(vp, fit);
        if (before is not null) _before[Key(vp)] = before;
    }

    /// <summary>되돌리기: puts back and returns the fit kept by <see cref="SetFound"/>, once; null and no change when none.</summary>
    public Fit? Revert(Viewport vp)
    {
        if (!_before.Remove(Key(vp), out var before)) return null;
        _fits[Key(vp)] = before;
        return before;
    }

    /// <summary>"자동 맞춤 82.5% · +3, −1 px": what a fit request's fine-tune found.</summary>
    public static string Describe(Fit fit, double scale) => "자동 맞춤 " + Offsets(fit, scale);

    /// <summary>"82.5% · +3, −1 px": the HUD size and the bar's nudge in screen pixels (<paramref name="scale"/>: Geometry.Scale).</summary>
    public static string Offsets(Fit fit, double scale)
    {
        static string Signed(double v) => (int)Math.Round(v) is var n && n < 0 ? $"−{-n}" : $"+{n}";
        return $"{fit.Hud:0.#}% · {Signed(fit.NudgeX * scale)}, {Signed(fit.NudgeY * scale)} px";
    }

    /// <summary>Test mode switched: nothing found on the last video applies.</summary>
    public void Clear()
    {
        _fits.Clear();
        _before.Clear();
        _tried.Clear();
        _pictures.Clear();
        _lastLook = null;
        (BuffSize, _sizeSeen) = (null, null);
    }

    /// <summary>The picture in a window of this size, relative to its client area, and whether a fit has locked it.</summary>
    public (PixelRect Rel, bool Locked)? Picture((int W, int H) window) => _pictures.TryGetValue(window, out var p) ? p : null;

    public void SetPicture((int W, int H) window, PixelRect rel, bool locked) => _pictures[window] = (rel, locked);

    /// <summary>A fit was found on the picture: it stays where it is (player controls or a dark scene can't move it).</summary>
    public void Lock((int W, int H) window)
    {
        if (_pictures.TryGetValue(window, out var p)) _pictures[window] = p with { Locked = true };
    }

    /// <summary>스킬창 맞추기: the picture is looked for again at once.</summary>
    public void DropPicture((int W, int H) window)
    {
        _pictures.Remove(window);
        _lastLook = null;
    }

    /// <summary>Whether to look for the picture now: at once, then at most every <see cref="Retry"/> whatever the window's size.</summary>
    public bool ShouldFind(TimeSpan now)
    {
        if (_lastLook is { } at && now - at < Retry) return false;
        _lastLook = now;
        return true;
    }

    /// <summary>The game area on screen: <paramref name="rel"/> inside <paramref name="client"/>.</summary>
    public static Viewport Area(PixelRect client, PixelRect rel) => new(client.X + rel.X, client.Y + rel.Y, rel.Width, rel.Height);

    /// <summary>Whether to look for a fit on <paramref name="vp"/> now: at once for an area not tried yet, then every
    /// <see cref="Retry"/>; never for one that has a fit. True notes the try.</summary>
    public bool ShouldTry(Viewport vp, TimeSpan now)
    {
        if (_fits.ContainsKey(Key(vp))) return false;
        if (_tried.TryGetValue(Key(vp), out var at) && now - at < Retry) return false;
        _tried[Key(vp)] = now;
        return true;
    }
}
