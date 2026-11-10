using System.Collections.Immutable;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>Fake snapshot source for --demo and the edit-mode sample. A 30 s script loops: cooldowns of
/// several lengths, a reduction, a reset (333 E → 아덴), a stack skill with a use left, a 3m long cooldown,
/// estimated, unknown, 초각기 표시, a gold frame on E and R from 12 to 18 s, a moving gauge and two peers. From 23 s to 30 s nothing moves, so the render loop can idle.</summary>
public sealed class DemoSnapshots
{
    const double Loop = 30;
    /// <summary>--meter-skill=name: the meter's 스킬 사용 횟수 line ("none": no data).</summary>
    public static MeterSkillLine? MeterSkillShown { get; set; }
    static readonly SlotId[] Aden = [SlotId.아덴2];
    readonly TimeSpan _origin = Clock.Now();
    readonly bool _duelist;
    readonly (double Left, bool Seen)? _amsu;   // --amsu: a bomb held at this many seconds
    Snapshot? _last;
    Snapshot[]? _lastPeers;

    /// <param name="duelist">--duelist: the debuff window shows the 결투자 from the start of each loop instead of the lightning.</param>
    public DemoSnapshots(bool duelist = false, (double Left, bool Seen)? amsu = null) => (_duelist, _amsu) = (duelist, amsu);

    /// <summary>암수 on the boss with <paramref name="left"/> s to go at <paramref name="at"/>; <paramref name="seen"/> false: its
    /// icon last seen 5 s ago (pushed out of the row, counting on).</summary>
    public static AmsuState DemoAmsu(TimeSpan at, double left, bool seen) =>
        AmsuState.None with { Up = AmsuState.None.Up.Add(Core.Vision.Bomb.암수, new BombTimer(at + Sec(left), seen ? at : at - Sec(5))) };

    /// <summary>True when the script changed since the last call; then returns the new state.</summary>
    public bool TryNext(out Player self, out IReadOnlyList<Player> peers)
    {
        var now = Clock.Now();
        double cycles = Math.Floor((now - _origin).TotalSeconds / Loop);
        var start = _origin + TimeSpan.FromSeconds(cycles * Loop);
        double t = (now - start).TotalSeconds;
        var me = Self(start, t, _duelist);
        if (_amsu is { } a) me = me with { Amsu = DemoAmsu(me.At, a.Left, a.Seen) };
        self = new Player("", me);
        peers = [new("민수", Minsu(start, t), MinsuLook), new("지현가나다라마바사", Jihyun(start, t))];   // 지현 sent none
        bool changed = _last is null || !Same(_last, me) || _lastPeers is null || !_lastPeers.Zip(peers).All(p => Same(p.First, p.Second.Snapshot));
        _last = me;
        _lastPeers = [.. peers.Select(p => p.Snapshot)];
        return changed;
    }

