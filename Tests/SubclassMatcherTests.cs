using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The 직업각인 from the 초각스 slot: its icon differs per 직업각인 (Profiles/subclass, cut from the skill
/// window). Slot crops at 1 fps: buff_gameplay.mp4 (발키리 해방자, 1080p HUD 110, 54 px slot), 4k_down.mp4 (바드
/// 절실한구원 = 아리아, 37 px slot, blurred by the downscale), 전태.mp4 (워로드 전투태세), 도화가support.mp4 (도화가 만개; 회귀 is the purple icon).</summary>
public class SubclassMatcherTests
{
    static Dictionary<string, Image> Icons(params string[] names) => names.ToDictionary(n => n, n => Fixture.Load("icon_" + n).Frames[0]);

    static readonly Dictionary<string, Image> 발키리 = Icons("해방자", "빛의기사");
    static readonly Dictionary<string, Image> 바드 = Icons("절실한구원", "진실된용맹");
    // 고독한기사's icon is cut from the in-game slot (42 px), not the skill window.
    static readonly Dictionary<string, Image> 워로드 = Icons("전투태세", "고독한기사");
    static readonly Dictionary<string, Image> 도화가 = Icons("만개", "회귀");

    [Theory]
    [InlineData("subclass_valk", "해방자", new[] { 0, 1, 2, 16, 17, 18, 19 })]
    [InlineData("subclass_bard", "절실한구원", new[] { 0, 1, 2, 3, 4, 5 })]
    [InlineData("subclass_war", "전투태세", new[] { 0, 1, 2, 3, 4, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40 })]
    [InlineData("subclass_doh", "만개", new[] { 0, 1, 2, 3, 4 })]
    public void A_ready_초각스_picks_its_직업각인_and_never_the_other(string fixture, string want, int[] ready)
    {
        var icons = want switch { "해방자" => 발키리, "절실한구원" => 바드, "만개" => 도화가, _ => 워로드 };
        var frames = Fixture.Load(fixture).Frames;
        for (int i = 0; i < frames.Count; i++)
        {
            var got = SubclassMatcher.Pick(frames[i], icons);
            if (ready.Contains(i)) Assert.True(got == want, $"{fixture} {i} s: got {got ?? "none"}");
            else Assert.True(got is null || got == want, $"{fixture} {i} s (cooldown): got {got}");
        }
    }

    /// <summary>Icons of another colour pick nothing. Not a guarantee across classes (전태's blue passes as 바드's
    /// 비바체): only the emblem's class's icons are ever compared.</summary>
    [Theory]
    [InlineData("subclass_valk", "바드")]
    [InlineData("subclass_war", "발키리")]
    public void Another_class_icons_pick_nothing(string fixture, string cls)
    {
        foreach (var frame in Fixture.Load(fixture).Frames)
            Assert.Null(SubclassMatcher.Pick(frame, cls == "바드" ? 바드 : 발키리));
    }

    /// <summary>A ready 초각스 unlike every icon of the class set means another class is played (CharacterService.ClassMismatch);
    /// its own class's icons are never all unlike it, even blurred at 4K. 도화가's icons are too close to 발키리's
    /// and 바드's to tell those classes apart either way: there the centre piece tells (ClassEmblemTests).</summary>
    [Theory]
    [InlineData("subclass_valk", new[] { 0, 1, 2, 16, 17, 18, 19 })]
    [InlineData("subclass_bard", new[] { 0, 1, 2, 3, 4, 5 })]
    [InlineData("subclass_war", new[] { 0, 1, 2, 3, 4, 22, 23, 24, 25, 26 })]
    [InlineData("subclass_doh", new[] { 0, 1, 2, 3, 4 })]
    public void Only_another_class_is_unlike_every_icon(string fixture, int[] ready)
    {
        var own = fixture switch { "subclass_valk" => 발키리, "subclass_bard" => 바드, "subclass_doh" => 도화가, _ => 워로드 };
        var other = fixture is "subclass_valk" or "subclass_doh" ? 바드 : 발키리;
        var frames = Fixture.Load(fixture).Frames;
        foreach (int i in ready)
        {
            Assert.False(SubclassMatcher.Match(frames[i], own).Unlike, $"{fixture} {i} s own");
            Assert.True(SubclassMatcher.Match(frames[i], other).Unlike, $"{fixture} {i} s other");
        }
    }

    static readonly string[] Skills = ["일반1", "일반2", "일반3", "일반4", "일반5", "일반6", "일반7", "일반8"];

    /// <summary>In town (and wherever skills can't be used) the game greys the whole skill bar out (greyed out.png, 워로드). A
    /// cooling slot is as grey as a greyed-out one, but cooldowns never grey every 일반 slot: one ready in colour is enough.
    /// (The 각성기 keeps its gold frame, and the 아덴 slots are nearly grey anyway.)</summary>
    [Fact]
    public void A_greyed_out_bar_is_told_from_one_in_colour()
    {
        Assert.True(SubclassMatcher.Greyed([.. Skills.Select(s => SubclassMatcher.Colour(Fixture.Load("greyed_" + s).Frames[0]))]));
        Assert.False(SubclassMatcher.Greyed([.. Skills.Select(s => SubclassMatcher.Colour(Fixture.Load("Nocd_" + s).Frames[0]))]));
        Assert.False(SubclassMatcher.Greyed([.. Skills.Skip(1).Select(s => SubclassMatcher.Colour(Fixture.Load("greyed_" + s).Frames[0])), SubclassMatcher.Colour(Fixture.Load("Nocd_일반1").Frames[0])]));
        Assert.False(SubclassMatcher.Greyed([.. Enumerable.Repeat(10.0, 7), double.NaN]));   // a slot not read yet
    }

    /// <summary>Greyed out, the 초각스 icon keeps its shapes but loses its hue: its 직업각인 is told by the shapes alone.</summary>
    [Fact]
    public void A_greyed_out_초각스_still_picks_its_직업각인()
    {
        Assert.Equal(("고독한기사", false), SubclassMatcher.Match(Fixture.Load("greyed_초각스").Frames[0], 워로드, greyed: true));
    }

    /// <summary>Greyed out, another class's icons are still unlike it, so the wrong class set is still told; 바드's two icons differ
    /// only in hue, so greyed they pick nothing.</summary>
    [Fact]
    public void A_greyed_out_초각스_is_unlike_another_class()
    {
        var slot = Fixture.Load("greyed_초각스").Frames[0];
        Assert.True(SubclassMatcher.Match(slot, 발키리, greyed: true).Unlike);
        Assert.True(SubclassMatcher.Match(slot, 도화가, greyed: true).Unlike);
        Assert.Null(SubclassMatcher.Match(slot, 바드, greyed: true).Name);
    }

    /// <summary>아이콘으로 표시 skipped icons in one hue (hue spread under 20) as a tinted bar: 전투 태세's Q (orange) and W (blue)
    /// were never shown (sBug.mp4). Only a greyed bar is skipped now, and their colour is far from grey.</summary>
    [Fact]
    public void One_colour_icons_are_not_a_greyed_bar()
    {
        var colours = new[] { "sbug_Q", "sbug_W" }.Select(n => SubclassMatcher.Colour(Fixture.Load(n).Frames[0])).ToList();
        Assert.False(SubclassMatcher.Greyed(colours));
        Assert.All(colours, c => Assert.True(c > 60, $"{c}"));
    }
}
