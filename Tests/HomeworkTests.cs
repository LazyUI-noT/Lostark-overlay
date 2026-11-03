using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>숙제 (homework.plan.md): the Wednesday 06:00 week, which fights clear a raid, the tags and raising a pick.</summary>
public class HomeworkTests
{
    static readonly IReadOnlyList<RaidGate> Gates =
    [
        new("serka-g1", "고통의 마녀", "1관문", [], [], Raid.없음),
        new("serka-g2", "고통의 마녀", "2관문", [], [], Raid.없음),
    ];

    static readonly HomeworkRaid Serka = new("serka", "세르카", ["serka-g1", "serka-g2"],
    [
        new("노말", "세노", HomeworkColor.White, ["serka-g2"]),
        new("하드", "세하", HomeworkColor.Orange, ["serka-g2"]),
        new("나이트메어", "세나메", HomeworkColor.Purple, ["serka-g2"]),
    ]);

    static readonly IReadOnlyList<HomeworkRaid> Raids = [Serka];

    static FightRecord Fight(string gate, string difficulty, bool? cleared = true, string? character = "살살", int day = 4, int hour = 20) =>
        new(new DateTime(2026, 11, day, hour, 0, 0), gate, 300, [], "나", 3, null, [], [], [], [], Difficulty: difficulty, Cleared: cleared, Character: character);

    static HomeworkClear Clear(string difficulty, int day = 4, int hour = 20) => new("살살", "serka", difficulty, new DateTime(2026, 11, day, hour, 0, 0));

    [Theory]
    [InlineData(2026, 11, 4, 6, 0, 2026, 11, 4)]     // Wednesday 06:00 itself starts the week
    [InlineData(2026, 11, 4, 5, 59, 2026, 10, 28)]   // a minute before: last week
    [InlineData(2026, 11, 10, 23, 0, 2026, 11, 4)]   // Tuesday night: still the week from Wednesday
    [InlineData(2026, 11, 8, 12, 0, 2026, 11, 4)]
    public void A_week_starts_on_wednesday_at_six(int y, int mo, int d, int h, int mi, int wy, int wmo, int wd)
    {
        Assert.Equal(new DateTime(wy, wmo, wd, 6, 0, 0), Homework.WeekStart(new DateTime(y, mo, d, h, mi, 0)));
    }

    [Fact]
    public void A_cleared_last_gate_of_a_known_character_is_a_clear()
    {
        var clear = Homework.ClearOf(Fight("고통의 마녀 · 2관문", "하드"), Gates, Raids);

        Assert.Equal(new HomeworkClear("살살", "serka", "하드", new DateTime(2026, 11, 4, 20, 0, 0)), clear);
    }

    [Theory]
    [InlineData("고통의 마녀 · 1관문", "하드", true, "살살")]    // not the last gate
    [InlineData("고통의 마녀 · 2관문", "하드", false, "살살")]   // failed
    [InlineData("고통의 마녀 · 2관문", "하드", null, "살살")]    // result unknown
    [InlineData("고통의 마녀 · 2관문", "하드", true, null)]      // character unknown
    [InlineData("고통의 마녀 · 2관문", "매칭", true, "살살")]    // a difficulty the raid doesn't track
    [InlineData("구원의 종탑", "3단계", true, "살살")]           // no known gate
    public void Other_fights_clear_nothing(string gate, string difficulty, bool? cleared, string? character)
    {
        Assert.Null(Homework.ClearOf(Fight(gate, difficulty, cleared, character), Gates, Raids));
    }

    [Fact]
    public void An_open_pick_shows_its_own_tag()
    {
        var tag = Homework.Tag(new HomeworkPick("serka", "나이트메어"), [], Raids, new Dictionary<string, string>(), Week);

        Assert.Equal(new HomeworkTag("세나메", HomeworkColor.Purple, Done: false, Arrow: 0), tag);
    }

    static readonly DateTime Week = new(2026, 11, 4, 6, 0, 0);

    [Fact]
    public void A_clear_this_week_crosses_the_tag_out_with_the_difficulty_cleared()
    {
        var pick = new HomeworkPick("serka", "나이트메어");

        Assert.Equal(new HomeworkTag("세나메", HomeworkColor.Purple, true, 0), Homework.Tag(pick, [Clear("나이트메어")], Raids, new Dictionary<string, string>(), Week));
        Assert.Equal(new HomeworkTag("세하", HomeworkColor.Orange, true, -1), Homework.Tag(pick, [Clear("하드")], Raids, new Dictionary<string, string>(), Week));
        Assert.Equal(new HomeworkTag("세나메", HomeworkColor.Purple, true, 1),
            Homework.Tag(new HomeworkPick("serka", "하드"), [Clear("나이트메어")], Raids, new Dictionary<string, string>(), Week));
    }

    [Fact]
    public void A_clear_from_last_week_does_not_count()
    {
        var tag = Homework.Tag(new HomeworkPick("serka", "하드"), [Clear("하드", day: 4, hour: 5)], Raids, new Dictionary<string, string>(), Week);

        Assert.False(tag!.Done);
    }

    [Fact]
    public void A_renamed_tag_is_shown_instead_of_the_default()
    {
        var names = new Dictionary<string, string> { [Homework.TagKey("serka", "하드")] = "세르하" };

        Assert.Equal("세르하", Homework.Tag(new HomeworkPick("serka", "하드"), [], Raids, names, Week)!.Text);
    }

    [Fact]
    public void A_pick_of_a_raid_no_longer_known_has_no_tag()
    {
        Assert.Null(Homework.Tag(new HomeworkPick("gone", "하드"), [], Raids, new Dictionary<string, string>(), Week));
    }

    [Fact]
    public void A_higher_clear_raises_the_pick_and_a_lower_one_keeps_it()
    {
        IReadOnlyList<HomeworkPick> picks = [new("serka", "하드")];

        Assert.Equal([new HomeworkPick("serka", "나이트메어")], Homework.Raise(picks, Clear("나이트메어"), Raids));
        Assert.Same(picks, Homework.Raise(picks, Clear("노말"), Raids));
        Assert.Same(picks, Homework.Raise(picks, Clear("하드"), Raids));
    }

    [Fact]
    public void At_most_five_picks_per_character()
    {
        Assert.Equal(5, HomeworkCharacter.MaxPicks);
    }
}
