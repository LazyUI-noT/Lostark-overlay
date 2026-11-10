using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>How the preview's gauge moves for one class: a click fills to the next of <paramref name="Splits"/> (or the
/// end), up to <see cref="Top"/>; once there, each click spends as the class does in game (<see cref="GaugeRule.AfterSpend"/>
/// with its first spend slot) while a spend is possible. A class with a spend rule stops at <see cref="RuleTop"/>, so the
/// leftover shows (user, 2026-11-10: 전태 a section, 해방자 its 80 %, 바드 every full section); one without fills to full,
/// then empties.</summary>
public sealed record GaugePlay(IReadOnlyList<double> Splits, GaugeRule? Rule = null)
{
    public const double RuleTop = 0.85;
    const double Eps = 1e-9;
    public static readonly GaugePlay Plain = new([]);

    SlotId? SpendSlot => Rule?.Spend.FirstOrDefault()?.Slots.FirstOrDefault();
    /// <summary><see cref="RuleTop"/> when a spend fires there; else full (the user's splits may leave no full section by
    /// 85 %), so the gauge never sticks.</summary>
    double Top => SpendSlot is { } slot && ClassSpend(slot, RuleTop) is not null ? RuleTop : 1;

    /// <summary>One fill click: up to the next section's end, no further than <see cref="Top"/>.</summary>
    public double Filled(double gauge) => Math.Min(Top, Splits.Append(1).FirstOrDefault(e => e > gauge + Eps, 1));

    public bool AtTop(double gauge) => gauge >= Top - Eps;

    /// <summary>What one spend leaves of <paramref name="gauge"/>; null: nothing to spend.</summary>
    public double? AfterSpend(double gauge) =>
        (SpendSlot is { } slot ? ClassSpend(slot, gauge) : null) ?? (gauge >= 1 - Eps ? 0 : null);

    double? ClassSpend(SlotId slot, double gauge) => (Rule! with { Splits = Splits }).AfterSpend(slot, gauge);

    /// <summary>The 표시 map's 아덴 GIF (user, 2026-11-10): a <see cref="LoopSeconds"/> loop that fills to <see cref="Top"/> in
    /// <see cref="FillSeconds"/>, then spends as the class does, one spend every <see cref="SpendEvery"/> from
    /// <see cref="FirstSpend"/> while one is possible, the leftover held to the loop's end.</summary>
    public double Looped(double seconds)
    {
        double t = seconds % LoopSeconds;
        if (t < FillSeconds) return Top * t / FillSeconds;
        double gauge = Top;
        for (double at = FirstSpend; at <= t + Eps && AfterSpend(gauge) is { } next && next < gauge - Eps; at += SpendEvery) gauge = next;
        return gauge;
    }

    public const double LoopSeconds = 4, FillSeconds = 0.5, FirstSpend = 1.25, SpendEvery = 0.75;
}

