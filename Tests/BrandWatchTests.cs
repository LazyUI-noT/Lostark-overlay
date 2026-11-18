using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>낙인's bar (BrandWatch): icon only, full while seen, gone <see cref="BrandWatch.Grace"/> after the last sighting.</summary>
public class BrandWatchTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Seen_turns_the_bar_on_full_with_no_end()
    {
        var w = new BrandWatch();
        var b = w.Step(BrandIcon.바드, S(1));
        Assert.Equal(BuffKind.낙인, b.Kind);
        Assert.True(b.Active);
        Assert.Null(b.EndsAt);
        Assert.Equal(1, b.Progress(S(5)));
        Assert.Equal(S(1), b.ChangedAt);
    }

    [Fact]
    public void Seen_again_keeps_when_it_started()
    {
        var w = new BrandWatch();
        w.Step(BrandIcon.도화가, S(1));
        Assert.Equal(S(1), w.Step(BrandIcon.도화가, S(1.5)).ChangedAt);
    }

    [Fact]
    public void Unseen_within_the_grace_stays_on_then_goes()
    {
        var w = new BrandWatch();
        w.Step(BrandIcon.홀나발키리, S(1));
        Assert.True(w.Step(null, S(1 + BrandWatch.Grace.TotalSeconds)).Active);
        var off = w.Step(null, S(1.1 + BrandWatch.Grace.TotalSeconds));
        Assert.False(off.Active);
        Assert.Equal(S(1.1 + BrandWatch.Grace.TotalSeconds), off.ChangedAt);
    }

    [Fact]
    public void Expire_ends_it_with_no_read_after_the_grace()
    {
        var w = new BrandWatch();
        w.Step(BrandIcon.바드, S(1));
        Assert.True(w.Expire(S(2)).Active);
        Assert.False(w.Expire(S(3)).Active);
        Assert.False(w.Expire(S(4)).Active);
    }

    [Fact]
    public void Never_seen_is_absent()
    {
        var w = new BrandWatch();
        Assert.False(w.Step(null, S(1)).Active);
        Assert.False(w.Expire(S(9)).Active);
    }

    [Fact]
    public void Reads_twice_a_second()
    {
        var w = new BrandWatch();
        Assert.True(w.Due(S(1)));
        w.Step(null, S(1));
        Assert.False(w.Due(S(1.4)));
        Assert.True(w.Due(S(1.5)));
    }

    [Fact]
    public void Reads_the_shots_strip()
    {
        // nakin_holy as the engine captures it: the strip of 1920×1080, HUD 110, 보스 디버프 크기 100.
        var vp = new Viewport(0, 0, 1920, 1080);
        var fit = new Fit(110);
        var s = Geometry.BossDebuffStrip(vp, fit, 100);
        var strip = Fixture.Load("nakin_holy").Frames[0].Crop(new PixelRect(s.X - 700, s.Y - 100, Math.Min(s.Width, 600 - (s.X - 700)), s.Height));
        var w = new BrandWatch();
        Assert.Equal(0, w.Filled);
        Assert.True(w.Read(AmsuLayout.Of(vp, fit, 100), strip, S(1)).Active);
        Assert.Equal(BrandIcon.홀나발키리, w.Icon);
        Assert.True(w.Filled > 0);   // the 읽는 자리 box: any debuff in the row
    }

    // Timed 낙인 (boss-row-count.plan.md, phase 3): "N초" under the icon, trusted once two reads agree.
    static BuffState Seen(BrandWatch w, double secs, double at, bool full = false, bool seen = true) => w.Step(BrandIcon.바드, secs, full, seen, S(at));

    [Fact]
    public void One_read_stays_icon_only()
    {
        var w = new BrandWatch();
        Assert.Null(Seen(w, 15, 0).EndsAt);
    }

    [Fact]
    public void Two_agreeing_reads_make_it_timed_with_the_fitted_length()
    {
        var w = new BrandWatch();
        Seen(w, 15, 0);
        var b = Seen(w, 14, 1);
        Assert.Equal(S(16), b.Total);
        Assert.Equal(15, b.Remaining(S(1)).TotalSeconds, 1);
    }

    [Fact]
    public void A_seventeen_is_an_eighteen_second_one()
    {
        var w = new BrandWatch();
        Seen(w, 17, 0);
        Assert.Equal(S(18), Seen(w, 16, 1).Total);
    }

    [Fact]
    public void A_lost_tens_digit_is_restored_from_the_countdown()
    {
        var w = new BrandWatch();
        Seen(w, 15, 0);
        Seen(w, 14, 1);
        var b = Seen(w, 3, 12);   // "13" read as "3"
        Assert.Equal(S(16), b.EndsAt);
    }

    [Fact]
    public void Disagreeing_reads_never_make_it_timed()
    {
        var w = new BrandWatch();
        Seen(w, 15, 0);
        Seen(w, 9, 0.5);
        Assert.Null(Seen(w, 15, 1).EndsAt);
    }

    [Fact]
    public void A_timed_icon_gone_from_a_full_row_counts_to_its_end()
    {
        var w = new BrandWatch();
        Seen(w, 15, 0);
        Seen(w, 14, 1);
        var b = w.Step(null, null, full: true, seen: true, S(5));
        Assert.True(b.Active);
        Assert.False(w.Step(null, null, true, true, S(16.1)).Active);
    }

    [Fact]
    public void A_timed_icon_gone_from_a_row_with_room_ends_after_the_grace()
    {
        var w = new BrandWatch();
        Seen(w, 15, 0);
        Seen(w, 14, 1);
        w.Step(null, null, false, true, S(2));
        Assert.True(w.Expire(S(2.5)).Active);
        Assert.False(w.Step(null, null, false, true, S(3.2)).Active);
    }

    [Fact]
    public void A_timed_icon_in_an_unseen_row_counts_on()
    {
        var w = new BrandWatch();
        Seen(w, 15, 0);
        Seen(w, 14, 1);
        w.Step(null, null, false, false, S(2));
        Assert.True(w.Expire(S(10)).Active);
        Assert.False(w.Expire(S(16.1)).Active);
    }

    [Fact]
    public void An_untimed_icon_keeps_the_grace_even_in_a_full_row()
    {
        var w = new BrandWatch();
        w.Step(BrandIcon.바드, null, true, true, S(0));
        Assert.False(w.Step(null, null, true, true, S(2)).Active);
    }
}
