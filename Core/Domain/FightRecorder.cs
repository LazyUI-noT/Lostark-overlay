namespace GaugeOverlay.Core.Domain;

/// <summary>Builds the record of the fight the meter follows (spec § D). <see cref="ObserveMine"/> turns my snapshots into events:
/// a slot going ready → cooldown is a cast, my 아덴 피증 / 초각스 피증 turning on or off is a buff window (the party's figures
/// come with <see cref="Step"/>; their casts are not recorded).
/// Event times are on the fight clock: the 전분 timer plus the capture clock since it last moved. They are held until
/// <see cref="Step"/> sees the timer move past them, and dropped if it stops first. Once a second, while the timer runs,
/// <see cref="Step"/> adds everyone's figures and the boss. A fight starts only while <see cref="InRaid"/> (the dungeon panel
/// says raid), and ends: on a clear (the timer stops with the boss under <see cref="ClearUnder"/>), when a new 전분 record
/// starts (a restart), when the timer hasn't moved for <see cref="PauseLimit"/> (stopped by a wipe, or hidden by a cutscene or
/// the inventory: a shorter pause resumes the same record), at the size cap, or on <see cref="Finish"/>. Mutable; one thread
/// (the UI's).</summary>
public sealed class FightRecorder(int maxEntries = FightRecorder.MaxEntries)
{
    public const int MaxEntries = 200_000;
    public static readonly TimeSpan EndAfterFrozen = TimeSpan.FromSeconds(120);   // PauseLimit's default
    public const long ClearUnder = 10_000;

    /// <summary>The dungeon panel says raid: only then does a fight start (set by the app each second).</summary>
    public bool InRaid { get; set; } = true;
    /// <summary>How long the timer may stay stopped or hidden before the fight ends (설정 → 전투 기록).</summary>
    public TimeSpan PauseLimit { get; set; } = EndAfterFrozen;
    static readonly BuffKind[] Windows = [BuffKind.공증, BuffKind.아덴피증, BuffKind.초각스피증];

    /// <summary>암흑수류탄 is read (its switch): only then are the bombs recorded, as one window "a bomb is up" with the bomb shown.</summary>
    public bool AmsuOn { get; set; }

    public string Me { get; set; } = "";
    public int MyClass { get; set; }
    public string? MySubclass { get; set; }

    SlotPhase[]? _slots;   // my slots as last seen
    readonly bool[] _buffs = new bool[Enum.GetValues<BuffKind>().Length];   // as last seen
    readonly bool[] _committed = new bool[Enum.GetValues<BuffKind>().Length];   // as the record has them
    int? _bomb, _bombCommitted;   // the bomb shown (Vision.Bomb), as last seen and as the record has it
    readonly List<(TimeSpan At, RecordEvent Event)> _pending = [];
    FightClockState _clock = FightClockState.None;
    bool _started, _stopped;
    DateTime _startedAt;
    string? _gate;
    List<RecordEvent> _events = [];
    List<RecordSample> _samples = [];
    List<RecordBoss> _boss = [];
    List<double> _phases = [];
    Dictionary<string, RecordMember> _members = [];

    /// <summary>My own snapshot: casts and buff windows in my lane (<see cref="Me"/>).</summary>
    public void ObserveMine(Snapshot snap, TimeSpan now)
    {
        Casts(snap, now);
        Bombs(snap, now);   // before the buffs' guard: with every buff bar hidden the snapshot has no buffs, but the bombs still count
        if (snap.Buffs.Length != _buffs.Length) return;
        foreach (var kind in Windows)
        {
            bool on = snap.Buffs[(int)kind].Active;
            if (on != _buffs[(int)kind]) Hold(now, Me, on ? RecordEventKind.버프시작 : RecordEventKind.버프끝, (int)kind);
            _buffs[(int)kind] = on;
        }
    }

    void Bombs(Snapshot snap, TimeSpan now)
    {
        int? bomb = AmsuOn && snap.Amsu is { } a && Amsu.View(a, now).Shown is { } shown ? (int)shown : null;
        if (bomb == _bomb) return;
        if (_bomb is { } was) Hold(now, Me, RecordEventKind.암수끝, was);
        if (bomb is { } b) Hold(now, Me, RecordEventKind.암수시작, b);
        _bomb = bomb;
    }

    static bool IsBuff(RecordEventKind k) => k is RecordEventKind.버프시작 or RecordEventKind.버프끝;
    static bool IsBomb(RecordEventKind k) => k is RecordEventKind.암수시작 or RecordEventKind.암수끝;

    void Casts(Snapshot snap, TimeSpan now)
    {
        var phases = snap.Slots.Select(s => s.Phase).ToArray();
        if (_slots is { } was && was.Length == phases.Length)
            for (int i = 0; i < phases.Length; i++)
                if (was[i] == SlotPhase.Ready && phases[i] is SlotPhase.Cooldown or SlotPhase.LongCooldown)
                    Hold(now, Me, RecordEventKind.사용, i);
        _slots = phases;
    }

