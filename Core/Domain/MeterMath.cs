namespace GaugeOverlay.Core.Domain;

/// <summary>The meter's numbers (spec § C Formulas, the user's choice (a)): 딜러 딜지분 % = 피해량 ÷ boss damage taken, 서포터
/// 찬조 % = 조력 피해량 ÷ the same (not ranked with dealers: 조력 overlaps their damage), 기타 = what no dealer covers. Every PC
/// takes the same boss figure (<see cref="BossFigure"/>), so all show the same numbers. Pure.</summary>
public static class MeterMath
{
    /// <summary>Dash rule: a share shows only once the member's damage (or the boss's) reaches 1천만.</summary>
    public const long DashUnder = 10_000_000;
    const double TrendStep = 0.005;   // half a percentage point over 10 s

    /// <summary>The boss damage everyone divides by: the host's figure when it shares, else the median of the shared figures
    /// (lower middle), so no one member's figure moves everyone's % (security review); 0 with nobody.</summary>
    public static long BossFigure(IReadOnlyList<MeterSample> sharing, ulong hostId)
    {
        if (sharing.FirstOrDefault(s => s.Id == hostId) is { } host) return host.BossTaken;
        var all = sharing.Select(s => s.BossTaken).Order().ToList();
        return all.Count == 0 ? 0 : all[(all.Count - 1) / 2];
    }

    public static IReadOnlyList<MeterRow> Rows(IReadOnlyList<MeterSample> sharing, ulong me, ulong hostId, MeterScope scope,
        IReadOnlyDictionary<ulong, double>? sharesTenSecondsAgo, long? bossFigure = null)
    {
        long boss = bossFigure ?? BossFigure(sharing, hostId);
        int myParty = sharing.FirstOrDefault(s => s.Id == me)?.Party ?? 1;
        var shown = scope switch
        {
            MeterScope.나 => sharing.Where(s => s.Id == me),
            MeterScope.파티 => sharing.Where(s => s.Party == myParty),
            _ => sharing,
        };
        var rows = shown.Select(s =>
        {
            double? share = s.Damage >= DashUnder && boss >= DashUnder ? (double)s.Damage / boss : null;
            int trend = share is { } now && sharesTenSecondsAgo?.GetValueOrDefault(s.Id, double.NaN) is { } ago && !double.IsNaN(ago)
                ? now - ago > TrendStep ? 1 : ago - now > TrendStep ? -1 : 0 : 0;
            return new MeterRow(s.Id, s.Name, s.ClassId, s.Role, s.Party, share, s.Damage, s.Dps, trend);
        });
        var ordered = scope == MeterScope.파티별 ? rows.OrderBy(r => r.Party).ThenBy(r => r.Role) : rows.OrderBy(r => r.Role);
        return [.. ordered.ThenByDescending(r => r.Damage).ThenBy(r => r.Id)];
    }

    /// <summary>기타: the boss damage no shown dealer covers (0 at least), null under the dash rule.</summary>
    public static double? Other(IReadOnlyList<MeterRow> rows, long bossTaken) =>
        bossTaken < DashUnder ? null : Math.Max(0, 1 - rows.Where(r => r.Role == MeterRole.딜러).Sum(r => (double)r.Damage / bossTaken));
}

/// <summary>The meter's 1 Hz history of one 전분 record: each member's share and DPS over time, for the 10 s trend and the
/// graphs. A new record (<c>fight</c>) starts it over.</summary>
public sealed class MeterHistory
{
    readonly Dictionary<ulong, List<(TimeSpan At, double? Share, long Dps)>> _series = [];
    int _fight;

    /// <summary>The rows last added (this 전분 record), shown again while the panel is lost.</summary>
    public IReadOnlyList<MeterRow>? LastRows { get; private set; }

    public void Add(TimeSpan at, int fight, IReadOnlyList<MeterRow> rows)
    {
        LastRows = rows.Count > 0 ? rows : LastRows;
        if (fight != _fight)
        {
            _fight = fight;
            _series.Clear();
            LastRows = rows.Count > 0 ? rows : null;
        }
        foreach (var r in rows)
        {
            if (!_series.TryGetValue(r.Id, out var list)) _series[r.Id] = list = [];
            list.Add((at, r.Share, r.Dps));
        }
    }

    /// <summary>Each member's last share at or before <paramref name="at"/> (those with none are left out).</summary>
    public IReadOnlyDictionary<ulong, double> SharesAt(TimeSpan at)
    {
        var d = new Dictionary<ulong, double>();
        foreach (var (id, list) in _series)
            if (list.LastOrDefault(p => p.At <= at) is { Share: { } share }) d[id] = share;
        return d;
    }

    public IReadOnlyList<(TimeSpan At, double? Share, long Dps)> Series(ulong id) => _series.TryGetValue(id, out var l) ? l : [];

