namespace GaugeOverlay.Core.Domain;

/// <summary>One hitch: the gap and the app parts that ran in it (ms, most first).</summary>
public sealed record FrameGap(TimeSpan Gap, IReadOnlyList<(string Part, double Ms)> Parts)
{
    /// <summary>All the app's work in this gap (the kept parts), ms.</summary>
    public double AppMs => Parts.Sum(p => p.Ms);
}

/// <summary>What <see cref="FrameGaps"/> found: <paramref name="Count"/> gaps over <see cref="FrameGaps.Hitch"/> between
/// <paramref name="Frames"/> captured frames, the <see cref="FrameGaps.TopKept"/> longest in <paramref name="Top"/> (longest first).</summary>
public sealed record FrameGapSummary(int Frames, int Count, IReadOnlyList<FrameGap> Top)
{
    public TimeSpan Worst => Top.Count == 0 ? TimeSpan.Zero : Top[0].Gap;
    /// <summary>The app's average work over the kept gaps, ms (0 with none).</summary>
    public double AverageAppMs => Top.Count == 0 ? 0 : Top.Average(g => g.AppMs);
}

/// <summary>Gaps between the game's captured frames (their present times, which capture already reports) and which app parts ran in
/// a long one. A gap cannot tell a slow game frame from a skipped capture. Any thread.</summary>
public sealed class FrameGaps
{
    /// <summary>A gap this long or longer is a hitch (about 6 frames at 60 fps).</summary>
    public static readonly TimeSpan Hitch = TimeSpan.FromMilliseconds(100);
    /// <summary>Longer than this the capture was not running (nothing to read, HUD hidden, a loading screen): not a gap.</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(2);
    const int PartsKept = 3;
    public const int TopKept = 5;

    readonly object _gate = new();
    TimeSpan? _last;
    int _frames, _count;
    readonly List<FrameGap> _top = [];

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
            if (_top.Count == TopKept && gap <= _top[^1].Gap) return;
            _top.Add(new(gap, [.. partMs.Where(p => !ResourceCheck.IsWait(p.Key)).OrderByDescending(p => p.Value).Take(PartsKept).Select(p => (p.Key, p.Value))]));
            _top.Sort((a, b) => b.Gap.CompareTo(a.Gap));
            if (_top.Count > TopKept) _top.RemoveAt(TopKept);
        }
    }

    public FrameGapSummary Summary()
    {
        lock (_gate) return new(_frames, _count, [.. _top]);
    }
}
