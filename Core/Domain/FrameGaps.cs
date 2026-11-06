namespace GaugeOverlay.Core.Domain;

/// <summary>What <see cref="FrameGaps"/> found: <paramref name="Count"/> gaps over <see cref="FrameGaps.Hitch"/> between
/// <paramref name="Frames"/> captured frames, the <paramref name="Worst"/> one with the app parts that ran in it (ms, most first).</summary>
public sealed record FrameGapSummary(int Frames, int Count, TimeSpan Worst, IReadOnlyList<(string Part, double Ms)> WorstParts);

/// <summary>Gaps between the game's captured frames (their present times, which capture already reports) and which app parts ran in
/// a long one. A gap cannot tell a slow game frame from a skipped capture. Any thread.</summary>
public sealed class FrameGaps
{
    /// <summary>A gap this long or longer is a hitch (about 6 frames at 60 fps).</summary>
    public static readonly TimeSpan Hitch = TimeSpan.FromMilliseconds(100);
    /// <summary>Longer than this the capture was not running (nothing to read, HUD hidden, a loading screen): not a gap.</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(2);
    const int PartsKept = 3;

    readonly object _gate = new();
    TimeSpan? _last;
    int _frames, _count;
    TimeSpan _worst;
    IReadOnlyList<(string Part, double Ms)> _worstParts = [];

    /// <param name="partMs">The parts that ended since the previous frame, in wall ms.</param>
    public void Add(TimeSpan frameTime, IReadOnlyDictionary<string, double> partMs)
    {
        lock (_gate)
        {
            _frames++;
            var gap = frameTime - (_last ?? frameTime);
            _last = frameTime;
            if (gap < Hitch || gap > Silence) return;
            _count++;
            if (gap <= _worst) return;
            _worst = gap;
            _worstParts = [.. partMs.Where(p => !ResourceCheck.IsWait(p.Key)).OrderByDescending(p => p.Value).Take(PartsKept).Select(p => (p.Key, p.Value))];
        }
    }

    public FrameGapSummary Summary()
    {
        lock (_gate) return new(_frames, _count, _worst, _worstParts);
    }
}
