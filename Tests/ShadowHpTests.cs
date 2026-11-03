using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The boss HP during the 그림자 gimmick: the last bar read minus the gimmick's number (the damage dealt in it).</summary>
public class ShadowHpTests
{
    const long Eok = 100_000_000;
    static readonly (long, long, int?) Bar = (4696 * Eok, 15392 * Eok, 92);   // 469.6B of 1,539.2B at X 92: 5.1B a line

    [Fact]
    public void Remaining_is_the_last_bar_hp_less_the_number_with_the_lines_from_its_X_n()
    {
        var s = ShadowHp.Step(null, Bar, 1341 * Eok)!;
        Assert.Equal(3355 * Eok, s.Remaining);
        Assert.Equal((int)Math.Ceiling(3355 / (4696 / 92.0)), s.Lines);
        Assert.False(s.Dead);
        Assert.Null(ShadowHp.Step(null, (4696 * Eok, 15392 * Eok, null), 1341 * Eok)!.Lines);   // no X n read: HP only
    }

    [Fact]
    public void Nothing_without_a_bar_read_before_or_a_number()
    {
        Assert.Null(ShadowHp.Step(null, null, 1341 * Eok));
        Assert.Null(ShadowHp.Step(null, Bar, null));
    }

    [Fact]
    public void The_number_only_grows_and_an_impossible_one_is_skipped()
    {
        var s = ShadowHp.Step(null, Bar, 1341 * Eok);
        Assert.Equal(1341 * Eok, ShadowHp.Step(s, Bar, 1000 * Eok)!.Dealt);   // a misread lower digit takes no HP back
        Assert.Equal(1341 * Eok, ShadowHp.Step(s, Bar, 99_999 * Eok)!.Dealt); // more than twice the HP left: a misread
    }

    [Fact]
    public void A_jump_of_over_a_quarter_of_the_hp_counts_only_when_the_next_read_agrees()
    {
        var s = ShadowHp.Step(null, Bar, 1000 * Eok);
        var once = ShadowHp.Step(s, Bar, 3000 * Eok);   // +2,000억 of 4,696억: a misread digit until seen twice
        Assert.Equal(1000 * Eok, once!.Dealt);
        Assert.Equal((1050 * Eok, (long?)null), ShadowHp.Step(once, Bar, 1050 * Eok) is { } back ? (back.Dealt, back.Pending) : default);   // it was a misread
        Assert.Equal(3010 * Eok, ShadowHp.Step(once, Bar, 3010 * Eok)!.Dealt);   // it was real: two reads agree
    }

    [Fact]
    public void Dead_stays_dead()
    {
        var s = ShadowHp.Step(null, Bar, 4500 * Eok);
        s = ShadowHp.Step(ShadowHp.Step(s, Bar, 4700 * Eok), Bar, 4710 * Eok);
        Assert.True(s!.Dead);
        Assert.True(ShadowHp.Step(s, Bar, 4000 * Eok)!.Dead);   // a lower read after: a misread, not the boss back
    }

    [Fact]
    public void The_boss_is_dead_after_two_reads_at_or_past_its_hp()
    {
        var s = ShadowHp.Step(null, Bar, 4600 * Eok);
        s = ShadowHp.Step(s, Bar, 4700 * Eok);
        Assert.False(s!.Dead);   // one read past it could be a misread
        Assert.Equal(0, s.Remaining);
        s = ShadowHp.Step(s, Bar, 4710 * Eok);
        Assert.True(s!.Dead);
    }
}