    /// <summary>A 공대 of eight sharing (two parties of three dealers and a support), <paramref name="timer"/> seconds into the
    /// record, with the minute and a half before it as history; "나" is id 1.</summary>
    internal static MeterView DemoMeter(int timer, MeterScope scope = MeterScope.공대)
    {
        (string Name, int Class, MeterRole Role, int Party, double Share)[] who =
        [
            ("나", 0, MeterRole.딜러, 1, 0.27), ("민수", 0, MeterRole.딜러, 1, 0.22), ("지현", 0, MeterRole.딜러, 1, 0.19), ("하나", 0, MeterRole.서포터, 1, 0.31),
            ("철수", 0, MeterRole.딜러, 2, 0.12), ("영희", 0, MeterRole.딜러, 2, 0.09), ("수진", 0, MeterRole.딜러, 2, 0.06), ("도윤", 0, MeterRole.서포터, 2, 0.14),
        ];
        const long Boss = 4_200_000_000_000;
        List<MeterSample> At(int t) => [.. who.Select((w, i) =>
        {
            double wobble = 1 + 0.08 * Math.Sin(t / 7.0 + i);
            long damage = (long)(Boss * t / timer * w.Share * wobble);
            return new MeterSample((ulong)i + 1, w.Name, w.Class, w.Role, w.Party, damage, damage / Math.Max(1, t), t, true, 1, Boss * t / timer);
        })];
        var history = new MeterHistory();
        for (int t = Math.Max(1, timer - 90); t < timer; t++)
            history.Add(Sec(t), 1, MeterMath.Rows(At(t), 1, 1, scope, null));
        var rows = MeterMath.Rows(At(timer), 1, 1, scope, history.SharesAt(Sec(timer - 10)));
        history.Add(Sec(timer), 1, rows);
        // 버프 분배 as a support would see it (the dealers only; "나" is a dealer here, so it is only for the look)
        IReadOnlyList<(ulong, string, double)> Split(params double[] s) => [(2, "민수", s[0]), (3, "지현", s[1]), (5, "철수", s[2])];
        return new(rows, scope == MeterScope.나 ? null : MeterMath.Other(rows, Boss), Boss, timer, true, true, history, [0.97, 0.80, 0.42],
            BuffSplit: [Split(0.42, 0.31, 0.27), Split(0.38, 0.36, 0.26), Split(0.29, 0.51, 0.20)], Skill: MeterSkillShown,
            BossName: "고통의 마녀, 세르카 [나이트메어]",   // a long name: the header shortens it before the 비교 label
            Ghost: new([.. Enumerable.Range(1, timer).Select(t => (Sec(t), (long)(Boss * 0.27 / timer * (0.9 + 0.1 * Math.Sin(t / 5.0)))))], MeterCompare.Kind.자동));   // 비교: an earlier fight, a little behind
    }

    /// <summary>Two 전투 기록 fights for --records: four players, a phase change, my casts and buff windows; the second a
    /// little faster.</summary>
    public static IReadOnlyList<FightRecord> DemoRecords()
    {
        (string Name, int Class, MeterRole Role, double Share)[] who =
            [("나", 0, MeterRole.딜러, 0.34), ("민수", 0, MeterRole.딜러, 0.27), ("지현", 0, MeterRole.딜러, 0.22), ("하나", 0, MeterRole.서포터, 0.30)];
        FightRecord Fight(int minute, double length, double phase, double pace)
        {
            const long Boss = 3_000_000_000_000;
            var samples = new List<RecordSample>();
            var boss = new List<RecordBoss>();
            for (int t = 1; t <= length; t++)
            {
                foreach (var (w, i) in who.Select((w, i) => (w, i)))
                {
                    double wobble = 1 + 0.25 * Math.Sin(t / 9.0 + i) * (t > phase - 8 && t < phase ? 0.2 : 1);
                    long damage = (long)(Boss * pace * t / length * w.Share);
                    samples.Add(new(t, w.Name, damage, (long)(Boss * pace / length * w.Share * wobble)));
                }
                boss.Add(new(t, (long)(Boss * pace * t / length), null, null));
            }
            var events = new List<RecordEvent>();
            foreach (var (cd, i) in new[] { 6.0, 9, 12, 15, 20, 24 }.Select((cd, i) => (cd, i)))   // my skills, each on its cooldown
                for (double t = 1 + i * 0.6; t < length; t += cd + i % 3 * 0.4)
                    if (Math.Abs(t - phase) > 4) events.Add(new(Math.Round(t, 1), "나", RecordEventKind.사용, (int)SlotId.일반1 + i));
            events.Add(new(phase + 20, "나", RecordEventKind.사용, (int)SlotId.각성기));
            for (double t = 4; t < length; t += 18)
            {
                events.Add(new(t, "나", RecordEventKind.버프시작, (int)BuffKind.공증));
                events.Add(new(t + 9, "나", RecordEventKind.버프끝, (int)BuffKind.공증));
            }
            for (double t = 6; t < length; t += 30)
            {
                events.Add(new(t, "나", RecordEventKind.버프시작, (int)BuffKind.아덴피증));
                events.Add(new(t + 8, "나", RecordEventKind.버프끝, (int)BuffKind.아덴피증));
                events.Add(new(t + 2, "나", RecordEventKind.사용, (int)SlotId.초각스));
                events.Add(new(t + 2.1, "나", RecordEventKind.버프시작, (int)BuffKind.초각스피증));
                events.Add(new(t + 12, "나", RecordEventKind.버프끝, (int)BuffKind.초각스피증));
                events.Add(new(t + 3, "나", RecordEventKind.암수시작, (int)(t % 60 < 30 ? Core.Vision.Bomb.암수 : Core.Vision.Bomb.빛암)));   // a bomb up 3–21 s of each 30
                events.Add(new(t + 21, "나", RecordEventKind.암수끝, (int)(t % 60 < 30 ? Core.Vision.Bomb.암수 : Core.Vision.Bomb.빛암)));
            }
            return new(new DateTime(2026, 10, 30, 21, minute, 0), "3막 · 3관문", length, [phase], "나", 0, null,
                [.. who.Select(w => new RecordMember(w.Name, w.Class, w.Role, 1))], samples, boss, [.. events.OrderBy(e => e.T)]);
        }
        // The first with the API's item level and 전투력 (lostark-api.plan.md phase 5), the second as an older record without.
        return [Fight(4, 182, 95, 1.0) with { AmsuOn = true, ItemLevel = 1791.66, CombatPower = 5004.5, Character = "데모캐릭", Difficulty = "하드" },
            Fight(40, 168, 88, 1.08) with { Kept = true, Character = "데모캐릭", Difficulty = "하드" }];   // a character: 「미터기 비교」 shows
    }

