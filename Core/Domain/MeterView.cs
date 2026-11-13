namespace GaugeOverlay.Core.Domain;

/// <summary>What the meter window draws: the rows (by MeterMath), 기타 (null: hidden or under the dash rule), the boss figure,
/// the 전분 timer and whether it runs, whether the panel is read at all ("전분 없음" when not), and each row's history.
/// <see cref="BuffCap"/>: the buff rows were cut to 공증 and its figure is capped at it (<see cref="BuffLock"/>); null: unlocked.</summary>
public sealed record MeterView(IReadOnlyList<MeterRow> Rows, double? Other, long BossTaken, int? Timer, bool Running, bool PanelFound,
    MeterHistory History, IReadOnlyList<double>? BuffCover = null, bool ShowBuffBars = true,
    IReadOnlyList<IReadOnlyList<(ulong Id, string Name, double Share)>>? BuffSplit = null, string? BossName = null,
    IReadOnlyList<double>? BuffParty = null, MeterSkillLine? Skill = null, MeterGhost? Ghost = null, double? BuffCap = null)
{
    public bool BuffLocked => BuffCap is not null;

    /// <summary>The header's right side: the boss's name (else "보스"), then my share of its damage, else the boss damage.
    /// Apart, so a long name can be shortened and the figure after it kept.</summary>
    public (string Name, string After)? Header(ulong me, Func<long, string> amount) =>
        Rows.FirstOrDefault(r => r.Id == me)?.Share is { } mine ? (BossName ?? "보스", $" · 내 {mine * 100:0.0}%")
        : BossTaken > 0 ? (BossName ?? "보스", $" {amount(BossTaken)}") : null;

    /// <summary>The line graph's rows: the dealers and always me (alone, or as a support), me first.</summary>
    public IEnumerable<MeterRow> Lines(ulong me, int max) =>
        Rows.Where(r => r.Role == MeterRole.딜러 || r.Id == me).OrderByDescending(r => r.Id == me).Take(max);

    static readonly TimeSpan TrendOver = TimeSpan.FromSeconds(10);

    /// <summary>Builds the view once a second (spec § C Display). My figures always come from this PC (so I see them with 내
    /// 피해량 보내기 off); everyone else's from the session (<paramref name="me"/>, <paramref name="host"/>: 0 alone). The history
    /// follows my own 전분 record, never a figure from the wire; this second is added to <paramref name="history"/>.
    /// <paramref name="myParty"/>: my 1·2파티 from the roster (the session's samples hold mine only when I send).
    /// <paramref name="align"/>: lines the figures up on one 전분 second (null: as they arrived). <paramref name="buffs"/>: my buff
    /// indicators this second (by BuffKind; empty or null when the buff row isn't read), for 버프 적용률.
    /// <paramref name="smallParty"/>: a 4-man raid, where the boss HP lost is the party's damage: each buff's share of it too
    /// (<see cref="BuffParty"/>). <paramref name="gimmick"/>: 대난투 is up, its number in the boss bar's place.</summary>
    public static MeterView Build(MeterStatus mine, MeterRole role, string myName, int classId, IReadOnlyList<MeterSample> session,
        ulong me, ulong host, MeterSettings settings, MeterHistory history, TimeSpan now, int myParty = 1,
        MeterAlign? align = null, IReadOnlyList<BuffState>? buffs = null, bool smallParty = false, bool gimmick = false)
    {
        if (!settings.Send) session = [];   // only someone who shares sees the others
        myParty = session.FirstOrDefault(s => s.Id == me)?.Party ?? myParty;
        var local = MeterTracker.Mine(mine, role) is { } l ? l with { Id = me, Name = myName, ClassId = classId, Party = myParty } : null;
        if (align is not null) (session, local) = align.Align(session, local);   // everyone at one 전분 second
        var samples = session.Where(s => s.Id != me).ToList();
        if (local is not null) samples.Add(local);
        // The boss figure comes from what was shared (mine is in it only when I send), so every PC divides by the same number;
        // my local figure stands in only when nobody shares.
        long boss = MeterMath.BossFigure(session.Count > 0 ? session : samples, host == 0 ? me : host);
        // The history is on the 전분 timer's seconds, as the game's own graph: a stopped timer (a phase change, the end) adds
        // nothing, so the graph stops with it and goes on without a gap.
        var fightAt = TimeSpan.FromSeconds(mine.Clock.Timer ?? 0);
        var rows = MeterMath.Rows(samples, me, host == 0 ? me : host, settings.Scope, history.SharesAt(fightAt - TrendOver), boss);
        // The panel lost (a cutscene, the inventory over it): the last figures and the graph stay, marked by PanelFound.
        if (mine.Panel is null && history.LastRows is { Count: > 0 } last) rows = last;
        else if (mine.Clock.Timer is not null && !mine.Clock.Frozen) history.Add(fightAt, mine.Clock.Fight, rows);
        // Left out of 버프 적용률 (LeftOut): the boss bar unread (read before, and not 대난투, whose number stands in for it),
        // or my skill bar tinted. A support's own dead time stays in (user, 2026-11-11).
        bool barUnread = !gimmick && mine.HpAt > TimeSpan.Zero && now - mine.HpAt > FightRecorder.HpFresh;
        bool tinted = role != MeterRole.서포터 && mine.Tinted(now);
        if (mine.Panel is { } p)
            history.Cover(mine.Clock.Fight, p.Damage, buffs, now, [.. samples.Where(s => s.Id != me && s.Role == MeterRole.딜러)], BossDamage.Taken(mine.Boss), barUnread, tinted);
        else if (barUnread) history.BarHidden();
        // A dealer sees the buffs' part of their own damage; a support (whose 조력 피해량 is the dealers' damage) sees each buff
        // split between the dealers, never itself.
        var split = role == MeterRole.서포터 ? history.Split(me, myName, iDeal: false) : null;
        // A support's own figure is 조력 피해량, so its "mine" is no share: in a 4-man raid it sees the party's alone.
        var party = smallParty ? history.PartyCovered : null;
        bool support = role == MeterRole.서포터;
        return new(rows, settings.Other ? MeterMath.Other(rows, boss) : null, boss, mine.Panel?.Timer, !mine.Clock.Frozen,
            mine.PanelFound, history, support ? null : history.Covered, ShowBuffBars: !support || split is not null || party is not null,
            BuffSplit: split, BuffParty: party);
    }
}
