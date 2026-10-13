using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>--check-submission: a user's files for one 직업각인 (reference/classes/&lt;클래스&gt;/&lt;직업각인&gt;/) checked against its
/// _NEED.md before anything is built, each problem with the Korean line to send back (CLASS_ADDING.md, Follow-up lines).</summary>
public class SubmissionTests
{
    const string Need = """
        # 창술사 · 절정  (tier A, level 0)

        | File | Required | What to capture |
        |---|---|---|
        | `gauge_empty.png` | yes | full-screen PNG, gauge empty, all skills ready |
        | `gauge_full.png` | yes | full-screen PNG, gauge full, all skills ready |
        | `icon_crop_ready.png` | yes | crop of the 초각스 icon off cooldown |
        | `extra_two_thirds.png` | optional | full-screen PNG with 2 segments filled |
        """;

    static readonly IReadOnlyList<NeedRow> Rows = Submission.ParseNeed(Need);
    static Image WithBar() => ReferenceShotTests.Screen("band_110", 1920, 1080, new Viewport(0, 0, 1920, 1080));
    static Image NoBar() => new(1920, 1080);
    static Image Icon() => new(60, 60);

    static SubmissionNote For(IReadOnlyList<SubmissionNote> notes, string file) => Assert.Single(notes, n => n.File == file);

    [Fact]
    public void The_need_table_gives_each_file_whether_it_is_required_and_whether_it_is_a_full_screenshot()
    {
        Assert.Equal(
            [new("gauge_empty.png", true, true), new("gauge_full.png", true, true), new("icon_crop_ready.png", true, false), new("extra_two_thirds.png", false, true)],
            Rows);
    }

    [Fact]
    public void A_complete_set_of_png_originals_with_the_skill_bar_passes()
    {
        var notes = Submission.Check([new("gauge_empty.png", true, WithBar()), new("gauge_full.png", true, WithBar()), new("icon_crop_ready.png", true, Icon())], Rows);
        Assert.All(notes, n => Assert.True(n.Ok, n.Text));
        Assert.True(For(notes, "extra_two_thirds.png").Ok);   // optional and missing: noted, not a problem
    }

    [Fact]
    public void A_jpg_is_refused_with_the_line_asking_for_png_originals()
    {
        var n = For(Submission.Check([new("gauge_empty.png", false, WithBar())], Rows), "gauge_empty.png");
        Assert.False(n.Ok);
        Assert.Equal(Submission.AskPng, n.FollowUp);
    }

    [Fact]
    public void A_full_screenshot_without_a_findable_skill_bar_is_refused_but_an_icon_crop_is_not_searched()
    {
        var notes = Submission.Check([new("gauge_full.png", true, NoBar()), new("icon_crop_ready.png", true, Icon())], Rows);
        Assert.Equal(Submission.AskSkillBar, For(notes, "gauge_full.png").FollowUp);
        Assert.True(For(notes, "icon_crop_ready.png").Ok);
    }

    [Fact]
    public void Missing_required_files_ask_for_the_gauge_pair_or_the_icon()
    {
        var notes = Submission.Check([], Rows);
        Assert.Equal(Submission.AskGaugePair, For(notes, "gauge_empty.png").FollowUp);
        Assert.Equal(Submission.AskIcon, For(notes, "icon_crop_ready.png").FollowUp);
        Assert.False(For(notes, "gauge_full.png").Ok);
    }

    [Fact]
    public void Repeats_named_as_need_md_asks_count_and_each_is_checked()
    {
        var notes = Submission.Check([new("gauge_empty-1.png", true, WithBar()), new("gauge_empty-2.png", false, WithBar())], Rows);
        Assert.True(For(notes, "gauge_empty-1.png").Ok);
        Assert.Equal(Submission.AskPng, For(notes, "gauge_empty-2.png").FollowUp);
        Assert.DoesNotContain(notes, n => n.File == "gauge_empty.png");   // not reported missing
    }

    [Fact]
    public void A_clip_is_noted_for_the_effects_check()
    {
        var n = For(Submission.Check([new("절정.mp4", false, null)], Rows), "절정.mp4");
        Assert.True(n.Ok);
        Assert.Contains("--trace-gauge", n.Text);
    }

    [Theory]
    [InlineData("광전사의 비기", "광전사의비기")]
    [InlineData("광전사의비기", "광전사의비기")]
    [InlineData("광전사", null)]
    public void Folder_names_match_the_profiles_ignoring_spaces(string folder, string? canonical) =>
        Assert.Equal(canonical, Submission.Canonical(folder, ["광기", "광전사의비기"]));
}
