using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class NameOcrPaceTests
{
    const long Hp = 7_752_830_141;

    static NameOcrPace Missed(int times, long? hp)
    {
        var p = NameOcrPace.Start;
        for (int i = 0; i < times; i++) p = p.Read(missed: true, hp);
        return p;
    }

    [Fact]
    public void Reads_every_second_before_any_miss()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), NameOcrPace.Start.Every(Hp));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(20, 30)]
    public void Backs_off_while_the_same_boss_keeps_missing(int misses, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), Missed(misses, Hp).Every(Hp));
    }

    [Fact]
    public void Another_max_hp_reads_every_second_again()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), Missed(6, Hp).Every(Hp + 1));
    }

    [Fact]
    public void An_unread_max_hp_never_backs_off()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), Missed(6, null).Every(null));
    }

    [Fact]
    public void A_found_gate_starts_over()
    {
        Assert.Equal(NameOcrPace.Start, Missed(6, Hp).Read(missed: false, Hp));
    }

    [Fact]
    public void A_miss_on_another_boss_counts_from_one()
    {
        Assert.Equal(new NameOcrPace(1, Hp + 1), Missed(6, Hp).Read(missed: true, Hp + 1));
    }
}