    static bool Same(Snapshot a, Snapshot b) =>
        a.Slots.SequenceEqual(b.Slots) && a.AwakeningChanged == b.AwakeningChanged && a.Gauge == b.Gauge && a.Groups.SetEquals(b.Groups)
        && a.Buffs.SequenceEqual(b.Buffs) && a.Debuffs == b.Debuffs && a.Judgment == b.Judgment && a.Raid == b.Raid && a.Amsu == b.Amsu;

    /// <summary>The buff bars: 뎀증 from 0 s (20 s), 공증 from 1 s (8 s), 아덴피증 from 3 s (12 s) and 초각스피증 from
    /// 5 s (10 s); absent otherwise.</summary>
    static ImmutableArray<BuffState> Buffs(TimeSpan s, double t) =>
    [
        .. new[] { (BuffKind.공증, 1.0, 8.0), (BuffKind.아덴피증, 3.0, 12.0), (BuffKind.초각스피증, 5.0, 10.0), (BuffKind.뎀증, 0.0, 20.0) }
            .Select(b => t >= b.Item2 && t < b.Item2 + b.Item3
                ? new BuffState(b.Item1, true, s + Sec(b.Item2 + b.Item3), Sec(b.Item3), false, s + Sec(b.Item2))
                : BuffState.Absent(b.Item1, s)),
    ];

