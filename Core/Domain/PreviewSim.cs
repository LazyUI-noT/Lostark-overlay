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

    /// <summary>The order 자동 uses the boxes in: skills between buffs and gauge fills, one step each <see cref="AutoEvery"/>.</summary>
    static readonly Part[] AutoOrder =
    [
        Part.일반1, Part.버프공증, Part.일반2, Part.게이지, Part.일반3, Part.버프아덴, Part.일반4, Part.아덴1, Part.일반5,
        Part.버프초각, Part.일반6, Part.게이지, Part.일반7, Part.버프뎀증, Part.일반8, Part.아덴2, Part.각성기, Part.게이지, Part.초각스,
    ];
    public static readonly TimeSpan AutoEvery = TimeSpan.FromSeconds(0.8);
    static readonly TimeSpan AutoCatchUp = TimeSpan.FromSeconds(5);
    int _autoNext;
    TimeSpan? _autoAt;

    /// <summary>자동 (설정 → 표시, ▶ 시뮬레이션): the boxes are used in turn as if clicked, a box still cooling or a buff still on
    /// skipped. After a gap (the page hidden) it goes on from now instead of replaying the missed steps.</summary>
    public void Auto(TimeSpan at, int sections)
    {
        if (_autoAt is not { } next || at - next > AutoCatchUp) next = at;
        for (; next <= at; next += AutoEvery)
        {
            var part = AutoOrder[_autoNext++ % AutoOrder.Length];
            Settle(next);
            bool busy = part >= FreeLayout.FirstBuff ? _buffEnds.ContainsKey((BuffKind)(part - FreeLayout.FirstBuff))
                : part != Part.게이지 && _cooling.ContainsKey((SlotId)part);
            if (!busy) Click(part, next, sections);
        }
        _autoAt = next;
    }

    public void Reset(TimeSpan at)
    {
        (_start, _gauge, _changed, _autoAt, _autoNext) = (at - LongAgo, 0, false, null, 0);
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
            Debuffs = sample.Debuffs is null ? null : Debuffs(at), Judgment = sample.Judgment is null ? null : Judgment(at),
            Amsu = sample.Amsu is null ? null : Amsu(at) };
    }

    static readonly TimeSpan DebuffLoop = TimeSpan.FromSeconds(30), JudgmentLoop = TimeSpan.FromSeconds(34),
        AmsuLoop = TimeSpan.FromSeconds(24), AmsuThrow = TimeSpan.FromSeconds(1);

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

    /// <summary>A 24 s loop: none, 암수 thrown at 1 s (its 20 s, seen throughout), none again from 21 s.</summary>
    AmsuState Amsu(TimeSpan at)
    {
        var (_, s) = Loop(at, AmsuLoop);
        var thrown = s + AmsuThrow;
        var ends = thrown + TimeSpan.FromSeconds(Domain.Amsu.Duration(Vision.Bomb.암수));
        return at < thrown || at >= ends ? AmsuState.None
            : AmsuState.None with { Up = AmsuState.None.Up.Add(Vision.Bomb.암수, new BombTimer(ends, at)) };
    }

    /// <summary>Ends what has run out by <paramref name="at"/>, remembering when (a box's ready shine starts then).</summary>
    void Settle(TimeSpan at)
    {
        foreach (var (id, c) in _cooling.Where(c => c.Value.Ends <= at).ToList()) { _cooling.Remove(id); _readySince[id] = c.Ends; }
        foreach (var (k, end) in _buffEnds.Where(b => b.Value <= at).ToList()) { _buffEnds.Remove(k); _buffOff[k] = end; }
    }
}
