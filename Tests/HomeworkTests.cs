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

    // The entry levels the user gave (2026-11-04), in raids.json's order.
    static readonly IReadOnlyList<HomeworkRaid> Entries =
    [
        new("act3", "3막", ["act3-g3"], [new("노말", "3노", HomeworkColor.White, ["act3-g3"])]),   // no entry level: never allocated
        R("act4", ("싱글", 1700), ("노말", 1700), ("하드", 1720)),
        R("final", ("싱글", 1710), ("노말", 1710), ("하드", 1730)),
        R("serka", ("매칭", 1710), ("노말", 1710), ("하드", 1730), ("나이트메어", 1740)),
        R("belgardin", ("매칭", 1750), ("노말", 1750), ("하드", 1770), ("나이트메어", 1780)),
        R("belltower", ("1단계", 1700), ("2단계", 1720), ("3단계", 1750)),
    ];

    static HomeworkRaid R(string id, params (string Difficulty, int Entry)[] levels) =>
        new(id, id, [id], [.. levels.Select(l => new HomeworkLevel(l.Difficulty, l.Difficulty, HomeworkColor.White, [id], l.Entry))]);

    static HomeworkPick[] P(params (string Raid, string Difficulty)[] picks) => [.. picks.Select(p => new HomeworkPick(p.Raid, p.Difficulty))];

    [Fact]
    public void A_level_gets_the_three_raids_with_the_highest_entry_it_meets()
    {
        Assert.Equal(P(("belltower", "2단계"), ("act4", "하드"), ("serka", "노말")), Homework.Allocate(1720, Entries));   // 세르카's top (1740) beats 종막's (1730)
        Assert.Equal(P(("belgardin", "노말"), ("belltower", "3단계"), ("serka", "나이트메어")), Homework.Allocate(1750, Entries));   // a tie: 벨가르딘's top (1780) first
        Assert.Equal(P(("belgardin", "하드"), ("belltower", "3단계"), ("serka", "나이트메어")), Homework.Allocate(1775, Entries));
        Assert.Equal(P(("belgardin", "나이트메어"), ("belltower", "3단계"), ("serka", "나이트메어")), Homework.Allocate(1780, Entries));
        Assert.Equal(P(("serka", "노말"), ("final", "노말"), ("belltower", "1단계")), Homework.Allocate(1710, Entries));   // the higher of two at one entry level
        Assert.Equal(P(("belltower", "1단계"), ("act4", "노말")), Homework.Allocate(1700, Entries));   // only two open
    }

    [Fact]
    public void A_level_under_every_entry_gets_no_raids()
    {
        Assert.Empty(Homework.Allocate(1640, Entries));
    }

    [Fact]
    public void A_first_level_allocates_only_a_character_without_picks()
    {
        IReadOnlyList<HomeworkPick> mine = P(("act3", "노말"));

        Assert.Equal(Homework.Allocate(1750, Entries), Homework.PicksAfter([], null, 1750, follow: false, Entries));
        Assert.Same(mine, Homework.PicksAfter(mine, null, 1750, follow: true, Entries));
    }

    [Fact]
    public void A_new_level_redoes_the_picks_only_while_the_switch_is_on()
    {
        IReadOnlyList<HomeworkPick> mine = P(("act3", "노말"));

        Assert.Equal(Homework.Allocate(1775, Entries), Homework.PicksAfter(mine, 1750, 1775, follow: true, Entries));
        Assert.Same(mine, Homework.PicksAfter(mine, 1750, 1775, follow: false, Entries));
        Assert.Same(mine, Homework.PicksAfter(mine, 1750, 1750, follow: true, Entries));
        Assert.Same(mine, Homework.PicksAfter(mine, 1650, 1660, follow: true, Entries));   // no raid open: nothing to replace them with
    }

    [Theory]
    [InlineData(null, 1750, 1750)]
    [InlineData(1750, 1755, 1755)]
    [InlineData(1750, 1740, 1750)]   // a level doesn't fall: a misread
    [InlineData(1750, 1950, 1750)]   // nor jump: a misread (a real one is typed on 설정 → 숙제)
    public void A_read_level_moves_only_up_and_by_a_believable_step(int? stored, int read, int kept)
    {
        Assert.Equal(kept, Homework.LevelFrom(stored, read));
    }

    static HomeworkCustomRaid Ex(bool auto = false) => new("custom-ex", "종막 EX",
        [new("EX 하드", "EX하", HomeworkColor.Orange, []), new("EX 나이트메어", "EX나메", HomeworkColor.Purple, [])], auto);

    static IReadOnlyList<HomeworkRaid> WithEx => [.. Entries, Homework.AsRaid(Ex())];

    [Fact]
    public void An_added_raid_is_made_from_its_filled_rows()
    {
        var raid = Homework.NewCustom("id", " 종막 EX ", [("EX 하드", "EX하", HomeworkColor.Orange), ("", "", HomeworkColor.White), ("", "EX나메", HomeworkColor.Purple)], auto: true);

        Assert.Equal("종막 EX", raid!.Name);
        Assert.True(raid.Auto);
        Assert.Equal([("EX 하드", "EX하"), ("EX나메", "EX나메")], raid.Levels.Select(l => (l.Difficulty, l.Tag)));   // a tag alone names its difficulty too
    }

    [Theory]
    [InlineData("", "EX 하드")]    // no name
    [InlineData("종막 EX", "")]    // no difficulty
    public void An_added_raid_needs_a_name_and_a_difficulty(string name, string difficulty)
    {
        Assert.Null(Homework.NewCustom("id", name, [(difficulty, "", HomeworkColor.White)], auto: false));
    }

    [Fact]
    public void An_added_raid_is_never_allocated_and_stays_on_top_of_the_three()
    {
        IReadOnlyList<HomeworkPick> mine = P(("act4", "노말"), ("custom-ex", "EX 하드"));

        Assert.DoesNotContain(Homework.Allocate(1800, WithEx), p => p.Raid == "custom-ex");
        Assert.Equal([.. Homework.Allocate(1775, WithEx), new HomeworkPick("custom-ex", "EX 하드")], Homework.PicksAfter(mine, 1750, 1775, follow: true, WithEx));
    }

    [Fact]
    public void A_first_level_still_gives_the_three_to_a_character_with_only_added_raids()
    {
        IReadOnlyList<HomeworkPick> mine = P(("custom-ex", "EX 하드"));

        Assert.Equal([.. Homework.Allocate(1750, WithEx), mine[0]], Homework.PicksAfter(mine, null, 1750, follow: false, WithEx));
    }

    [Fact]
    public void Added_raids_past_five_picks_are_left_off()
    {
        IReadOnlyList<HomeworkPick> mine = P(("custom-ex", "EX 하드"), ("custom-ex", "EX 나이트메어"), ("custom-ex", "EX 하드"));

        Assert.Equal(HomeworkCharacter.MaxPicks, Homework.PicksAfter(mine, 1750, 1775, follow: true, WithEx).Count);
    }

    [Fact]
    public void An_automatic_raid_goes_on_at_its_top_difficulty_when_there_is_room()
    {
        var ex = Homework.AsRaid(Ex());
        IReadOnlyList<HomeworkPick> full = [.. Enumerable.Range(0, 5).Select(i => new HomeworkPick($"r{i}", "노말"))];
        IReadOnlyList<HomeworkPick> has = P(("custom-ex", "EX 하드"));

        Assert.Equal(P(("custom-ex", "EX 나이트메어")), Homework.WithRaid([], ex));
        Assert.Same(full, Homework.WithRaid(full, ex));
        Assert.Same(has, Homework.WithRaid(has, ex));
    }

    [Fact]
    public void A_clear_of_an_added_raid_crosses_its_tag_out_and_no_fight_can_make_one()
    {
        var clear = new HomeworkClear("살살", "custom-ex", "EX 하드", new DateTime(2026, 11, 4, 20, 0, 0));
        var tag = Homework.Tag(new HomeworkPick("custom-ex", "EX 나이트메어"), [clear], WithEx, new Dictionary<string, string>(), Week);

        Assert.Equal(new HomeworkTag("EX하", HomeworkColor.Orange, true, -1), tag);
        Assert.Null(Homework.ClearOf(Fight("종막 EX", "EX 하드"), Gates, WithEx));
    }

    [Fact]
    public void Added_raids_join_the_shipped_ones_unless_broken()
    {
        var broken = new HomeworkCustomRaid("custom-b", null!, [], false);
        var outsider = Ex() with { Id = "act4" };   // could shadow a shipped raid

        Assert.Equal([.. Entries.Select(r => r.Id), "custom-ex"], Homework.AllRaids(Entries, [broken, outsider, Ex()]).Select(r => r.Id));
    }

    [Fact]
    public void A_click_clears_an_added_raid_and_another_takes_it_back()
    {
        string path = Path.Combine(Path.GetTempPath(), $"homework-{Guid.NewGuid():N}.json");
        try
        {
            var store = new GaugeOverlay.Core.Data.HomeworkStore(path);
            var clear = new HomeworkClear("살살", "custom-ex", "EX 하드", new DateTime(2026, 11, 4, 20, 0, 0));
            var other = clear with { Character = "밝키리" };
            store.Toggle(other);

            Assert.True(store.Toggle(clear));
            Assert.Contains(clear, store.Load());
            Assert.False(store.Toggle(clear with { At = clear.At.AddHours(1) }));
            Assert.Equal([other], store.Load());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void At_most_five_picks_per_character()
    {
        Assert.Equal(5, HomeworkCharacter.MaxPicks);
    }
}
