using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>A saved fight (spec § D What a record holds): the list's "my share".</summary>
public class FightRecordTests
{
    static FightRecord R(params RecordSample[] samples) =>
        new(new DateTime(2026, 10, 30, 21, 0, 0), null, 60, [], "나", 0, null, [], samples, [new RecordBoss(60, 1_000_000_000, null, null)], []);

    [Fact]
    public void My_share_is_my_last_damage_over_the_boss_taken() =>
        Assert.Equal(0.25, R(new RecordSample(30, "나", 100_000_000, 0), new RecordSample(60, "나", 250_000_000, 0), new RecordSample(60, "민수", 500_000_000, 0)).MyShare!.Value, 6);

    [Fact]
    public void On_a_clear_my_share_is_over_the_boss_whole_hp()
    {
        // The boss died with 100M unread on a 1,000M bar: it took all 1,000M, not the 800M read.
        var r = R(new RecordSample(60, "나", 250_000_000, 0)) with { Boss = [new RecordBoss(60, 800_000_000, 100_000_000, 1_000_000_000)] };
        Assert.Equal(250.0 / 800, r.MyShare!.Value, 6);
        Assert.Equal(250.0 / 1000, (r with { Cleared = true }).MyShare!.Value, 6);
    }

    [Fact]
    public void On_a_clear_across_bars_the_unread_hp_is_added_to_what_was_taken()
    {
        // Two bars: 1,500M taken, 100M of the last 1,000M bar unread.
        var r = R(new RecordSample(60, "나", 400_000_000, 0)) with { Boss = [new RecordBoss(60, 1_500_000_000, 100_000_000, 1_000_000_000)], Cleared = true };
        Assert.Equal(400.0 / 1600, r.MyShare!.Value, 6);
    }

    [Fact]
    public void My_share_is_a_dash_under_ten_million() => Assert.Null(R(new RecordSample(60, "나", 9_999_999, 0)).MyShare);

    [Fact]
    public void My_share_is_a_dash_without_my_samples() => Assert.Null(R(new RecordSample(60, "민수", 500_000_000, 0)).MyShare);
}
