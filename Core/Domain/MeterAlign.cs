namespace GaugeOverlay.Core.Domain;

/// <summary>Lines the meter's samples up on one 전분 second, so a member's figure that arrives late (network, the host's relay)
/// is not compared with a newer one from someone else. Each second it keeps the last few samples per member and takes everyone
/// at the newest second all sharers have reached (members more than <see cref="LagLimit"/> seconds behind the newest don't hold
/// the others back). The second comes from the shared samples only, so every PC picks the same one; my own figures (from this
/// PC) are taken at that second too. The meter shows about a second behind live in exchange. One per PC, UI thread.</summary>
public sealed class MeterAlign
{
    public const int Keep = 6;
    public const int LagLimit = 3;
    readonly Dictionary<ulong, List<MeterSample>> _seen = [];

    public (IReadOnlyList<MeterSample> Shared, MeterSample? Mine) Align(IReadOnlyList<MeterSample> shared, MeterSample? mine)
    {
        var now = shared.Append(mine).OfType<MeterSample>().ToList();
        foreach (var gone in _seen.Keys.Where(id => now.All(s => s.Id != id)).ToList()) _seen.Remove(gone);
        foreach (var s in now) Remember(s);

        var timers = (shared.Count > 0 ? shared : now).Select(s => s.Timer).OfType<int>().ToList();
        if (timers.Count == 0) return (shared, mine);
        int newest = timers.Max(), second = timers.Where(t => t >= newest - LagLimit).Min();
        return ([.. shared.Select(s => At(s, second))], mine is null ? null : At(mine, second));
    }

    void Remember(MeterSample s)
    {
        if (s.Timer is not { } t) return;
        if (!_seen.TryGetValue(s.Id, out var list)) _seen[s.Id] = list = [];
        if (list.Count > 0 && t < list[^1].Timer) list.Clear();   // a new 전분 record
        if (list.Count > 0 && t == list[^1].Timer) list[^1] = s;
        else list.Add(s);
        if (list.Count > Keep) list.RemoveAt(0);
    }

    /// <summary>The member's sample at <paramref name="second"/> (the last at or before it), or the latest when none is.</summary>
    MeterSample At(MeterSample s, int second) =>
        s.Timer is null ? s : _seen.GetValueOrDefault(s.Id)?.LastOrDefault(x => x.Timer <= second) ?? s;
}
