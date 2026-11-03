using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>테스트 모드 강화 읽기 (reinforced-reader.plan.md): the cleanings, the soft reads, and the fight checks that turn a
/// look-alike digit on compressed video into no read. The sweep over damaged videos is ReinforcedSweep (env-gated).</summary>
public sealed class ReinforceTests
{
    static Image Filled(int w, int h, Func<int, int, byte> grey)
    {
        var img = new Image(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * img.Stride + x * 4;
                byte v = grey(x, y);
                (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]) = (v, v, v, 255);
            }
        return img;
    }

    [Fact]
    public void Levels_stretch_a_dim_capture_to_black_and_white_and_leave_a_flat_one()
    {
        var dim = Filled(100, 1, (x, _) => (byte)(64 + x * 128 / 99));
        var stretched = Reinforce.Levels(dim);
        Assert.True(stretched.Pixel(0, 0).B <= 5);
        Assert.True(stretched.Pixel(99, 0).B >= 250);
        var flat = Filled(10, 10, (_, _) => 90);
        Assert.Same(flat, Reinforce.Levels(flat));
    }

    [Fact]
    public void Clean_steepens_an_edge_and_keeps_a_flat_area()
    {
        var edge = Filled(9, 3, (x, _) => (byte)(x < 4 ? 50 : x == 4 ? 125 : 200));
        var clean = Reinforce.Clean(edge);
        Assert.True(clean.Pixel(3, 1).B < 50);    // the dark side darker by the edge
        Assert.True(clean.Pixel(5, 1).B > 200);   // the bright side brighter
        Assert.Equal(50, clean.Pixel(1, 1).B);
        Assert.Equal(200, clean.Pixel(7, 1).B);
    }

    [Theory]
    [InlineData("221109억", 221_109_000_000L)]    // the '.' and ',' faded
    [InlineData("2,211.09억", 221_109_000_000L)]
    [InlineData("598577만", 59_857_700L)]
    [InlineData("12345", 12_345L)]
    public void A_soft_amount_puts_back_the_marks_the_format_has(string text, long value) =>
        Assert.Equal(value, JeonbunReader.SoftAmount(text));

    [Theory]
    [InlineData("12억")]     // two decimals and no whole part
    [InlineData("12%34억")]  // a stray glyph
    [InlineData("")]
    public void A_soft_amount_still_needs_the_format(string text) => Assert.Null(JeonbunReader.SoftAmount(text));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_clean_panel_reads_the_same_soft(int i)
    {
        var panel = Fixture.Load($"jeonbun_{i}").Frames[0];
        Assert.Equal(JeonbunReader.Read(panel), JeonbunReader.ReadSoft(panel));
    }

    static (JeonbunReading?, bool) At(int timer, long damage) => (new JeonbunReading(damage, 100, timer, false), false);
    static JeonbunReading Is(int timer, long damage) => new(damage, 100, timer, false);
    static readonly (JeonbunReading?, bool) None = (null, false);
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void The_soft_panel_takes_the_clock_s_timer_for_a_look_alike_and_drops_a_falling_damage()
    {
        var soft = new JeonbunSoftReader();
        Assert.Equal(Is(210, 100), soft.Step(_ => [At(210, 100)], S(0)));
        Assert.Equal(Is(211, 120), soft.Step(_ => [At(219, 120)], S(1)));   // a 1 read as 9: the clock's 211
        Assert.Null(soft.Step(_ => [At(212, 70)], S(2)));                   // 120 → 70: a look-alike digit
        Assert.Equal(Is(213, 130), soft.Step(_ => [None, At(213, 130)], S(3)));
        Assert.Equal(Is(214, 140), soft.Step(e => e == 214 ? [At(214, 140)] : [], S(4)));   // the expected timer is given to the read
    }

    [Fact]
    public void The_soft_panel_follows_a_new_fight_once_it_ticks()
    {
        var soft = new JeonbunSoftReader();
        soft.Step(_ => [At(100, 500)], S(0));
        Assert.Null(soft.Step(_ => [At(0, 0)], S(1)));                 // off the clock once, and the damage fell: no read
        Assert.Equal(Is(1, 10), soft.Step(_ => [At(1, 10)], S(2)));    // it ticked: a new fight
        Assert.Equal(Is(2, 20), soft.Step(_ => [At(2, 20)], S(3)));
    }

    [Fact]
    public void The_soft_panel_follows_a_stopped_timer_after_two_seconds()
    {
        var soft = new JeonbunSoftReader();
        soft.Step(_ => [At(50, 900)], S(0));
        soft.Step(_ => [At(50, 900)], S(1));   // on the clock still (the second may be turning)
        Assert.Equal(52, soft.Step(_ => [At(50, 900)], S(2.5))!.Timer);
        Assert.Equal(53, soft.Step(_ => [At(50, 900)], S(3.5))!.Timer);
        Assert.Equal(50, soft.Step(_ => [At(50, 900)], S(4.5))!.Timer);   // held 2 s: stopped
        Assert.Null(soft.Step(_ => [At(50, 300)], S(5.5)));   // the same fight: its damage still can't fall
    }

    [Fact]
    public void A_timer_from_the_clock_itself_does_not_move_the_clock()
    {
        var soft = new JeonbunSoftReader();
        soft.Step(_ => [At(10, 1)], S(0.9));   // shown 10 at 0.9 s: the clock's offset is in (9.1, 10.1]
        for (int i = 1; i <= 6; i++)                  // unread timers: the reads take the clock's own
            soft.Step(e => [(new JeonbunReading(1 + i, 100, e!.Value, false), true)], S(0.9 + i * 0.5));
        Assert.Equal(Is(14, 9), soft.Step(_ => [At(14, 9)], S(4.95)));   // a real read there still fits
    }

    const long Max = 1_194_141_353_268;

    [Fact]
    public void The_soft_boss_hp_falls_and_takes_a_jump_only_when_read_twice()
    {
        var hp = new BossHpSoftReader();
        Assert.Equal((500_000_000_000L, Max, 0L), hp.Check((500_000_000_000, Max, 0), S(0)));
        Assert.Equal((499_000_000_000L, Max, 0L), hp.Check((499_000_000_000, Max, 0), S(0.5)));
        Assert.Null(hp.Check((499_500_000_000, Max, 0), S(1)));        // up: a look-alike
        Assert.Null(hp.Check((199_000_000_000, Max, 0), S(1.5)));      // 3 → 1 in a high digit: far over a party's damage
        Assert.Null(hp.Check((800_000_000_000, 900_000_000_000, 0), S(2)));   // another max once
        Assert.Equal((799_800_000_000L, 900_000_000_000L, 0L), hp.Check((799_800_000_000, 900_000_000_000, 0), S(2.5)));   // twice: a new boss
    }

    [Fact]
    public void Without_a_slash_the_current_hp_is_the_digits_before_the_known_max()
    {
        var last = (484_800_000_000L, Max, S(0));
        Assert.Equal(484_736_704_287L, BossHpSoftReader.ByMax(["48473670428" + "7" + Max], last, S(0.5)));   // the '/' lost
        Assert.Equal(484_736_704_287L, BossHpSoftReader.ByMax(["484736704287" + "1" + Max], last, S(0.5)));  // the '/' read as 1
        Assert.Null(BossHpSoftReader.ByMax(["999" + Max], last, S(0.5)));   // nothing that fits the fight
        Assert.Null(BossHpSoftReader.ByMax(["484736704287"], last, S(0.5)));   // no max
    }
}
