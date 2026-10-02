using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The class from the skill bar's centre piece (ClassEmblem): each gauge class's empty and full shots (reference/
/// subclass, HUD 110) against 1 fps crops of other recordings of it: 전태.mp4 and 전태 버그 아덴키.mp4 (워로드),
/// buff_gameplay.mp4 and 발키리아덴버그.mp4 (발키리), bard bug.mp4, 절실한구원gameplay.mp4 and 4k_down.mp4 (바드, blurred),
/// 도화가bug_aden.mp4 and 도화가support.mp4 (도화가). The class played scores highest on every frame; skill effects
/// and a tooltip over 바드's harp bring its lead down to 0.03.</summary>
public class ClassEmblemTests
{
    static readonly string[] Classes = ["워로드", "발키리", "바드", "도화가"];
    static readonly string Dir = Path.Combine(Helpers.RepoRoot(), "Tests", "Fixtures");

    static readonly List<ClassEmblem> Refs = [.. Directory.GetFiles(Dir, "emblemref_*empty.json").Select(Path.GetFileNameWithoutExtension)
        .Select(e => new ClassEmblem(Classes.First(c => e!.Contains(c)), Fixture.Load(e!).Frames[0], Fixture.Load(e!.Replace("empty", "full")).Frames[0]))];

    public static TheoryData<string> Plays() => [.. Directory.GetFiles(Dir, "emblemplay_*.json").Select(p => Path.GetFileNameWithoutExtension(p)!)];

    static string ClassOf(string play) => Classes.First(play.Contains);

    [Theory]
    [MemberData(nameof(Plays))]
    public void The_class_played_is_never_taken_for_another(string play)
    {
        var frames = Fixture.Load(play).Frames;
        for (int i = 0; i < frames.Count; i++)
            Assert.True(ClassEmblemMatcher.Other(frames[i], ClassOf(play), Refs) is null, $"{play} {i} s");
    }

    /// <summary>Set to another gauge class, the class played is told on nearly every capture. The misses are 바드 frames under skill
    /// effects or a tooltip; the capture after a 스킬바 맞추기, the player idle, has none, and a miss only
    /// delays the warning to the next capture.</summary>
    [Theory]
    [MemberData(nameof(Plays))]
    public void Another_class_set_is_told(string play)
    {
        var frames = Fixture.Load(play).Frames;
        foreach (var set in Classes.Where(c => c != ClassOf(play)))
        {
            int told = frames.Count(f => ClassEmblemMatcher.Other(f, set, Refs) == ClassOf(play));
            Assert.True(told >= frames.Count * 0.9, $"{play} set to {set}: {told}/{frames.Count}");
        }
    }

    [Fact]
    public void A_class_with_no_reference_is_not_checked() =>
        Assert.Null(ClassEmblemMatcher.Other(Fixture.Load("emblemplay_도화가_bug").Frames[0], "소서리스", Refs));
}

public class ClassEmblemShownTests
{
    static readonly string[] Classes = ["워로드", "발키리", "바드", "도화가"];
    static readonly string Dir = Path.Combine(Helpers.RepoRoot(), "Tests", "Fixtures");
    static readonly List<ClassEmblem> Refs = [.. Directory.GetFiles(Dir, "emblemref_*empty.json").Select(Path.GetFileNameWithoutExtension)
        .Select(e => new ClassEmblem(Classes.First(c => e!.Contains(c)), Fixture.Load(e!).Frames[0], Fixture.Load(e!.Replace("empty", "full")).Frames[0]))];

    public static TheoryData<string> Plays() => [.. Directory.GetFiles(Dir, "emblemplay_*.json").Select(p => Path.GetFileNameWithoutExtension(p)!)];

    /// <summary>With no class to compare against (a class without a centre piece set, e.g. 홀리나이트 picked by mistake),
    /// the class played is told on most frames and another one never.</summary>
    [Theory]
    [MemberData(nameof(Plays))]
    public void The_class_shown_is_the_class_played_or_none(string play)
    {
        var own = Classes.First(play.Contains);
        var frames = Fixture.Load(play).Frames;
        var shown = frames.Select(f => ClassEmblemMatcher.Shown(f, Refs)).ToList();
        Assert.All(shown, s => Assert.True(s is null || s == own, $"{play}: {s}"));
        Assert.True(shown.Count(s => s == own) >= frames.Count * 0.5, $"{play}: {shown.Count(s => s == own)}/{frames.Count}");
    }

    /// <summary>The 4K stills (native and forced 21:9, HUD 100) against the HUD 110 1080p references: the crop is two and
    /// one and a half times their size, and still tells 바드.</summary>
    [Theory]
    [InlineData("emblem_4k_바드")]
    [InlineData("emblem_4k219_바드")]
    public void A_4k_still_shows_바드(string still) =>
        Assert.Equal("바드", ClassEmblemMatcher.Shown(Fixture.Load(still).Frames[0], Refs));

    /// <summary>In town the game greys the skill bar out (greyed out.png); the centre piece keeps its shapes and still tells 워로드.</summary>
    [Fact]
    public void A_greyed_out_bar_still_shows_its_class() =>
        Assert.Equal("워로드", ClassEmblemMatcher.Shown(Fixture.Load("emblem_greyed").Frames[0], Refs));

    [Fact]
    public void Another_character_s_skill_bar_shows_no_gauge_class()
    {
        // emblem_*: a character recorded before the gauge classes; its centre piece is none of theirs.
        foreach (var f in new[] { "emblem_Nocd", "emblem_cd", "emblem_mainfocus" })
            Assert.Null(ClassEmblemMatcher.Shown(Fixture.Load(f).Frames[0], Refs));
    }
}
