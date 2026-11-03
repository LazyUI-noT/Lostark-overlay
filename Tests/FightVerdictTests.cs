using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>A fight's 클리어 / 실패 from the next 전분 record's boss (user, 2026-11-03): the same boss again is a retry (실패), another
/// is the next gate (클리어).</summary>
public class FightVerdictTests
{
    static FightRecord Fight(string? gate, params long?[] maxes) =>
        new(new DateTime(2026, 11, 3, 21, 0, 0), gate, 60, [], "나", 0, null, [], [],
            [.. maxes.Select((m, i) => new RecordBoss(i, 0, m is null ? null : m / 2, m))], []);

    const long 세르카1 = 1_194_141_353_268, 세르카2 = 1_302_000_000_000;

    [Fact]
    public void The_same_boss_again_is_a_retry_so_the_fight_failed() =>
        Assert.False(FightVerdict.ByNext(Fight("세르카 1관문", 세르카1), 세르카1 - 3, null));   // the read wobbles in its last digits

    [Fact]
    public void Another_boss_is_the_next_gate_so_the_fight_was_cleared() =>
        Assert.True(FightVerdict.ByNext(Fight("세르카 1관문", 세르카1), 세르카2, null));

    [Fact]
    public void A_retry_of_a_boss_with_phases_matches_its_first_bar()
    {
        // A boss whose second phase has another max: the retry starts on the first phase's bar again.
        var twoPhases = Fight("카제로스", 1_000_000_000_000, 400_000_000_000);
        Assert.False(FightVerdict.ByNext(twoPhases, 1_000_000_000_000, null));
    }

    [Fact]
    public void Without_a_max_hp_the_gate_names_decide()
    {
        Assert.False(FightVerdict.ByNext(Fight("구원의 종탑", [null]), null, "구원의 종탑"));
        Assert.True(FightVerdict.ByNext(Fight("구원의 종탑", [null]), null, "구원의 종탑 · 2관문"));
    }

    [Fact]
    public void Nothing_to_compare_decides_nothing()
    {
        Assert.Null(FightVerdict.ByNext(Fight(null, [null]), null, null));
        Assert.Null(FightVerdict.ByNext(Fight("구원의 종탑", 세르카1), null, null));
    }
}