    void Hold(TimeSpan now, string name, RecordEventKind kind, int what)
    {
        // Not while stopped, and not once the timer has gone unread for a stop's length (the panel covered): the event's
        // second would be a guess.
        if (!_started || _stopped || _clock.Timer is not { } t || now - _clock.MovedAt >= FightClock.FrozenAfter) return;
        double into = Math.Clamp((now - _clock.MovedAt).TotalSeconds, 0, 0.99);
        _pending.Add((now, new RecordEvent(Math.Round(t + into, 2), name, kind, what)));
    }

    /// <summary>The meter's second. Returns the fight that just ended, if one did.</summary>
    public FightRecord? Step(MeterStatus status, IReadOnlyList<MeterSample> samples, IReadOnlyList<RecordMember> members, long bossTaken,
        string? gate, TimeSpan now, DateTime wall)
    {
        var c = status.Clock;
        var was = _clock;
        _clock = c;
        FightRecord? done = null;
        if (c.Fight != was.Fight) { done = Finish(); return done; }   // a new 전분 record: wait for its timer to move
        if (c.Timer is not { } t || was.Timer is not { } before) return null;
        if (t > before)
        {
            if (!_started && !InRaid) return null;
            if (_stopped && _started) _phases.Add(before);   // a stop before the fight (the preparation) is no phase
            if (!_started) (_started, _startedAt, _gate) = (true, wall, gate);
            _stopped = false;
            foreach (var (_, e) in _pending.Where(p => p.At < c.MovedAt)) Commit(e);
            _pending.RemoveAll(p => p.At < c.MovedAt);
            // A buff window is a state: a change no event carries (made while nothing was held: stopped, not started, or the
            // timer hidden by a cutscene or the inventory) goes at the last second known; changes still waiting for their
            // second count as made.
            var expected = (bool[])_committed.Clone();
            foreach (var (_, e) in _pending.Where(p => IsBuff(p.Event.Kind))) expected[e.What] = e.Kind == RecordEventKind.버프시작;
            foreach (var kind in Windows.Where(k => _buffs[(int)k] != expected[(int)k]))
                Commit(new RecordEvent(before, Me, _buffs[(int)kind] ? RecordEventKind.버프시작 : RecordEventKind.버프끝, (int)kind));
            int? bombExpected = _bombCommitted;
            foreach (var (_, e) in _pending.Where(p => IsBomb(p.Event.Kind))) bombExpected = e.Kind == RecordEventKind.암수시작 ? e.What : null;
            if (_bomb != bombExpected)   // the bomb window too is a state
            {
                if (bombExpected is { } ended) Commit(new RecordEvent(before, Me, RecordEventKind.암수끝, ended));
                if (_bomb is { } started) Commit(new RecordEvent(before, Me, RecordEventKind.암수시작, started));
            }
            _gate ??= gate;
            foreach (var m in members) _members[m.Name] = m;
            foreach (var s in samples)
            {
                _members[s.Name] = new RecordMember(s.Name, s.ClassId, s.Role, s.Party);
                _samples.Add(new RecordSample(t, s.Name, s.Damage, s.Dps));
            }
            _boss.Add(new RecordBoss(t, bossTaken, status.Boss.Last, status.Boss.Max));
            return _events.Count + _samples.Count >= maxEntries ? Finish() : null;
        }
        if (c.Frozen && !_stopped) { _stopped = true; _pending.Clear(); }
        if (_started && c.Frozen && status.Boss.Last < ClearUnder) return Finish();   // cleared: the boss sits at 1 HP when it stops
        return _started && now - c.MovedAt >= PauseLimit ? Finish() : null;
    }

    void Commit(RecordEvent e)
    {
        _events.Add(e);
        if (IsBuff(e.Kind)) _committed[e.What] = e.Kind == RecordEventKind.버프시작;
        else if (IsBomb(e.Kind)) _bombCommitted = e.Kind == RecordEventKind.암수시작 ? e.What : null;
    }

    /// <summary>Ends the fight being recorded (the meter turned off, the app closing); null when none was running. Events not
    /// yet confirmed by the timer are dropped.</summary>
    public FightRecord? Finish()
    {
        _pending.Clear();
        _stopped = false;
        if (!_started) return null;
        _started = false;
        Array.Clear(_committed);
        _bombCommitted = null;
        bool? cleared = _boss.LastOrDefault(b => b.Current is not null)?.Current is { } hp ? hp < ClearUnder : null;
        var r = new FightRecord(_startedAt, _gate, _boss.Count > 0 ? _boss[^1].T : 0, _phases, Me, MyClass, MySubclass,
            [.. _members.Values], _samples, _boss, _events, Cleared: cleared, AmsuOn: AmsuOn);
        (_events, _samples, _boss, _phases, _members, _gate) = ([], [], [], [], [], null);
        return r;
    }
}
