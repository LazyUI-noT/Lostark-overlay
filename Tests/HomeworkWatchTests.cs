using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>숙제's fallback (pyosi-map.plan.md): clears worked out from the boss bar alone, with no recorded fight.</summary>
public class HomeworkWatchTests
{
    const long G1 = 1_000_000_000_000, G2 = 2_000_000_000_000, Other = 3_000_000_000_000, Hidden = 7;

    static readonly RaidGate Serka1 = new("serka-g1", "고통의 마녀", "1관문", [], [], Raid.없음, HpByDifficulty: new Dictionary<string, long> { ["하드"] = G1 });
    static readonly RaidGate Serka2 = new("serka-g2", "고통의 마녀", "2관문", [], [], Raid.없음, HpByDifficulty: new Dictionary<string, long> { ["하드"] = G2 });
    static readonly RaidGate Elsewhere = new("act4-g2", "4막: 파멸의 성채", "2관문", [], [], Raid.없음, HpByDifficulty: new Dictionary<string, long> { ["하드"] = Other });
    static readonly RaidGate Act3 = new("act3-g3", "3막", "3관문", [], [], Raid.없음);   // no HP data: known by its name only
    static readonly RaidGate Final22 = new("final-g2-2", "종막", "2-2관문 (하드)", [], [], Raid.없음);
    static readonly IReadOnlyList<RaidGate> Gates = [Serka1, Serka2, Elsewhere, Act3, Final22];

    static readonly IReadOnlyList<HomeworkRaid> Raids =
    [
        new("serka", "세르카", ["serka-g1", "serka-g2"], [new("하드", "세하", HomeworkColor.Orange, ["serka-g2"])]),
        new("act3", "3막", ["act3-g3"], [new("노말", "3노", HomeworkColor.White, ["act3-g3"]), new("하드", "3하", HomeworkColor.Orange, ["act3-g3"])]),
        new("final", "종막", ["final-g2-2"], [new("하드", "종하", HomeworkColor.Orange, ["final-g2-2"], EndsHidden: true)]),
    ];

    /// <summary>A watch stepped second by second: <see cref="Run"/> feeds reads, <see cref="Decisions"/> collects what it decided.</summary>
    sealed class Play
    {
        public HomeworkWatch Watch = HomeworkWatch.None;
        public double Time;
        public readonly List<WatchDecision> Decisions = [];

        /// <summary>One step per second for <paramref name="seconds"/>: the bar at <paramref name="current"/> of <paramref name="max"/>, or
        /// no bar when <paramref name="max"/> is null.</summary>
        public Play Run(int seconds, long? max, long current = 0, RaidGate? name = null, bool blocked = false, bool daenantu = false, bool killed = false)
        {
            for (int i = 0; i < seconds; i++)
            {
                Time += 1;
                var read = max is { } m ? new WatchRead(current, m) : null;
                var (next, decision) = Watch.Step(read, new WatchContext(blocked, name, TimeSpan.FromSeconds(Time), daenantu, killed), Gates, Raids);
                Watch = next;
                if (decision is not null) Decisions.Add(decision);
            }
            return this;
        }
    }

    [Fact]
    public void A_last_gate_whose_bar_is_gone_under_the_clear_threshold_is_cleared()
    {
        var play = new Play().Run(5, G2, G2 / 2).Run(2, G2, G2 / 100).Run(4, null);

        Assert.Equal([new WatchDecision("serka", "하드", WatchOutcome.Cleared)], play.Decisions);
    }

    [Fact]
    public void Daenantu_and_a_few_seconds_after_it_are_no_bar_gone()
    {
        // 대난투 shows no boss HP, only the damage dealt (user, 2026-11-08); its number unread a few seconds (or the gimmick just
        // ended) is held as FightRecorder holds it, until ShadowEndAfter.
        var play = new Play().Run(3, G2, G2 / 100).Run(20, null, daenantu: true).Run(6, null);

        Assert.Empty(play.Decisions);
    }

    [Fact]
    public void A_bar_still_gone_after_daenantu_s_hold_is_judged_again()
    {
        var play = new Play().Run(3, G2, G2 / 100).Run(20, null, daenantu: true).Run(7, null);

        Assert.Equal([new WatchDecision("serka", "하드", WatchOutcome.Cleared)], play.Decisions);
    }

    [Fact]
    public void A_boss_killed_in_daenantu_whose_bar_never_returns_is_cleared()
    {
        // The bar was at half before 대난투; the gimmick's number reached the boss's HP (Dead), and no bar came back.
        var play = new Play().Run(3, G2, G2 / 2).Run(20, null, daenantu: true).Run(12, null, killed: true);

        Assert.Equal([new WatchDecision("serka", "하드", WatchOutcome.Cleared)], play.Decisions);
    }

    [Fact]
    public void Daenantu_without_a_kill_leaves_a_half_bar_undecided()
    {
        var play = new Play().Run(3, G2, G2 / 2).Run(20, null, daenantu: true).Run(12, null);

        Assert.Empty(play.Decisions);
    }

    [Fact]
    public void A_real_bar_read_after_a_daenantu_kill_wins_over_the_kill()
    {
        var play = new Play().Run(3, G2, G2 / 2).Run(20, null, daenantu: true).Run(2, null, killed: true).Run(3, G2, G2 / 2, killed: true).Run(12, null);

        Assert.Empty(play.Decisions);
    }

    [Fact]
    public void The_bar_gone_for_less_than_three_seconds_decides_nothing()
    {
        var play = new Play().Run(2, G2, G2 / 100).Run(2, null);

        Assert.Empty(play.Decisions);
    }

