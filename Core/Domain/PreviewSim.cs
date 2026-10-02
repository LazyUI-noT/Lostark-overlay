namespace GaugeOverlay.Core.Domain;

/// <summary>레이아웃 편집기의 ▶ 실행해 보기: a clicked box acts as if used, so its animations can be watched while its
/// options change. A ready box starts a <see cref="SkillLength"/> cooldown; a cooling one jumps to <see cref="NearEnd"/>
/// left (end flash, then the ready shine); a buff bar turns on for <see cref="BuffLength"/>, then jumps likewise; the
/// gauge fills a section per click and empties when full; 변화 toggles. Everything else comes from the sample.</summary>
public sealed class PreviewSim(Snapshot sample, TimeSpan now)
{
    public static readonly TimeSpan SkillLength = TimeSpan.FromSeconds(5), BuffLength = TimeSpan.FromSeconds(8),
        NearEnd = TimeSpan.FromSeconds(1);
    /// <summary>Well before any click, so nothing shines at the start.</summary>
    static readonly TimeSpan LongAgo = TimeSpan.FromMinutes(1);

    readonly Dictionary<SlotId, (TimeSpan Ends, TimeSpan Total)> _cooling = [];
    readonly Dictionary<BuffKind, TimeSpan> _buffEnds = [];
    readonly Dictionary<SlotId, TimeSpan> _readySince = [];
    readonly Dictionary<BuffKind, TimeSpan> _buffOff = [];
    TimeSpan _start = now - LongAgo;
    double _gauge;
    bool _changed;

    public void Click(Part part, TimeSpan at, int sections = 1)
    {
        Settle(at);
        if (part == Part.게이지) _gauge = _gauge >= 1 ? 0 : Math.Min(1, (Math.Floor(_gauge * sections + 1e-9) + 1) / sections);
        else if (part == Part.변화) _changed = !_changed;
        else if (part >= FreeLayout.FirstBuff)
        {
            var kind = (BuffKind)(part - FreeLayout.FirstBuff);
            _buffEnds[kind] = _buffEnds.ContainsKey(kind) ? at + NearEnd : at + BuffLength;
        }
        else
        {
            var slot = (SlotId)part;
            _cooling[slot] = _cooling.TryGetValue(slot, out var c) ? (at + NearEnd, c.Total) : (at + SkillLength, SkillLength);
        }
    }

    public void Reset(TimeSpan at)
    {
        (_start, _gauge, _changed) = (at - LongAgo, 0, false);
        _cooling.Clear(); _buffEnds.Clear(); _readySince.Clear(); _buffOff.Clear();
    }

    public Snapshot At(TimeSpan at)
    {
        Settle(at);
        var slots = SlotInfo.All.Select(id => _cooling.TryGetValue(id, out var c)
            ? new SlotState(id, SlotPhase.Cooldown, c.Ends, c.Total, false, c.Ends - c.Total)
            : new SlotState(id, SlotPhase.Ready, null, null, false, _readySince.GetValueOrDefault(id, _start)));
        var buffs = Enum.GetValues<BuffKind>().Select(k => _buffEnds.TryGetValue(k, out var end)
            ? new BuffState(k, true, end, BuffLength, false, end - BuffLength)
            : BuffState.Absent(k, _buffOff.GetValueOrDefault(k, _start)));
        return sample with { At = at, Slots = [.. slots], AwakeningChanged = _changed, Gauge = sample.Gauge is null ? null : _gauge, Buffs = [.. buffs] };
    }

    /// <summary>Ends what has run out by <paramref name="at"/>, remembering when (a box's ready shine starts then).</summary>
    void Settle(TimeSpan at)
    {
        foreach (var (id, c) in _cooling.Where(c => c.Value.Ends <= at).ToList()) { _cooling.Remove(id); _readySince[id] = c.Ends; }
        foreach (var (k, end) in _buffEnds.Where(b => b.Value <= at).ToList()) { _buffEnds.Remove(k); _buffOff[k] = end; }
    }
}