    // 버프 적용률 for this 전분 record: my damage in all, and the part dealt while 공증 / 아덴 피증 / 초각스 피증 was on. Each
    // second's new damage goes to each buff by the part of that second it was on (a buff on from halfway takes half: the
    // damage is taken as even over the second, the panel updating once a second). A guide, not what each buff added.
    static readonly BuffKind[] Covering = [BuffKind.공증, BuffKind.아덴피증, BuffKind.초각스피증];
    readonly double[] _under = new double[Covering.Length];
    long _total, _lastDamage;
    TimeSpan? _lastAt;
    // The other dealers (버프 분배): their damage under 아덴 피증 / 초각스 피증 by this screen's timing (party-wide buffs), and
    // under 공증 as each sent it (a 공증 field counts only for who stands in it).
    readonly Dictionary<ulong, (string Name, long Last, double[] Under)> _others = [];
    int _coverFight = -1;
    bool _buffsRead;

    public void Cover(int fight, long damage, IReadOnlyList<BuffState>? buffs, TimeSpan now, IReadOnlyList<MeterSample>? otherDealers = null)
    {
        // A new record: counted from its first reading on (damage before it, joined mid-fight, is nobody's guess).
        if (fight != _coverFight) { (_coverFight, _total, _lastDamage) = (fight, 0, damage); Array.Clear(_under); _others.Clear(); }
        var from = _lastAt;
        _lastAt = now;
        _buffsRead = buffs is { Count: > 0 };
        var on = new double[Covering.Length];
        if (_buffsRead)
            for (int i = 0; i < Covering.Length; i++)
                if ((int)Covering[i] < buffs!.Count) on[i] = OnFor(buffs[(int)Covering[i]], from, now);
        long added = damage - _lastDamage;
        _lastDamage = damage;
        if (added > 0 && _buffsRead)
        {
            _total += added;
            for (int i = 0; i < Covering.Length; i++) _under[i] += added * on[i];
        }
        foreach (var s in otherDealers ?? [])
        {
            // First seen (joined or started sharing mid-fight): counted from here on, not their whole record at once.
            if (!_others.TryGetValue(s.Id, out var o)) { _others[s.Id] = (s.Name, s.Damage, [double.NaN, 0, 0]); continue; }
            long d = s.Damage - o.Last;
            if (d > 0 && _buffsRead) for (int i = 1; i < Covering.Length; i++) o.Under[i] += d * on[i];
            o.Under[0] = s.AtkBuffed is { } atk ? atk : double.NaN;
            _others[s.Id] = (s.Name, s.Damage, o.Under);
        }
    }

    /// <summary>My damage this record under my own 공증, for the party (null while the buff row isn't read).</summary>
    public long? MyAtkBuffed => _buffsRead ? (long)Math.Round(_under[0]) : null;

    /// <summary>버프 분배: per buff (공증 / 아덴 피증 / 초각스 피증), each dealer's part of the damage dealt under it, largest first;
    /// null with fewer than two dealers (then <see cref="Covered"/> is the bar) or while the buff row isn't read.</summary>
    public IReadOnlyList<IReadOnlyList<(ulong Id, string Name, double Share)>>? Split(ulong me, string myName, bool iDeal)
    {
        if (!_buffsRead || _others.Count + (iDeal ? 1 : 0) < 2) return null;
        var who = _others.Select(o => (Id: o.Key, o.Value.Name, o.Value.Under)).ToList();
        if (iDeal) who.Add((me, myName, _under));
        return [.. Enumerable.Range(0, Covering.Length).Select(i =>
        {
            var parts = who.Where(w => double.IsFinite(w.Under[i]) && w.Under[i] > 0).ToList();
            double sum = parts.Sum(w => w.Under[i]);
            return (IReadOnlyList<(ulong, string, double)>)[.. parts.OrderByDescending(w => w.Under[i]).Select(w => (w.Id, w.Name, w.Under[i] / sum))];
        })];
    }

    /// <summary>The part of (from, now] the buff was on, from its state now and when it last changed (these buffs last
    /// seconds, so at most one change falls in a second).</summary>
    static double OnFor(BuffState b, TimeSpan? from, TimeSpan now)
    {
        if (from is not { } f || now <= f || b.ChangedAt <= f) return b.Active ? 1 : 0;
        double changedPart = Math.Clamp((now - b.ChangedAt) / (now - f), 0, 1);   // the part since it changed
        return b.Active ? changedPart : 1 - changedPart;
    }

    /// <summary>공증 / 아덴 피증 / 초각스 피증's share of my damage this record; null while the buff row isn't read.</summary>
    public IReadOnlyList<double>? Covered => !_buffsRead ? null : [.. _under.Select(u => _total > 0 ? (double)u / _total : 0)];
}
