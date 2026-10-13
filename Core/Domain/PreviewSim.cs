namespace GaugeOverlay.Core.Domain;

/// <summary>레이아웃 편집기의 ▶ 실행해 보기: a clicked box acts as if used, so its animations can be watched while its
/// options change. A ready box starts a <see cref="SkillLength"/> cooldown; a cooling one jumps to <see cref="NearEnd"/>
/// left (end flash, then the ready shine); a buff bar turns on for <see cref="BuffLength"/>, then jumps likewise; the
/// gauge fills a section per click and empties when full; 변화 toggles. The debuff and 단심 windows, when the sample has
/// them, play on loops (user, 2026-10-13). Everything else comes from the sample.</summary>
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
        return sample with { At = at, Slots = [.. slots], AwakeningChanged = _changed, Gauge = sample.Gauge is null ? null : _gauge, Buffs = [.. buffs],
            Debuffs = sample.Debuffs is null ? null : Debuffs(at), Judgment = sample.Judgment is null ? null : Judgment(at) };
    }

    static readonly TimeSpan DebuffLoop = TimeSpan.FromSeconds(30), JudgmentLoop = TimeSpan.FromSeconds(34);

    /// <summary>Seconds into a loop of <paramref name="length"/> run from the start (or the last 처음으로), and when that
    /// loop began.</summary>
    (double T, TimeSpan From) Loop(TimeSpan at, TimeSpan length)
    {
        var into = TimeSpan.FromTicks(Math.Max(0, (at - (_start + LongAgo)).Ticks) % length.Ticks);
        return (into.TotalSeconds, at - into);
    }

    /// <summary>A 30 s loop: red until 15 s, then blue; a swap at 12–15 s; and the box 번개 and 결투자 share takes turns: the
    /// lightning at 5–11 s, the 결투자 (its countdown, then the icon) at 16–28 s.</summary>
    DebuffStatus Debuffs(TimeSpan at)
    {
        var (t, s) = Loop(at, DebuffLoop);
        TimeSpan Sec(double x) => s + TimeSpan.FromSeconds(x);
        DebuffMark? On(double from, double length) => t >= from && t < from + length ? new(Sec(from), Sec(from + length)) : null;
        var colour = new DebuffMark(Sec(t < 15 ? 0 : 15), null);
        return new DebuffStatus(t < 15 ? null : colour, t < 15 ? colour : null, On(5, 6), On(12, 3), t < 15 ? 6 : 4, t < 15 ? 2 : 4,
            s, t is >= 16 and < 28 ? new DuelMark(Sec(16)) : null);
    }

    /// <summary>A 34 s loop: ready, 심판 at 2 s (its 6 s, the 24 s cooldown), ready again at 32 s with the flash.</summary>
    JudgmentState Judgment(TimeSpan at)
    {
        var (t, s) = Loop(at, JudgmentLoop);
        var trigger = s + TimeSpan.FromSeconds(2);
        return t < 2 ? JudgmentState.Ready : t < 32 ? new JudgmentState(trigger) : JudgmentState.Ready with { Previous = trigger };
    }

    /// <summary>Ends what has run out by <paramref name="at"/>, remembering when (a box's ready shine starts then).</summary>
    void Settle(TimeSpan at)
    {
        foreach (var (id, c) in _cooling.Where(c => c.Value.Ends <= at).ToList()) { _cooling.Remove(id); _readySince[id] = c.Ends; }
        foreach (var (k, end) in _buffEnds.Where(b => b.Value <= at).ToList()) { _buffEnds.Remove(k); _buffOff[k] = end; }
    }
}