    [Fact]
    public void A_bar_gone_between_five_and_ten_percent_needs_checking()
    {
        var play = new Play().Run(3, G2, G2 / 14).Run(4, null);   // 7%

        Assert.Equal([new WatchDecision("serka", "하드", WatchOutcome.Unsure)], play.Decisions);
    }

    [Fact]
    public void A_bar_gone_with_most_of_the_boss_left_is_no_clear()
    {
        var play = new Play().Run(3, G2, G2 / 3).Run(4, null);

        Assert.Empty(play.Decisions);
    }

    [Fact]
    public void A_gate_that_does_not_finish_the_raid_decides_nothing()
    {
        var play = new Play().Run(3, G1, G1 / 100).Run(4, null);

        Assert.Empty(play.Decisions);
    }

    [Fact]
    public void A_gone_bar_is_not_counted_while_the_game_shows_nothing_to_read()
    {
        var play = new Play().Run(3, G2, G2 / 100).Run(10, null, blocked: true);

        Assert.Empty(play.Decisions);   // the HUD hidden, another app in front or 대난투

        play.Run(4, null);

        Assert.Equal([new WatchDecision("serka", "하드", WatchOutcome.Cleared)], play.Decisions);   // three seconds after it is back
    }

    [Fact]
    public void The_same_boss_again_soon_after_means_the_wipe_took_it_back()
    {
        var play = new Play().Run(3, G2, G2 / 100).Run(4, null).Run(30, null).Run(2, G2, G2);

        Assert.Equal([new WatchDecision("serka", "하드", WatchOutcome.Cleared), new WatchDecision("serka", "하드", WatchOutcome.Failed)], play.Decisions);
    }

    [Fact]
    public void A_low_bar_back_after_a_gap_is_the_same_fight_not_a_retry()   // v1, v5: the bar at 1 HP drops out for 3 s during the kill
    {
        var play = new Play().Run(3, G2, 1).Run(4, null).Run(2, G2, 1).Run(4, null);

        Assert.DoesNotContain(play.Decisions, d => d.Outcome == WatchOutcome.Failed);
        Assert.Equal(new WatchDecision("serka", "하드", WatchOutcome.Cleared), play.Decisions[^1]);
    }

    [Fact]
    public void One_misread_of_a_tiny_max_before_the_bar_goes_is_not_the_hidden_phase()   // v5: "1/2" for one read at 1 HP
    {
        var play = new Play().Run(3, G2, 1).Run(1, Hidden, 1).Run(4, null);

        Assert.Equal([new WatchDecision("serka", "하드", WatchOutcome.Cleared)], play.Decisions);
    }

    [Fact]
    public void The_same_boss_long_after_is_another_run_and_keeps_the_clear()
    {
        var play = new Play().Run(3, G2, G2 / 100).Run(4, null).Run((int)HomeworkWatch.RetryWithin.TotalSeconds + 5, null).Run(2, G2, G2);

        Assert.Equal([new WatchDecision("serka", "하드", WatchOutcome.Cleared)], play.Decisions);
    }

    [Fact]
    public void Another_raids_bar_right_after_a_low_last_gate_needs_checking()
    {
        var play = new Play().Run(3, G2, G2 / 100).Run(3, Other, Other);   // no 3 s of no bar between them

        Assert.Equal([new WatchDecision("serka", "하드", WatchOutcome.Unsure)], play.Decisions);
    }

    [Fact]
    public void The_same_boss_right_after_a_low_end_is_a_retry_and_decides_nothing()
    {
        var play = new Play().Run(3, G2, G2 / 100).Run(3, G2, G2);

        Assert.Empty(play.Decisions);
    }

    [Fact]
    public void A_gate_the_data_does_not_know_by_its_bar_is_not_counted()
    {
        var play = new Play().Run(3, 1_234_567_890_123, 1_000).Run(4, null);

        Assert.Empty(play.Decisions);
    }

    [Fact]
    public void A_gate_known_by_its_name_alone_is_cleared_with_the_difficulty_to_check()
    {
        var play = new Play().Run(8, 5_000_000_000_000, 1_000, name: Act3).Run(4, null, name: Act3);

        Assert.Equal([new WatchDecision("act3", null, WatchOutcome.Unsure)], play.Decisions);   // 노말 or 하드: the max HP says nothing
    }

    [Fact]
    public void A_name_is_not_trusted_in_the_first_seconds_of_a_bar()
    {
        var play = new Play().Run(2, 5_000_000_000_000, 1_000, name: Act3).Run(4, null, name: Act3);   // it may still be the last boss's name

        Assert.Empty(play.Decisions);
    }

    [Fact]
    public void A_raid_that_ends_on_the_hidden_bar_needs_checking_once_it_shows()
    {
        var play = new Play().Run(8, 4_000_000_000_000, 2_000_000_000_000, name: Final22).Run(2, Hidden, name: Final22);

        Assert.Empty(play.Decisions);   // two reads of ??? may be a misread

        play.Run(5, Hidden, name: Final22);

        Assert.Equal([new WatchDecision("final", "하드", WatchOutcome.Unsure)], play.Decisions);   // once, not on every read
    }

    [Fact]
    public void A_hidden_bar_after_a_gate_that_does_not_end_on_it_decides_nothing()
    {
        var play = new Play().Run(4, G2, G2 / 2).Run(6, Hidden);

        Assert.Empty(play.Decisions);
    }

    [Fact]
    public void A_raid_that_ends_on_the_hidden_bar_is_not_cleared_by_its_bar_going()
    {
        var play = new Play().Run(8, 4_000_000_000_000, 100, name: Final22).Run(5, null, name: Final22);   // 2-2 low, but its end is ???

        Assert.Empty(play.Decisions);
    }
}
