using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class BossHpGraphTests
{
    const long Max = 1_000_000_000;

    static FightRecord Fight(params RecordBoss[] boss) =>
        new(new DateTime(2026, 11, 4, 20, 0, 0), "x", 100, [], "나", 3, null, [], [], boss, []);

    /// <summary>One read a second from HP percentages, each with its shield percentage or 0.</summary>
    static FightRecord Hp(params (double Hp, double Shield)[] reads) =>
        Fight([.. reads.Select((r, i) => new RecordBoss(i, 0, (long)((r.Hp + r.Shield) * Max / 100), Max, r.Shield > 0 ? (long)(r.Shield * Max / 100) : null))]);

    static FightRecord Hp(params double[] reads) => Hp([.. reads.Select(r => (r, 0d))]);

    static IReadOnlyList<BossHeal> Heals(FightRecord r) => BossHpGraph.Of(r).SelectMany(b => b.Heals).ToList();

    [Fact]
    public void The_real_heal_of_a_saved_fight_counts()   // 벨가르딘 1관문 나이트메어, 2026-11-04 20:39, 4:57 → 5:00
    {
        const long max = 4951627226525;
        var r = Fight(
            new(295, 0, 1906772038908, max), new(296, 0, 1906760853087, max), new(297, 0, 1906748958948, max),
            new(300, 0, 1956172310803, max), new(301, 0, 1956171557325, max), new(302, 0, 1956157614963, max));
        var heal = Assert.Single(Heals(r));
        Assert.Equal((297d, 300d), (heal.From, heal.To));
        Assert.Equal(0.01, heal.Gain, 3);
    }

    [Fact]
    public void The_misread_first_read_of_a_saved_fight_is_no_heal()   // 고통의 마녀 1관문, 2026-11-03 22:55: "68"
    {
        const long max = 769567947570;
        var r = Fight(new(5, 0, null, null), new(7, 0, 68, max), new(8, 0, 767539531858, max), new(9, 0, 765622996700, max), new(10, 0, 762587254227, max));
        Assert.Empty(Heals(r));
    }

    [Fact]
    public void A_rise_after_a_wild_fall_is_no_heal() => Assert.Empty(Heals(Hp(60, 59, 10, 58, 57, 56)));

    [Fact]
    public void A_rise_that_snaps_back_is_no_heal() => Assert.Empty(Heals(Hp(60, 59, 90, 58, 57, 56)));

    [Fact]
    public void A_rise_at_the_end_of_a_bar_is_not_judged() => Assert.Empty(Heals(Hp(60, 59, 58, 62, 61)));

    [Fact]
    public void A_heal_over_several_reads_is_one_heal_to_its_last_rise()
    {
        var heal = Assert.Single(Heals(Hp(60, 59, 58, 59, 60, 61, 61, 60, 59)));
        Assert.Equal((2d, 5d), (heal.From, heal.To));
        Assert.Equal(0.03, heal.Gain, 3);
    }

    [Fact]
    public void A_new_bar_is_no_heal_and_question_marks_draw_nothing()
    {
        var r = Fight(new(0, 0, 500_000_000, 1_000_000_000), new(1, 0, 400_000_000, 1_000_000_000), new(2, 0, 7, 7), new(3, 0, 900_000_000, 2_000_000_000), new(4, 0, 800_000_000, null), new(5, 0, null, 2_000_000_000));
        var bars = BossHpGraph.Of(r);
        Assert.Equal(2, bars.Count);
        Assert.Equal([0.5, 0.4], bars[0].Points.Select(p => p.Hp));
        Assert.Equal([0.45, 0.4], bars[1].Points.Select(p => p.Hp));   // a read without a max keeps the bar's; one without HP draws nothing
        Assert.Empty(bars.SelectMany(b => b.Heals));
    }

    [Fact]
    public void A_bar_of_question_marks_draws_nothing() => Assert.Empty(BossHpGraph.Of(Fight(new(0, 0, 7, 7), new(1, 0, 6, 7))));

    static double[] Shields(FightRecord r) => [.. BossHpGraph.Of(r).Single().Points.Select(p => Math.Round(p.Shield * 100))];

    [Fact]
    public void A_shield_sits_on_top_of_the_hp_and_is_no_heal()
    {
        var r = Hp((60, 0), (59, 0), (58, 0), (58, 5), (58, 4), (58, 3), (58, 0), (58, 0), (57, 0), (56, 0));
        Assert.Equal([0, 0, 0, 5, 4, 3, 0, 0, 0, 0], Shields(r));
        Assert.Empty(Heals(r));
        Assert.Equal(0.58, BossHpGraph.Of(r).Single().Points[3].Hp, 3);
    }

    [Fact]
    public void One_read_of_a_shield_is_ignored() =>
        Assert.Equal([0, 0, 0, 0], Shields(Hp((60, 0), (59, 5), (58, 0), (57, 0))));

    [Fact]
    public void Another_stack_on_a_shield_not_yet_gone_counts_at_any_size() =>
        Assert.Equal([0, 40, 30, 90, 80, 70, 60], Shields(Hp((60, 0), (60, 40), (60, 30), (60, 90), (60, 80), (60, 70), (60, 60))));

    [Fact]
    public void A_shield_jumping_for_one_read_keeps_its_value() =>
        Assert.Equal([0, 40, 39, 39, 38, 37], Shields(Hp((60, 0), (60, 40), (60, 39), (60, 90), (60, 38), (60, 37))));

    [Fact]
    public void A_shield_dropping_for_one_read_keeps_its_value() =>
        Assert.Equal([0, 40, 40, 39, 38], Shields(Hp((60, 0), (60, 40), (60, 5), (60, 39), (60, 38))));

    [Fact]
    public void A_shield_missed_for_one_read_stays_until_two_reads_without_it() =>
        Assert.Equal([0, 40, 40, 40, 39, 0, 0], Shields(Hp((60, 0), (60, 40), (60, 40), (60, 0), (60, 39), (60, 0), (60, 0))));

    [Theory]
    [InlineData(1_057_137_405_322, "1조 571억")]
    [InlineData(2_000_000_000_000, "2조")]
    [InlineData(123_456_789_012, "1,234억")]
    [InlineData(50_000_000, "5,000만")]
    [InlineData(68, "68")]
    public void Amounts_are_spelled_in_the_game_s_units(long hp, string text) => Assert.Equal(text, BossHpGraph.AmountText(hp));

    [Fact]
    public void Lines_left_round_up_as_the_game_shows_them() =>   // 구원의 종탑 2관문 3단계, read in game: X 177 of 200
        Assert.Equal(177, BossHpGraph.LinesLeft(1_057_137_405_322, 1_197_954_508_761 / 200d));

    [Fact]
    public void A_fight_recorded_before_shields_were_saved_still_draws() =>
        Assert.Equal(0.5, BossHpGraph.Of(Fight(new RecordBoss(0, 0, 500_000_000, 1_000_000_000))).Single().Points[0].Hp, 3);
}
