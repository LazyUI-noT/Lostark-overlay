using System.Collections.Immutable;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>Fake snapshot source for --demo and the edit-mode sample. A 30 s script loops: cooldowns of
/// several lengths, a reduction, a reset (333 E → 아덴), a stack skill with a use left, a 3m long cooldown,
/// estimated, unknown, 초각기 표시, a gold frame on E and R from 12 to 18 s, a moving gauge and two peers. From 23 s to 30 s nothing moves, so the render loop can idle.</summary>
public sealed class DemoSnapshots
{
    const double Loop = 30;
    static readonly SlotId[] Aden = [SlotId.아덴2];
    readonly TimeSpan _origin = Clock.Now();
    readonly bool _duelist;
    Snapshot? _last;
    Snapshot[]? _lastPeers;

    /// <param name="duelist">--duelist: the debuff window shows the 결투자 from the start of each loop instead of the lightning.</param>
    public DemoSnapshots(bool duelist = false) => _duelist = duelist;

    /// <summary>True when the script changed since the last call; then returns the new state.</summary>
    public bool TryNext(out Player self, out IReadOnlyList<Player> peers)
    {
        var now = Clock.Now();
        double cycles = Math.Floor((now - _origin).TotalSeconds / Loop);
        var start = _origin + TimeSpan.FromSeconds(cycles * Loop);
        double t = (now - start).TotalSeconds;
        var me = Self(start, t, _duelist);
        self = new Player("", me);
        peers = [new("민수", Minsu(start, t), MinsuLook), new("지현가나다라마바사", Jihyun(start, t))];   // 지현 sent none
        bool changed = _last is null || !Same(_last, me) || _lastPeers is null || !_lastPeers.Zip(peers).All(p => Same(p.First, p.Second.Snapshot));
        _last = me;
        _lastPeers = [.. peers.Select(p => p.Snapshot)];
        return changed;
    }

    static bool Same(Snapshot a, Snapshot b) =>
        a.Slots.SequenceEqual(b.Slots) && a.AwakeningChanged == b.AwakeningChanged && a.Gauge == b.Gauge && a.Groups.SetEquals(b.Groups)
        && a.Buffs.SequenceEqual(b.Buffs) && a.Debuffs == b.Debuffs && a.Judgment == b.Judgment;

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
        return new Snapshot(s + Sec(t), [.. SlotInfo.All.Select(Lit)], t is >= 6 and < 16, gauge, All, [.. Aden]) { Buffs = Buffs(s, t), Debuffs = Debuffs(s, t, duelist) };
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
            Judgment = new JudgmentState(at - Sec(3)) };   // 심판 on: both parts of the bar and the line show
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