    static Snapshot Self(TimeSpan s, double t, bool duelist = false)
    {
        SlotState Rdy(SlotId id, double at) => new(id, SlotPhase.Ready, null, null, false, s + Sec(at));
        SlotState Cd(SlotId id, double cast, double total, double? endsAt = null, bool est = false) =>
            new(id, SlotPhase.Cooldown, s + Sec(endsAt ?? cast + total), Sec(total), est, s + Sec(cast));

        SlotState State(SlotId id) => id switch
        {
            SlotId.각성기 => new(id, SlotPhase.LongCooldown, null, null, false, s, Minutes: 3),
            SlotId.초각스 => t < 12 ? Cd(id, -30, 42) : Rdy(id, 12),
            SlotId.일반1 => t < 6 ? Cd(id, 0, 6) : t < 8 ? Rdy(id, 6) : t < 14 ? Cd(id, 8, 6) : Rdy(id, 14),
            SlotId.일반2 => t < 5 ? Cd(id, 0, 14) : t < 10 ? Cd(id, 0, 14, endsAt: 10) : Rdy(id, 10),   // reduction at 5 s
            SlotId.일반3 => t < 2 ? Rdy(id, 0) : t < 11 ? Cd(id, 2, 9) with { Stacked = t < 6, StackSkill = true } : Rdy(id, 11),   // a use left until 6 s, then none
            SlotId.일반4 => t < 1 ? Rdy(id, 0) : t < 23 ? Cd(id, 1, 22) : Rdy(id, 23),
            SlotId.일반5 => t < 3 ? Rdy(id, 0) : t < 6 ? Cd(id, 3, 12) : t < 17 ? Cd(id, 3, 12, est: true) : Rdy(id, 17),
            SlotId.일반6 => t < 4 ? SlotState.Unknown(id) : Rdy(id, 4),
            SlotId.일반7 => t < 4 ? Rdy(id, 0) : t < 8.5 ? Cd(id, 4, 4.5) : Rdy(id, 8.5),
            SlotId.일반8 => t < 12 ? Rdy(id, 0) : t < 20 ? Cd(id, 12, 8) : Rdy(id, 20),
            SlotId.아덴2 => t < 2 ? Cd(id, -10, 20) : t < 15 ? Rdy(id, 2) : t < 22 ? Cd(id, 15, 7) : Rdy(id, 22),   // reset by 일반3 at 2 s
            _ => Rdy(id, 0),
        };

        double gauge = t < 20 ? Math.Floor(t * 2) / 40 : 0.2;
        SlotState Lit(SlotId id) => State(id) with { Gold = id is SlotId.일반3 or SlotId.일반4 && t is >= 12 and < 18 };   // gold frame
        return new Snapshot(s + Sec(t), [.. SlotInfo.All.Select(Lit)], t is >= 6 and < 16, gauge, All, [.. Aden]) { Buffs = Buffs(s, t), Debuffs = Debuffs(s, t, duelist),
            Judgment = t < 1 ? JudgmentState.Ready : new JudgmentState(s + Sec(1)),   // 심판 at 1 s: its 6 s, then the 24 s cooldown
            Raid = new RaidStatus(null, 760 - (int)t, t is >= 5 and < 12), MeterView = DemoMeter(60 + (int)t) };   // the 줄 수 창 glows at 755 for 7 s
    }

    /// <summary>The debuff window: red with the boss at 6 red 2 blue (bring 1 red), the lightning from 5 s (6 s), a swap
    /// from 12 s (3 s), then blue at 4 / 4 from 15 s. With <paramref name="duelist"/> no lightning, and the 결투자 first seen at
    /// the start of the loop: its 5 s countdown, then the icon until the loop ends (30 s).</summary>
    static DebuffStatus Debuffs(TimeSpan s, double t, bool duelist = false)
    {
        DebuffMark? On(double from, double length) => t >= from && t < from + length ? new(s + Sec(from), s + Sec(from + length)) : null;
        var colour = new DebuffMark(s + Sec(t < 15 ? 0 : 15), null);
        var status = new DebuffStatus(t < 15 ? null : colour, t < 15 ? colour : null, On(5, 6), On(12, 3), t < 15 ? 6 : 4, t < 15 ? 2 : 4);
        return duelist ? status with { Lightning = null, Duelist = new DuelMark(s) } : status;
    }

    /// <summary>What 민수 sent of their boxes: colours (shown under 파티원 설정 받기) and a wide, short box size (under
    /// 파티원 크기 받기, fitted inside yours).</summary>
    static readonly Look MinsuLook = new(new Dictionary<SlotGroup, string>
    {
        [SlotGroup.일반스킬] = "#3FB8AF", [SlotGroup.각성기] = "#E8B53A", [SlotGroup.초각스] = "#B07CE8", [SlotGroup.아덴스킬] = "#5AA0FF",
    }, Width: 1.4, Height: 0.8);

    static Snapshot Minsu(TimeSpan s, double t)
    {
        SlotState State(SlotId id)
        {
            double cast = (int)id, total = 4 + (int)id * 0.6;
            return t >= cast && t < cast + total
                ? new(id, SlotPhase.Cooldown, s + Sec(cast + total), Sec(total), false, s + Sec(cast), Stacked: id == SlotId.일반3)
                : new(id, SlotPhase.Ready, null, null, false, s + Sec(t < cast ? 0 : cast + total));
        }
        return new Snapshot(s + Sec(t), [.. SlotInfo.All.Select(State)], t is >= 3 and < 9, Math.Min(1, Math.Floor(t) / 18), All, [], ClassId: 19);   // 바드
    }

