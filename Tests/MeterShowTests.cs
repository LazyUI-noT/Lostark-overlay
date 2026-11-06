using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>미터기 and 버프 적용률: 끄기, 표시 안 함 (worked out, no window), 켜기. 버프 → 적용률 → 타임라인.</summary>
public class MeterShowTests
{
    static MeterSettings With(ThreeWay meter, ThreeWay wish)
    {
        var m = new MeterSettings();
        m.SetMode(meter);
        m.SetBuffWish(wish);
        return m;
    }

    [Theory]
    [InlineData(false, true, ThreeWay.끄기)]
    [InlineData(false, false, ThreeWay.끄기)]
    [InlineData(true, false, ThreeWay.표시안함)]
    [InlineData(true, true, ThreeWay.켜기)]
    public void The_mode_reads_older_files(bool on, bool show, ThreeWay expected) =>
        Assert.Equal(expected, new MeterSettings { On = on, Show = show }.Mode());

    [Fact]
    public void Off_keeps_the_window_choice_for_when_it_comes_back()
    {
        var m = new MeterSettings();
        m.SetMode(ThreeWay.표시안함);
        m.SetMode(ThreeWay.끄기);
        Assert.False(m.On);
        m.SetMode(ThreeWay.표시안함);
        Assert.Equal((true, false), (m.On, m.Show));
    }

    [Fact]
    public void An_older_file_without_the_bars_still_feeds_the_timeline()
    {
        var m = new MeterSettings { On = true, BuffBars = false };
        Assert.Equal(ThreeWay.표시안함, m.BuffShare());
    }

    [Fact]
    public void Meter_off_turns_the_share_off() =>
        Assert.Equal(ThreeWay.끄기, With(ThreeWay.끄기, ThreeWay.켜기).BuffShare());

    [Fact]
    public void Meter_without_its_window_can_not_show_the_share() =>
        Assert.Equal(ThreeWay.표시안함, With(ThreeWay.표시안함, ThreeWay.켜기).BuffShare());

    [Fact]
    public void The_share_off_stays_off_whatever_the_meter()
    {
        Assert.Equal(ThreeWay.끄기, With(ThreeWay.켜기, ThreeWay.끄기).BuffShare());
        Assert.Equal(ThreeWay.끄기, With(ThreeWay.표시안함, ThreeWay.끄기).BuffShare());
    }

    [Fact]
    public void The_users_pick_comes_back_with_the_meter()
    {
        var m = With(ThreeWay.끄기, ThreeWay.켜기);
        m.SetMode(ThreeWay.켜기);
        Assert.Equal(ThreeWay.켜기, m.BuffShare());
    }

    [Fact]
    public void The_choices_follow_the_meter()
    {
        Assert.Equal([ThreeWay.끄기], With(ThreeWay.끄기, ThreeWay.켜기).BuffChoices());
        Assert.Equal([ThreeWay.끄기, ThreeWay.표시안함], With(ThreeWay.표시안함, ThreeWay.켜기).BuffChoices());
        Assert.Equal([ThreeWay.끄기, ThreeWay.표시안함, ThreeWay.켜기], With(ThreeWay.켜기, ThreeWay.켜기).BuffChoices());
    }
}