/// <summary>레이아웃 편집기의 ▶ 실행해 보기: a clicked box acts as if used, so its animations can be watched while its
/// options change. A ready box starts a <see cref="SkillLength"/> cooldown; a cooling one jumps to <see cref="NearEnd"/>
/// left (end flash, then the ready shine); a buff bar turns on for <see cref="BuffLength"/>, then jumps likewise; the
/// gauge fills a section per click, then is spent as the class does (<see cref="GaugePlay"/>); 변화 toggles. The debuff and 단심 windows, when the sample has
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
    bool _spending;
    bool _changed;

    public void Click(Part part, TimeSpan at, GaugePlay? gauge = null)
    {
        Settle(at);
        if (part == Part.게이지) StepGauge(gauge ?? GaugePlay.Plain);
        else if (part == Part.변화) _changed = !_changed;
        else if (part >= FreeLayout.FirstBuff)
        {
            var kind = (BuffKind)(part - FreeLayout.FirstBuff);
            _buffEnds[kind] = _buffEnds.TryGetValue(kind, out var end) ? Sooner(end, at) : at + BuffLength;
        }
        else
        {
            var slot = (SlotId)part;
            _cooling[slot] = _cooling.TryGetValue(slot, out var c) ? (Sooner(c.Ends, at), c.Total) : (at + SkillLength, SkillLength);
        }
        Settle(at);   // one ended by this click: ready (or off) from now
    }

    void StepGauge(GaugePlay g)
    {
        _gauge = _spending ? g.AfterSpend(_gauge) ?? _gauge : g.Filled(_gauge);
        _spending = _spending ? g.AfterSpend(_gauge) is not null : g.AtTop(_gauge);
    }

    /// <summary>A running box clicked: on to its last <see cref="NearEnd"/>; already in it, to its end (user, 2026-11-10: not
    /// the last second again).</summary>
    static TimeSpan Sooner(TimeSpan ends, TimeSpan at) => ends - at > NearEnd ? at + NearEnd : at;

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
    public void Auto(TimeSpan at, GaugePlay gauge)
    {
        if (_autoAt is not { } next || at - next > AutoCatchUp) next = at;
        for (; next <= at; next += AutoEvery)
        {
            var part = AutoOrder[_autoNext++ % AutoOrder.Length];
            Settle(next);
            bool busy = part >= FreeLayout.FirstBuff ? _buffEnds.ContainsKey((BuffKind)(part - FreeLayout.FirstBuff))
                : part != Part.게이지 && _cooling.ContainsKey((SlotId)part);
            if (!busy) Click(part, next, gauge);
        }
        _autoAt = next;
    }

    public void Reset(TimeSpan at)
    {
        (_start, _gauge, _spending, _changed, _autoAt, _autoNext) = (at - LongAgo, 0, false, false, null, 0);
        _ahead.Clear();
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

    static readonly TimeSpan DebuffLoop = TimeSpan.FromSeconds(30), JudgmentLoop = TimeSpan.FromSeconds(36),
        AmsuLoop = TimeSpan.FromSeconds(24), AmsuThrow = TimeSpan.FromSeconds(1);
    /// <summary>Where each loop's look changes (seconds in), so a click can jump to the next one.</summary>
    static readonly double[] DebuffPhases = [5, 11, 12, 15, 16, 28], JudgmentPhases = [1, 4, 10, 34], AmsuPhases = AmsuSteps();
    const double AmsuLastSeconds = 5;

    /// <summary>암수 by clicks (user, 2026-11-10): thrown (full), its last 5 s, gone, then thrown again: past the loop's end
    /// straight to the next throw, with no empty click between.</summary>
    static double[] AmsuSteps()
    {
        double thrown = AmsuThrow.TotalSeconds, length = Domain.Amsu.Duration(Vision.Bomb.암수);
        return [thrown, thrown + length - AmsuLastSeconds, thrown + length, AmsuLoop.TotalSeconds + thrown];
    }
    /// <summary>How far each loop was jumped ahead by clicks (<see cref="Skip"/>).</summary>
    readonly Dictionary<TimeSpan, TimeSpan> _ahead = [];

    /// <summary>A click on a looping window (디버프, 단심, 암수): it jumps to its next phase, wrapping to the loop's start.
    /// Other windows: nothing (their boxes take <see cref="Click"/>).</summary>
    public void Skip(OverlayPart window, TimeSpan at)
    {
        (TimeSpan length, double[] phases)? loop = window switch
        {
            OverlayPart.Debuffs => (DebuffLoop, DebuffPhases),
            OverlayPart.Judgment => (JudgmentLoop, JudgmentPhases),
            OverlayPart.Amsu => (AmsuLoop, AmsuPhases),
            _ => null,
        };
        if (loop is not var (length, phases)) return;
        var (t, _) = Loop(at, length);
        double step = ToNextPhase(t, phases, length.TotalSeconds);
        // Under a second to the next change: past it to the one after, so the click shows something new.
        if (step < NearEnd.TotalSeconds) step += ToNextPhase((t + step) % length.TotalSeconds, phases, length.TotalSeconds);
        _ahead[length] = _ahead.GetValueOrDefault(length) + TimeSpan.FromSeconds(step);
    }

    /// <summary>Seconds from <paramref name="t"/> to the first phase after it, or to the loop's end (its start again).</summary>
    public static double ToNextPhase(double t, IReadOnlyList<double> phases, double length) =>
        (phases.FirstOrDefault(p => p > t + 1e-6, length)) - t;

    /// <summary>Seconds into a loop of <paramref name="length"/> run from the start (or the last 처음으로), plus any jumps,
    /// and when that loop began.</summary>
    (double T, TimeSpan From) Loop(TimeSpan at, TimeSpan length)
    {
        var into = TimeSpan.FromTicks(Math.Max(0, (at + _ahead.GetValueOrDefault(length) - (_start + LongAgo)).Ticks) % length.Ticks);
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

    /// <summary>A 36 s loop: ready, 단죄 at 1 s (its 3 s), 심판 at 4 s (its 6 s, the 24 s cooldown), ready again at 34 s
    /// with the flash.</summary>
    JudgmentState Judgment(TimeSpan at)
    {
        var (t, s) = Loop(at, JudgmentLoop);
        var trigger = s + TimeSpan.FromSeconds(4);
        return t < 4 ? JudgmentState.Ready with { ConvictionAt = s + TimeSpan.FromSeconds(1) }
            : t < 34 ? new JudgmentState(trigger) : JudgmentState.Ready with { Previous = trigger };
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
