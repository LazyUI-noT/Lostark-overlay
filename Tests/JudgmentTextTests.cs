using System.Diagnostics;
using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The white "심판" over the character (단심, user 2026-10-13): the backup when the icon is off the buff row.
/// Fixtures: 440×300 round the character at 1080p (make-fixtures.ps1, 단심), one 심판 per 데미지 폰트 size.</summary>
[Collection(nameof(VisionReplayTests))]
public class JudgmentTextTests
{
    // judg_text: 100%, 48.0–49.5 s at 10 fps; it fades in large at 48.18 s and reads from 48.4 s (frame 4) to 49.1 s (11).
    // judg_text_75 … _300: 1.6 s round the 심판 of 단심75%.mp4 … 단심300%.mp4 at 5 fps.
    [Theory]
    [InlineData("judg_text", 100, 3)]
    [InlineData("judg_text_75", 75, 2)]
    [InlineData("judg_text_150", 150, 2)]
    [InlineData("judg_text_200", 200, 2)]
    [InlineData("judg_text_300", 300, 2)]
    public void 심판_is_read_at_every_font_size(string fixture, int size, int atLeast)
    {
        var f = Fixture.Load(fixture);
        var found = Enumerable.Range(0, f.Frames.Count).Where(i => JudgmentText.Find(f.Frames[i], 1, size)).ToList();
        Assert.True(found.Count >= atLeast, $"{fixture}: found in frames [{string.Join(",", found)}] of {f.Frames.Count}");
    }

    [Fact]
    public void Nothing_before_or_after_the_100_percent_one()
    {
        var f = Fixture.Load("judg_text");
        Assert.All(new[] { 0, 1, 2, 12, 13, 14, 15 }, i => Assert.False(JudgmentText.Find(f.Frames[i], 1, 100), $"frame {i}"));
    }

    // 단죄 in the same font at 75–300% (read at its own size, so only the shape tells them apart), and 슈퍼차지 at every size.
    [Theory]
    [InlineData("dz_text_75", 75)]
    [InlineData("dz_text_100a", 100)]
    [InlineData("dz_text_100b", 100)]
    [InlineData("dz_text_150", 150)]
    [InlineData("dz_text_200", 200)]
    [InlineData("dz_text_200b", 200)]
    [InlineData("dz_text_300a", 300)]
    [InlineData("dz_text_300b", 300)]
    [InlineData("super_text", 75)]
    [InlineData("super_text", 100)]
    [InlineData("super_text", 150)]
    [InlineData("super_text", 200)]
    [InlineData("super_text", 300)]
    public void 단죄_and_other_text_is_never_심판(string fixture, int size)
    {
        Assert.All(Fixture.Load(fixture).Frames, img => Assert.False(JudgmentText.Find(img, 1, size)));
    }

    // Busy combat round the character, no 심판 (damage numbers at every size, 헤드 어택, 65, a small 단죄, effects), read at
    // every 데미지 폰트 크기: one false read would start 30 s of 단심 that nothing corrects.
    [Theory]
    [InlineData("busy_300a")]
    [InlineData("busy_300b")]
    [InlineData("busy_200")]
    [InlineData("busy_150")]
    [InlineData("busy_100a")]
    [InlineData("busy_100b")]
    public void Busy_combat_is_never_심판(string fixture)
    {
        var img = Fixture.Load(fixture).Frames[0];
        Assert.All(Core.Domain.JudgmentSettings.FontSizes, size => Assert.False(JudgmentText.Find(img, 1, size), $"at {size}%"));
    }

    [Fact]
    public void Read_through_the_tracker_the_text_alone_starts_one_cycle_near_the_trigger()
    {
        var f = Fixture.Load("judg_text");
        var s = JudgmentState.Ready;
        var triggers = new List<TimeSpan>();
        for (int i = 0; i < f.Frames.Count; i++)
        {
            var next = JudgmentTracker.Update(s, new(false, JudgmentText.Find(f.Frames[i], 1, 100)), f.Pts[i], hudVisible: true);
            if (next.TriggeredAt != s.TriggeredAt) triggers.Add(next.TriggeredAt!.Value);
            s = next;
        }
        Assert.Single(triggers);
        Assert.InRange(triggers[0].TotalSeconds, 48.17, 48.17 + 0.25);   // the first frames fade in too large and faint to read
    }

    [Fact]
    public void The_band_is_where_the_fixtures_were_cut_and_scales_with_the_viewport()
    {
        Assert.Equal(new PixelRect(740, 320, 440, 300), Geometry.JudgmentBand(Fhd));
        Assert.Equal(new PixelRect(1480, 640, 880, 600), Geometry.JudgmentBand(new Viewport(0, 0, 3840, 2160)));
        Assert.Equal(new PixelRect(740, 455, 440, 300), Geometry.JudgmentBand(new Viewport(0, 135, 1920, 1080)));
    }

    [Fact]
    public void A_band_is_read_in_a_few_milliseconds()
    {
        var img = Fixture.Load("judg_text").Frames[6];
        JudgmentText.Find(img, 1, 100);
        double best = double.MaxValue;   // the best of 5 runs: a loaded machine slows some, not all
        for (int run = 0; run < 5; run++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) JudgmentText.Find(img, 1, 100);
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / 10);
        }
        Assert.True(best < 5, $"{best:0.0} ms");
    }
}
