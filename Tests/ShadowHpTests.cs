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
    public void A_jump_of_over_a_quarter_of_the_hp_counts_only_when_four_reads_agree()
    {
        var s = ShadowHp.Step(null, Bar, 1000 * Eok);
        var once = ShadowHp.Step(s, Bar, 3000 * Eok);   // +2,000억 of 4,696억: a misread digit until it holds 0.6 s
        Assert.Equal(1000 * Eok, once!.Dealt);
        Assert.Equal((1050 * Eok, (long?)null), ShadowHp.Step(once, Bar, 1050 * Eok) is { } back ? (back.Dealt, back.Pending) : default);   // it was a misread

        var thrice = ShadowHp.Step(ShadowHp.Step(once, Bar, 3005 * Eok), Bar, 3008 * Eok);
        Assert.Equal(1000 * Eok, thrice!.Dealt);
        Assert.Equal(3010 * Eok, ShadowHp.Step(thrice, Bar, 3010 * Eok)!.Dealt);   // it was real
    }

    [Fact]
    public void A_misread_past_the_hp_that_held_is_undone_when_four_lower_reads_agree()
    {
        // 세르카 2관 2026-11-09: the number read past the HP left for 0.8 s stuck the boss at 1 HP for 23 s of 대난투.
        var s = ShadowHp.Step(null, Bar, 1000 * Eok);
        foreach (long n in new long[] { 5000, 5001, 5002, 5003 }) s = ShadowHp.Step(s, Bar, n * Eok);
        Assert.Equal(5003 * Eok, s!.Dealt);

        foreach (long n in new long[] { 1100, 1110, 1120 }) s = ShadowHp.Step(s, Bar, n * Eok);
        Assert.Equal(5003 * Eok, s!.Dealt);   // a lower stretch shorter than 0.6 s takes nothing back
        s = ShadowHp.Step(s, Bar, 1130 * Eok);

        Assert.Equal(1130 * Eok, s!.Dealt);
        Assert.False(s.Dead);
    }

    [Fact]
    public void A_misread_between_agreeing_reads_starts_the_count_again()
    {
        var s = ShadowHp.Step(null, Bar, 1000 * Eok);
        s = ShadowHp.Step(ShadowHp.Step(ShadowHp.Step(s, Bar, 3000 * Eok), Bar, 3005 * Eok), Bar, 1100 * Eok);
        s = ShadowHp.Step(ShadowHp.Step(s, Bar, 3010 * Eok), Bar, 3015 * Eok);

        Assert.Equal(1100 * Eok, s!.Dealt);
    }

    [Fact]
    public void After_a_gap_a_jump_from_the_count_before_it_still_needs_four_reads()
    {
        var before = ShadowHp.Step(null, Bar, 1000 * Eok);
        var after = ShadowHp.Resume(before, Bar, 3000 * Eok);
        Assert.Equal(1000 * Eok, after!.Dealt);   // without the count before, 3,000억 would be taken as is
        Assert.Equal(1050 * Eok, ShadowHp.Resume(before, Bar, 1050 * Eok)!.Dealt);
    }

    [Fact]
    public void After_a_gap_a_skipped_read_starts_nothing()
    {
        var before = ShadowHp.Step(null, Bar, 1000 * Eok);
        Assert.Null(ShadowHp.Resume(before, Bar, 99_999 * Eok));   // the old count doesn't come back on a misread
        Assert.Null(ShadowHp.Resume(before, null, 1000 * Eok));
    }

    [Fact]
    public void After_a_gap_reads_agreeing_before_it_count_again_from_one()
    {
        var s = ShadowHp.Step(null, Bar, 1000 * Eok);
        foreach (long n in new long[] { 3000, 3005, 3008 }) s = ShadowHp.Step(s, Bar, n * Eok);
        Assert.Equal(1000 * Eok, ShadowHp.Resume(s, Bar, 3010 * Eok)!.Dealt);   // 4 in a row, not 3 then 1 after 3 s unread
    }

    [Fact]
    public void After_a_gap_a_dead_count_is_not_carried()
    {
        var s = ShadowHp.Step(null, Bar, 4500 * Eok);
        foreach (long n in new long[] { 4700, 4710, 4720, 4730 }) s = ShadowHp.Step(s, Bar, n * Eok);
        var after = ShadowHp.Resume(s, Bar, 2000 * Eok);
        Assert.False(after!.Dead);   // a false kill from misreads must not stick for the rest of 대난투
        Assert.Equal(2000 * Eok, after.Dealt);
    }

    [Fact]
    public void Agreeing_reads_must_be_close_both_ways()
    {
        var s = ShadowHp.Step(null, Bar, 1000 * Eok);
        s = ShadowHp.Step(ShadowHp.Step(ShadowHp.Step(s, Bar, 3000 * Eok), Bar, 3600 * Eok), Bar, 4200 * Eok);
        Assert.Equal(1000 * Eok, ShadowHp.Step(s, Bar, 4300 * Eok)!.Dealt);   // each 20% over the last: not one number held
    }

    [Fact]
    public void Dead_stays_dead()
    {
        var s = ShadowHp.Step(null, Bar, 4500 * Eok);
        foreach (long n in new long[] { 4700, 4710, 4720, 4730 }) s = ShadowHp.Step(s, Bar, n * Eok);
        Assert.True(s!.Dead);
        Assert.True(ShadowHp.Step(s, Bar, 4000 * Eok)!.Dead);   // a lower read after: a misread, not the boss back
    }

    [Fact]
    public void The_boss_is_dead_after_four_reads_at_or_past_its_hp()
    {
        var s = ShadowHp.Step(null, Bar, 4600 * Eok);
        foreach (long n in new long[] { 4700, 4710, 4720 }) s = ShadowHp.Step(s, Bar, n * Eok);
        Assert.False(s!.Dead);   // under 0.6 s past it could be a misread
        Assert.Equal(0, s.Remaining);
        s = ShadowHp.Step(s, Bar, 4730 * Eok);
        Assert.True(s!.Dead);
    }
}
