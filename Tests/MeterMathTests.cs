using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The meter's numbers (MeterMath, spec § C Formulas): shares of the boss damage taken, 기타, the dash rule, scopes,
/// the 10 s trend and the one boss figure every PC uses.</summary>
public class MeterMathTests
{
    const long Eok = 100_000_000;   // 억
    static MeterSample P(ulong id, MeterRole role, int party, long damage, long boss = 100 * Eok) =>
        new(id, $"p{id}", 0, role, party, damage, damage / 60, 60, true, 1, boss);

    static readonly MeterSample[] Party1 =
        [P(1, MeterRole.딜러, 1, 40 * Eok), P(2, MeterRole.딜러, 1, 30 * Eok), P(3, MeterRole.딜러, 1, 25 * Eok), P(4, MeterRole.서포터, 1, 50 * Eok)];

    [Fact]
    public void Dealers_share_the_boss_damage_and_rank_before_supports()
    {
        var rows = MeterMath.Rows(Party1, me: 1, hostId: 1, MeterScope.파티, null);
        Assert.Equal([1UL, 2, 3, 4], rows.Select(r => r.Id));
        Assert.Equal(0.40, rows[0].Share!.Value, 6);
        Assert.Equal(0.50, rows[3].Share!.Value, 6);   // 찬조: 조력 overlaps dealer damage, so it is not ranked with them
        Assert.Equal(0.05, MeterMath.Other(rows, 100 * Eok)!.Value, 6);
    }

    [Fact]
    public void A_value_shows_only_past_ten_million()
    {
        var rows = MeterMath.Rows([P(1, MeterRole.딜러, 1, 9_999_999), P(2, MeterRole.딜러, 1, 10_000_000)], 1, 1, MeterScope.파티, null);
        Assert.Null(rows.Single(r => r.Id == 1).Share);
        Assert.NotNull(rows.Single(r => r.Id == 2).Share);
        Assert.Null(MeterMath.Other(rows, 9_999_999));
    }

    [Fact]
    public void Every_pc_takes_the_same_boss_figure()
    {
        MeterSample[] s = [P(5, MeterRole.딜러, 1, Eok, boss: 90 * Eok), P(3, MeterRole.딜러, 1, Eok, boss: 91 * Eok), P(9, MeterRole.딜러, 1, Eok, boss: 92 * Eok)];
        Assert.Equal(92 * Eok, MeterMath.BossFigure(s, hostId: 9));   // the host's, when it shares
        Assert.Equal(91 * Eok, MeterMath.BossFigure(s, hostId: 7));   // else the median: one member's figure can't move it
        MeterSample[] liar = [.. s, P(1, MeterRole.딜러, 1, Eok, boss: 10_000_000)];
        Assert.InRange(MeterMath.BossFigure(liar, hostId: 7), 90 * Eok, 92 * Eok);
        Assert.Equal(0, MeterMath.BossFigure([], 1));
    }

    [Fact]
    public void Scopes_pick_the_members_shown()
    {
        MeterSample[] raid = [.. Party1, P(5, MeterRole.딜러, 2, 20 * Eok), P(6, MeterRole.서포터, 2, 10 * Eok)];
        Assert.Equal([1UL], MeterMath.Rows(raid, 1, 1, MeterScope.나, null).Select(r => r.Id));
        Assert.Equal([5UL, 6], MeterMath.Rows(raid, 5, 1, MeterScope.파티, null).Select(r => r.Id));
        Assert.Equal(6, MeterMath.Rows(raid, 1, 1, MeterScope.공대, null).Count);
        Assert.Equal([1, 1, 1, 1, 2, 2], MeterMath.Rows(raid, 5, 1, MeterScope.파티별, null).Select(r => r.Party));
    }

    [Fact]
    public void The_trend_compares_with_ten_seconds_ago()
    {
        var ago = new Dictionary<ulong, double> { [1] = 0.30, [2] = 0.31, [3] = 0.25 };
        var rows = MeterMath.Rows(Party1, 1, 1, MeterScope.파티, ago);
        Assert.Equal([1, -1, 0], rows.Take(3).Select(r => r.Trend));
    }

    [Fact]
    public void History_keeps_one_fight_and_answers_ten_seconds_ago()
    {
        var h = new MeterHistory();
        var rows = MeterMath.Rows(Party1, 1, 1, MeterScope.파티, null);
        h.Add(S(0), 1, rows);
        h.Add(S(5), 1, rows.Select(r => r with { Share = 0.9 }).ToList());
        Assert.Equal(0.40, h.SharesAt(S(4))[1], 6);
        Assert.Equal(2, h.Series(1).Count);
        h.Add(S(20), 2, rows);   // a new 전분 record
        Assert.Single(h.Series(1));
        Assert.Empty(h.SharesAt(S(4)));
    }
}
