using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The change measure and threshold every <see cref="IconGate{T}"/> shares.</summary>
public static class IconGate
{
    /// <summary>Mean |Δ| (0–255) of the 10×10 BGR thumbnail. Measured on buffs (GAUGE_BUFF_CHANGE sweep, 6 videos and 5
    /// degraded copies): an unchanged icon moves 0.8–2.4 median from compression; a new icon 19.9 at the least (22.8 on the
    /// low-bitrate copy). 8 is 2.5× under the lowest real change; 24 already missed one.</summary>
    internal const double Threshold = 8;

    internal static double Change(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]);
        return s / a.Length;
    }

    /// <summary>The icon's interior lies inside <paramref name="area"/>: a thumbnail can be taken.</summary>
    internal static bool Inside(Image area, PixelRect icon)
    {
        var inner = BuffReader.Interior(icon);
        return inner.X >= 0 && inner.Y >= 0 && inner.Right <= area.Width && inner.Bottom <= area.Height && inner.Width > 0 && inner.Height > 0;
    }
}

/// <summary>Each icon box identified only when its icon moved (perf-hotspots.plan.md 3b for buffs, icon-gates.plan.md step 3
/// for 단심): the icon's thumbnail against the one last identified in that box, and <c>classify</c> only past
/// <see cref="IconGate.Threshold"/>. No timed re-check (user, 2026-11-09, status/decisions.md). Mutable: one per reading
/// thread and per row.</summary>
public sealed class IconGate<T>(Func<Image, PixelRect, T> classify)
{
    readonly record struct Box(PixelRect Icon, float[] Thumb, T Kind);
    Box?[] _boxes = [];

    /// <summary>How many times <c>classify</c> ran (tests).</summary>
    internal int Identifies { get; private set; }

    /// <summary>What box <paramref name="box"/>'s icon at <paramref name="icon"/> (relative to <paramref name="area"/>) holds:
    /// the last identified answer while the icon stays put, else identified again. A moved box (another size or HUD)
    /// re-identifies.</summary>
    public T Classify(Image area, int box, PixelRect icon)
    {
        if (box >= _boxes.Length) Array.Resize(ref _boxes, box + 1);
        if (!IconGate.Inside(area, icon)) return classify(area, icon);   // outside the capture: no thumbnail to keep
        Span<float> thumb = stackalloc float[BuffReader.ThumbLength];
        BuffReader.Thumb(area, BuffReader.Interior(icon), thumb);
        if (_boxes[box] is { } last && last.Icon == icon && IconGate.Change(thumb, last.Thumb) <= IconGate.Threshold) return last.Kind;
        Identifies++;
        var kind = classify(area, icon);
        _boxes[box] = new(icon, thumb.ToArray(), kind);
        return kind;
    }

    /// <summary>Box <paramref name="box"/> is identified again at its next ask (an answer its reader found doubtful).</summary>
    public void Forget(int box)
    {
        if (box < _boxes.Length) _boxes[box] = null;
    }

    /// <summary>Every box identified again: the image is prepared differently now (강화 읽기 switched, another source), which
    /// can keep the thumbnail within the threshold while the answer would differ.</summary>
    public void Reset() => _boxes = [];
}

/// <summary>For a row whose icons shift from slot to slot (the boss's debuffs come and go: on bb15 a slot's box moved a whole
/// slot, ±30–38 px, in 69% of the asks, icon-gates step 2): the last <c>capacity</c> icons identified, by their thumbnail
/// wherever their box was. A box whose thumbnail is within <see cref="IconGate.Threshold"/> of one of them holds the same icon
/// placed the same in its box, so it gets that answer (any position in it relative to the box); else identified. A "no
/// match" answer is kept too, for that exact look only (no timed re-check, status/decisions.md). Mutable and not
/// thread-safe: one per reading thread and per row, never shared.</summary>
public sealed class IconCache<T>(Func<Image, PixelRect, T> classify, int capacity)
{
    readonly List<(float[] Thumb, T Kind)> _seen = [];

    /// <summary>How many times <c>classify</c> ran (tests).</summary>
    internal int Identifies { get; private set; }

    /// <summary>Every icon identified again: the image is prepared differently now (강화 읽기 switched), as <see cref="IconGate{T}.Reset"/>.</summary>
    public void Reset() => _seen.Clear();

    /// <summary>What the icon at <paramref name="icon"/> (relative to <paramref name="area"/>) is: the closest remembered icon's
    /// answer within the threshold, else identified. Either way it becomes the newest; past the capacity the one seen least
    /// lately is forgotten.</summary>
    public T Classify(Image area, PixelRect icon)
    {
        if (!IconGate.Inside(area, icon)) return classify(area, icon);   // outside the capture: no thumbnail to compare
        Span<float> thumb = stackalloc float[BuffReader.ThumbLength];
        BuffReader.Thumb(area, BuffReader.Interior(icon), thumb);
        int best = -1;
        double least = IconGate.Threshold;
        for (int i = 0; i < _seen.Count; i++)
            if (IconGate.Change(thumb, _seen[i].Thumb) is var d && d <= least)
                (best, least) = (i, d);
        if (best >= 0)
        {
            var hit = _seen[best];
            _seen.RemoveAt(best);
            _seen.Add(hit);
            return hit.Kind;
        }
        Identifies++;
        var kind = classify(area, icon);
        if (_seen.Count >= capacity) _seen.RemoveAt(0);
        _seen.Add((thumb.ToArray(), kind));
        return kind;
    }
}
