namespace GaugeOverlay.Core.Domain;

/// <summary>테스트 모드's fits: the window in front is a video of the game, at whatever resolution and HUD size it was recorded
/// (a 4K recording on a 1080p screen is just another HUD scale), so the game's own saved fits don't apply. A fit is looked for
/// on the video and kept here, in memory only, per game-area size, until test mode ends. Engine thread only.</summary>
public sealed class TestFits
{
    /// <summary>How often a game area with no fit yet is tried again (the video's HUD may be hidden for a moment).</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(2);

    readonly Dictionary<Viewport, Fit> _fits = [];
    readonly Dictionary<Viewport, TimeSpan> _tried = [];   // per area, so one flickering between two isn't tried on every flip

    public Fit? For(Viewport vp) => _fits.GetValueOrDefault(vp);

    public void Set(Viewport vp, Fit fit) => _fits[vp] = fit;

    /// <summary>Test mode switched: nothing found on the last video applies.</summary>
    public void Clear()
    {
        _fits.Clear();
        _tried.Clear();
    }

    /// <summary>Whether to look for a fit on <paramref name="vp"/> now: at once for an area not tried yet, then every
    /// <see cref="Retry"/>; never for one that has a fit. True notes the try.</summary>
    public bool ShouldTry(Viewport vp, TimeSpan now)
    {
        if (_fits.ContainsKey(vp)) return false;
        if (_tried.TryGetValue(vp, out var at) && now - at < Retry) return false;
        _tried[vp] = now;
        return true;
    }
}