    static Snapshot Jihyun(TimeSpan s, double t)
    {
        SlotState State(SlotId id)
        {
            double cast = 2 + (int)id * 0.7, total = 6;
            return t >= cast && t < cast + total
                ? new(id, SlotPhase.Cooldown, s + Sec(cast + total), Sec(total), false, s + Sec(cast))
                : new(id, SlotPhase.Ready, null, null, false, s + Sec(t < cast ? 0 : cast + total));
        }
        return new Snapshot(s + Sec(t), [.. SlotInfo.All.Select(State)], false, t < 12 ? 0.8 - Math.Floor(t) / 20 : 0.25,
            [SlotGroup.일반스킬], [], ClassId: 1);   // 워로드
    }

    /// <summary>Static sample for edit mode: some ready, some mid-cooldown; draw it with slot time frozen at <paramref name="at"/>.</summary>
    public static (Player Self, IReadOnlyList<Player> Peers) Sample(TimeSpan at, IReadOnlyCollection<SlotId>? adenSlots = null)
    {
        SlotState State(SlotId id)
        {
            int i = (int)id;
            bool gold = id is SlotId.일반1 or SlotId.일반2;   // one ready and one cooling box, to judge the gold border
            if (i % 3 == 0) return new(id, SlotPhase.Ready, null, null, false, at - Sec(10), Gold: gold);
            double total = 10 + i, left = total * (0.25 + 0.2 * (i % 4));
            return new(id, SlotPhase.Cooldown, at + Sec(left), Sec(total), false, at - Sec(total - left), Gold: gold);
        }
        var snap = new Snapshot(at, [.. SlotInfo.All.Select(State)], true, 0.6, All, [.. adenSlots ?? Aden]) { Buffs = Buffs(at - Sec(4), 4), Debuffs = Debuffs(at - Sec(7), 7) with { Swap = new(at, at + Sec(3)) },
            Judgment = new JudgmentState(at - Sec(3)), Raid = new RaidStatus(null, 755, true), MeterView = DemoMeter(95), Amsu = DemoAmsu(at, 13, true) };   // 심판 on, half its 6 s left; 줄 수 창 glowing
        var peer = snap with { AwakeningChanged = false, Gauge = 0.35 };
        return (new Player("", snap),
            [new("파티원1", peer with { AdenSlots = [.. Aden], ClassId = 1 }), new("파티원2", peer with { Groups = [SlotGroup.일반스킬], AdenSlots = [], ClassId = 19 }),
             new("파티원3", peer with { AdenSlots = [], ClassId = 17 })]);
    }

    /// <summary>Fake debug-window row for a slot: a crop and a reading consistent with the state.</summary>
    public static (Image Crop, Reading Reading) DebugRow(SlotState st, TimeSpan now)
    {
        var crop = new Image(44, 44);
        bool dark = st.Phase != SlotPhase.Ready;
        for (int y = 0; y < 44; y++)
            for (int x = 0; x < 44; x++)
            {
                int i = y * crop.Stride + x * 4, v = 60 + (x + y) * 2 + (int)st.Slot * 7;
                crop.Bgra[i] = (byte)(dark ? v / 3 : v);
                crop.Bgra[i + 1] = (byte)(dark ? v / 4 : 255 - v);
                crop.Bgra[i + 2] = (byte)(dark ? v / 3 : 200);
                crop.Bgra[i + 3] = 255;
            }
        var reading = st.Phase switch
        {
            SlotPhase.Ready => new Reading(st.Slot, ReadKind.Ready, 0, now),
            SlotPhase.Cooldown when st.Estimated => new Reading(st.Slot, ReadKind.Unreadable, 0, now),
            SlotPhase.Cooldown => new Reading(st.Slot, ReadKind.Seconds, (int)Math.Ceiling(st.Remaining(now)!.Value.TotalSeconds), now),
            SlotPhase.LongCooldown => new Reading(st.Slot, ReadKind.Minutes, st.Minutes, now),
            _ => new Reading(st.Slot, ReadKind.Unreadable, 0, now),
        };
        return (crop, reading);
    }

    static readonly ImmutableHashSet<SlotGroup> All = [.. Enum.GetValues<SlotGroup>()];
    static TimeSpan Sec(double s) => TimeSpan.FromSeconds(s);
}
