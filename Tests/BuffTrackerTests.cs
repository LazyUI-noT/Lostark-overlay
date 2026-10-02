using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class BuffTrackerTests
{
    static TimeSpan T(double s) => TimeSpan.FromSeconds(s);
    static BuffReading R(BuffKind k, double? s, double at) => new(k, s, T(at));
    static BuffState None(BuffKind k) => BuffState.Absent(k, TimeSpan.Zero);

    /// <summary>Feeds one read per (time, readings) step, HUD visible.</summary>
    static BuffState Run(BuffKind k, params (double At, double?[] Seconds)[] steps) =>
        steps.Aggregate(None(k), (s, st) => BuffTracker.Update(s, [.. st.Seconds.Select(v => R(k, v, st.At))], T(st.At), true));

    [Fact]
    public void A_new_buff_ends_within_the_second_its_timer_shows()
    {
        var s = Run(BuffKind.공증, (0, [8]));
        Assert.True(s.Active);
        Assert.InRange(s.EndsAt!.Value, T(8), T(9));
        Assert.Equal(T(8), s.Total);
        Assert.False(s.Estimated);
    }

    /// <summary>A 공증 lasts 8 s at most, so a whole "9" is "0.9" with its "0." lost (공증bug.mp4 on a bright floor).</summary>
    [Fact]
    public void A_공증_read_past_its_longest_is_tenths_with_the_point_lost()
    {
        var s = Run(BuffKind.공증, (0, [1]), (0.5, [1]), (1.0, [9]), (1.1, [0.8]));
        Assert.Equal(T(1), s.Total);
        Assert.InRange(s.Remaining(T(1.1)).TotalSeconds, 0.7, 0.9);
    }

    /// <summary>Tenths that read higher than held, short of a reset, are a one-read blip ("0.3" as "0.8", 도화가support
    /// (2).mp4 at 31.2 s) until a second read agrees.</summary>
    [Fact]
    public void Tenths_reading_higher_than_held_wait_for_a_second_read()
    {
        var s = Run(BuffKind.공증, (0, [1]), (0.5, [1]), (1.0, [0.4]), (1.1, [0.8]));
        Assert.InRange(s.EndsAt!.Value, T(1.39), T(1.41));
        s = BuffTracker.Update(s, [R(BuffKind.공증, 0.7, 1.2)], T(1.2), true);
        Assert.InRange(s.EndsAt!.Value, T(1.89), T(1.91));
    }

    [Fact]
    public void Two_공증_follow_the_longer_one() =>
        Assert.InRange(Run(BuffKind.공증, (0, [3, 7])).EndsAt!.Value, T(7), T(8));

    [Fact]
    public void The_end_narrows_when_the_digit_changes() =>
        Assert.InRange(Run(BuffKind.공증, (0, [8]), (0.5, [8]), (0.6, [7])).EndsAt!.Value, T(8.5), T(8.6));

    [Theory]
    [InlineData(BuffKind.아덴피증, 11, 12)]
    [InlineData(BuffKind.아덴피증, 14, 16)]
    [InlineData(BuffKind.아덴피증, 17, 20)]
    [InlineData(BuffKind.초각스피증, 13, 15)]
    [InlineData(BuffKind.뎀증, 19, 20)]
    public void Total_is_the_smallest_known_duration_that_fits(BuffKind k, double seen, double total) =>
        Assert.Equal(T(total), Run(k, (0, [seen])).Total);

    [Fact]
    public void A_missed_tens_digit_is_restored_from_the_countdown()
    {
        var s = Run(BuffKind.아덴피증, (0, [15]), (1.0, [4]));
        Assert.InRange(s.EndsAt!.Value, T(15), T(16));
        Assert.False(s.Estimated);
    }

    [Fact]
    public void A_single_read_that_jumps_away_is_ignored()
    {
        var before = Run(BuffKind.공증, (0, [7]));
        var s = BuffTracker.Update(before, [R(BuffKind.공증, 3, 0.1)], T(0.1), true);
        Assert.Equal(before.EndsAt, s.EndsAt);
        s = BuffTracker.Update(s, [R(BuffKind.공증, 7, 0.2)], T(0.2), true);
        Assert.Equal(before.EndsAt, s.EndsAt);
        Assert.Null(s.Pending);
    }

    [Fact]
    public void A_recast_is_taken_once_the_next_read_agrees()
    {
        var s = Run(BuffKind.공증, (0, [3]), (0.1, [8]));
        Assert.InRange(s.EndsAt!.Value, T(3), T(4));
        s = BuffTracker.Update(s, [R(BuffKind.공증, 8, 0.2)], T(0.2), true);
        Assert.InRange(s.EndsAt!.Value, T(8.2), T(9.2));
        Assert.Equal(T(0.2), s.ChangedAt);
        Assert.Equal(T(8), s.Total);
    }

    [Fact]
    public void A_recast_whose_tens_was_missed_settles_on_the_tens_read()
    {
        var s = Run(BuffKind.뎀증, (0, [16]), (10, [6]), (10.1, [9]), (10.2, [18]), (10.3, [8]));
        Assert.InRange(s.EndsAt!.Value, T(28.3), T(29.3));
        Assert.Equal(T(20), s.Total);
    }

    [Theory]
    [InlineData(BuffKind.아덴피증, 2, 12)]   // first read with the tens missed: the value that fits a known duration
    [InlineData(BuffKind.초각스피증, 9, 10)]   // "9" = 9 to 10 s left
    [InlineData(BuffKind.공증, 5, 6)]
    public void A_new_buff_takes_the_tens_that_fits_a_known_duration(BuffKind k, double shown, double seconds) =>
        Assert.Equal(T(seconds), Run(k, (0, [shown])).EndsAt);

    [Fact]
    public void A_buff_hidden_by_the_glare_resumes_with_its_total()
    {
        var s = Run(BuffKind.아덴피증, (0, [16]), (5, [11]), (5.1, []), (5.2, []), (5.5, [10]));
        Assert.True(s.Active);
        Assert.Equal(T(16), s.Total);
        Assert.InRange(s.EndsAt!.Value, T(15.5), T(16.5));
    }

    /// <summary>바드 and 도화가 also have a 공증 that lasts 1 s and is recast 8 times: its bar is 1 s long, not 1/8 of 8 s.</summary>
    [Fact]
    public void The_1s_공증_is_its_own_full_length()
    {
        var s = Run(BuffKind.공증, (0, [1]), (0.5, [0.5]), (1.0, [1]), (1.3, [0.7]));
        Assert.True(s.Active);
        Assert.Equal(T(1), s.Total);
        Assert.Equal(T(2.0), s.EndsAt);
    }

    /// <summary>Standing on the 1 s 공증's AoE as the 8 s one runs out (one icon): the first reset keeps the 8 s bar,
    /// so it drains to 0 without refilling; the second reset in a row is the AoE, and the bar becomes 1 s long.</summary>
    [Fact]
    public void The_8s_공증_keeps_its_bar_through_one_reset_and_the_second_makes_it_1s()
    {
        var last = Run(BuffKind.공증, (0, [7]), (7.5, [0.4]));
        Assert.Equal(T(8), last.Total);
        var s = BuffTracker.Update(last, [R(BuffKind.공증, 0.9, 7.9)], T(7.9), true);    // reset 1
        Assert.Equal((T(8), T(8.8)), (s.Total, s.EndsAt));
        s = BuffTracker.Update(s, [R(BuffKind.공증, 0.7, 8.1)], T(8.1), true);
        Assert.Equal(T(8), s.Total);
        s = BuffTracker.Update(s, [R(BuffKind.공증, 0.9, 8.8)], T(8.8), true);            // reset 2
        Assert.Equal((T(1), T(9.7)), (s.Total, s.EndsAt));
        s = BuffTracker.Update(s, [R(BuffKind.공증, 0.9, 9.7)], T(9.7), true);            // and it stays 1 s
        Assert.Equal((T(1), T(10.6)), (s.Total, s.EndsAt));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    public void A_공증_first_read_at_2s_or_more_is_the_8s_one(double shown) =>
        Assert.Equal(T(8), Run(BuffKind.공증, (0, [shown])).Total);

    /// <summary>"0.1초" read as 1 at the very end is that decimal too, not an AoE reset (도화가support.mp4 at 16.3 s).</summary>
    [Fact]
    public void A_last_tenth_read_as_1_is_not_a_reset() =>
        Assert.Equal(T(0.9), Run(BuffKind.공증, (0, [0.9]), (0.4, [0.5]), (0.7, [0.2]), (0.8, [1]), (0.9, [0])).EndsAt);

    /// <summary>A misread above every known duration (a 공증 read as "9") has no tens candidate: taken as read, not a crash.</summary>
    [Fact]
    public void A_first_read_above_every_known_duration_is_taken_as_read() =>
        Assert.True(Run(BuffKind.공증, (0, [9])).Active);

    [Fact]
    public void A_decimal_timer_is_used_as_is() =>
        Assert.Equal(T(1.4), Run(BuffKind.공증, (1, [0.4])).EndsAt);

    [Fact]
    public void An_unreadable_timer_keeps_counting_estimated()
    {
        var read = Run(BuffKind.공증, (0, [8]));
        var s = BuffTracker.Update(read, [R(BuffKind.공증, null, 0.1)], T(0.1), true);
        Assert.True(s.Active);
        Assert.True(s.Estimated);
        Assert.Equal(read.EndsAt, s.EndsAt);
    }

    [Fact]
    public void A_buff_never_read_shows_full()
    {
        var s = Run(BuffKind.뎀증, (0, [null]));
        Assert.True(s.Active);
        Assert.Equal(1, s.Progress(T(5)));
    }

    [Fact]
    public void The_other_공증_unreadable_keeps_the_longer_estimate()
    {
        var s = Run(BuffKind.공증, (0, [7, 3]), (0.1, [null, 3]), (0.2, [null, 3]));
        Assert.InRange(s.EndsAt!.Value, T(7), T(8));
    }

    [Fact]
    public void A_buff_goes_absent_after_two_misses()
    {
        var s = Run(BuffKind.공증, (0, [8]), (0.1, []));
        Assert.True(s.Active);
        Assert.False(Run(BuffKind.공증, (0, [8]), (0.1, []), (0.2, [])).Active);
    }

    [Fact]
    public void A_hidden_HUD_freezes_the_buff_as_estimated()
    {
        var s = Run(BuffKind.공증, (0, [8]));
        for (int i = 1; i <= 5; i++) s = BuffTracker.Update(s, [], T(i * 0.1), false);
        Assert.True(s.Active);
        Assert.True(s.Estimated);
        s = BuffTracker.Update(s, [], T(0.6), true);   // one miss after the HUD returns is not enough
        Assert.True(s.Active);
    }

    /// <summary>A labelled clip end to end, every fixture frame through BuffReader then BuffTracker: one frame after every
    /// labelled second (a held jump needs a second read; 0.5 s apart in buff_80_to_110 against 100 ms in the app), each
    /// buff shown is active and its remaining time is within a second or so of the longest timer shown for it. Covers
    /// the missed tens and the one-read blips (BuffSizeTests.KnownWrong).</summary>
    [Theory]
    [InlineData("buff_80_to_110")]
    [InlineData("buff_gameplay")]
    public void Replays_a_labelled_clip(string clip)
    {
        var (fixture, fps, labels, timers) = clip == "buff_gameplay"
            ? (BuffReaderTests.Bg.Fixture, 5, BuffReaderTests.BgLabels, BuffReaderTests.BgTimers)
            : (BuffSizeTests.B80.Fixture, 2, BuffSizeTests.Labels, BuffSizeTests.Timers);
        var f = Fixture.Load(fixture);
        var states = Enum.GetValues<BuffKind>().Select(None).ToArray();
        var wrong = new List<string>();
        for (int i = 0; i < f.Frames.Count && i / fps < labels.Length; i++)
        {
            int second = i / fps;
            var src = clip == "buff_gameplay" ? BuffReaderTests.Bg : BuffSizeTests.At(second);
            var reader = new Core.Vision.BuffReader(Core.Vision.Geometry.BuffScale(src.Vp, src.Buff));
            var now = T((double)i / fps);
            var readings = BuffReaderTests.Boxes(src)
                .Select(b => (b, kind: Core.Vision.BuffReader.Classify(f.Frames[i], b.Icon)))
                .Where(x => x.kind is not null)
                .Select(x => new BuffReading(x.kind!.Value, reader.ReadTimer(f.Frames[i], x.b.Timer), now)).ToList();
            states = [.. states.Select(s => BuffTracker.Update(s, readings, now, true))];
            if (i % fps != 1) continue;

            var shown = labels[second].Where(c => BuffReaderTests.Label(c) is not null)
                .Zip(timers[second],(c, v) => (kind: BuffReaderTests.Label(c)!.Value, v, upper: char.IsUpper(c)));
            foreach (var g in shown.GroupBy(x => x.kind))
            {
                double want = g.Max(x => x.v);
                var s = states[(int)g.Key];
                if (double.IsNaN(want) || want < 1 || g.Any(x => !x.upper)) continue;   // under the glare it may be missed
                double left = s.Remaining(now).TotalSeconds + 1.0 / fps;
                if (!s.Active || s.EndsAt is null || left < want - 1 || left > want + 2) wrong.Add($"{second} s {g.Key}: shown {want}, tracked {(s.Active ? left.ToString("0.0") : "absent")}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join(" | ", wrong));
    }

    /// <summary>The 1 s 공증's AoE icon beside the 8 s one (two icons): the 8 s bar follows its own icon to 0, not the AoE's
    /// longer "1".</summary>
    [Fact]
    public void Beside_the_AoE_icon_the_8s_공증_drains_on_its_own_icon()
    {
        var s = Run(BuffKind.공증, (1.1, [7]), (7.1, [1]), (7.5, [1, 1]), (8.5, [0.5, 1]));
        Assert.Equal((T(8), T(9.0)), (s.Total, s.EndsAt));
    }

    /// <summary>도화가 clips (HUD 110, buff 120%, 10 fps; crop 600,870 260×90 for the 도화가support ones): the 공증 bar refills
    /// only at a real cast or stepping onto the AoE, never on a misread (a dropped "0.", the AoE icon's decimals beside
    /// the 8 s one). 도화가support: 8 s at 1.8 s, AoE at 11.3 s, 8 s at 16.8 s. (2): AoE at 3.1 s, 8 s at 11.5 s, AoE at 26.4 s and again at 29.5 s, 8 s at 33 s.
    /// 1sdoublebuff: 8 s at 1.1 s, AoE once the 8 s icon is gone (9.4 s).</summary>
    [Theory]
    [InlineData("buffrow_dohwa1", new[] { 1.8, 11.3, 16.8 })]
    [InlineData("buffrow_dohwa2", new[] { 3.1, 11.5, 26.4, 29.5, 33.0 })]
    [InlineData("buffrow_dbl", new[] { 1.1, 9.4 })]
    public void The_공증_bar_refills_only_at_real_casts(string fixture, double[] refills)
    {
        var f = Fixture.Load(fixture);
        var src = fixture == "buffrow_dbl" ? BuffReaderTests.Bg with { Fixture = fixture }
            : BuffReaderTests.Bg with { Fixture = fixture, Rect = new(600, 870, 260, 90) };
        var reader = new Core.Vision.BuffReader(Core.Vision.Geometry.BuffScale(src.Vp, src.Buff));
        var boxes = BuffReaderTests.Boxes(src);
        var s = None(BuffKind.공증);
        double last = 0;
        var seen = new List<double>();
        for (int i = 0; i < f.Frames.Count; i++)
        {
            var now = T(i / 10.0);
            var readings = boxes.Select(b => (b, kind: Core.Vision.BuffReader.Classify(f.Frames[i], b.Icon))).Where(x => x.kind is not null)
                .Select(x => new BuffReading(x.kind!.Value, reader.ReadTimer(f.Frames[i], x.b.Timer), now)).ToList();
            s = BuffTracker.Update(s, readings, now, true);
            double p = s.EndsAt is null ? 0 : s.Progress(now);
            if (p - last > 0.3) seen.Add(Math.Round(i / 10.0, 1));
            last = p;
        }
        Assert.True(refills.Length == seen.Count, string.Join(", ", seen));
        Assert.All(refills.Zip(seen), x => Assert.InRange(x.Second, x.First - 0.3, x.First + 0.3));
    }

    /// <summary>1sdoublebuff.mp4 (도화가 party, set up as buff_gameplay) at 10 fps: an 8 s 공증 first read "7" at 1.1 s, the
    /// AoE's 1 s 공증 beside it from 7.5 s reading "1" while stood on, the 8 s icon at 0 by 9.0 s and gone at 9.4 s, the
    /// AoE one draining 0.9 → 0 from 11.0 s. The bar drains 8 → 0, then is a full 1 s bar, then drains 1 → 0.</summary>
    [Fact]
    public void The_8s_공증_then_the_AoE_replays_as_8s_then_1s()
    {
        var f = Fixture.Load("buffrow_dbl");
        var src = BuffReaderTests.Bg with { Fixture = "buffrow_dbl" };
        var reader = new Core.Vision.BuffReader(Core.Vision.Geometry.BuffScale(src.Vp, src.Buff));
        var boxes = BuffReaderTests.Boxes(src);
        var s = None(BuffKind.공증);
        var at = new Dictionary<double, BuffState>();
        for (int i = 0; i < f.Frames.Count; i++)
        {
            var now = T(i / 10.0);
            var readings = boxes.Select(b => (b, kind: Core.Vision.BuffReader.Classify(f.Frames[i], b.Icon))).Where(x => x.kind is not null)
                .Select(x => new BuffReading(x.kind!.Value, reader.ReadTimer(f.Frames[i], x.b.Timer), now)).ToList();
            s = BuffTracker.Update(s, readings, now, true);
            at[Math.Round(i / 10.0, 1)] = s;
        }
        Assert.Equal(T(8), at[5.0].Total);
        Assert.Equal(T(8), at[8.5].Total);
        Assert.InRange(at[8.5].Remaining(T(8.5)).TotalSeconds, 0.4, 0.7);   // its own icon, not the AoE's "1"
        Assert.Equal(T(1), at[9.8].Total);
        Assert.InRange(at[9.8].Progress(T(9.8)), 0.9, 1);                   // stood on: full
        Assert.Equal(T(1), at[11.5].Total);
        Assert.InRange(at[11.5].Remaining(T(11.5)).TotalSeconds, 0.3, 0.6);
    }

    /// <summary>공증bug.mp4 (HUD 100, buff 120%, the 도화가 crop) at 10 fps: the 8 s 공증 at "1초" from 2.2 s when the AoE's icon joins it at 2.6 s;
    /// the 8 s one drains 0.9 → 0 over 3.2–4.2 s and goes; the AoE one shows "1초" while stood on until 6.6 s, then
    /// drains 0.9 → 0 by 7.4 s. The bar follows the 8 s icon down, then is a full 1 s bar, then drains.</summary>
    [Fact]
    public void The_AoE_cast_near_the_end_of_the_8s_공증_becomes_the_1s_bar()
    {
        var f = Fixture.Load("buffrow_gzbug");
        var src = BuffReaderTests.Bg with { Fixture = "buffrow_gzbug", Rect = new(600, 870, 260, 90), Fit = new Core.Domain.Fit(100) };
        var reader = new Core.Vision.BuffReader(Core.Vision.Geometry.BuffScale(src.Vp, src.Buff));
        var boxes = BuffReaderTests.Boxes(src);
        var s = None(BuffKind.공증);
        var at = new Dictionary<double, BuffState>();
        var trace = new List<string>();
        for (int i = 0; i < f.Frames.Count; i++)
        {
            var now = T(i / 10.0);
            var readings = boxes.Select(b => (b, kind: Core.Vision.BuffReader.Classify(f.Frames[i], b.Icon))).Where(x => x.kind is not null)
                .Select(x => new BuffReading(x.kind!.Value, reader.ReadTimer(f.Frames[i], x.b.Timer), now)).ToList();
            s = BuffTracker.Update(s, readings, now, true);
            at[Math.Round(i / 10.0, 1)] = s;
            trace.Add($"{i / 10.0:0.0}: [{string.Join(",", readings.Where(r => r.Kind == BuffKind.공증).Select(r => r.Seconds))}] " +
                      $"{(s.Active ? "on" : "off")} total {s.Total?.TotalSeconds} left {s.Remaining(now).TotalSeconds:0.00}");
        }
        string log = string.Join("\n", trace);
        Assert.True(at[3.5].Total == T(8), log);
        Assert.True(at[3.5].Remaining(T(3.5)).TotalSeconds is >= 0.3 and <= 0.7, log);   // its own icon
        foreach (double t in new[] { 5.0, 5.5, 6.0 })
        {
            Assert.True(at[t].Active && at[t].Total == T(1), $"{t}\n{log}");
            Assert.True(at[t].Progress(T(t)) >= 0.9, $"{t}\n{log}");                        // stood on: full
        }
        Assert.True(at[7.0].Total == T(1) && at[7.0].Remaining(T(7.0)).TotalSeconds is >= 0.2 and <= 0.6, log);
    }
}
