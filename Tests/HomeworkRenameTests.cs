using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>homework-auto.plan.md: renaming a character (a misread like 밝기리 for 밟키리), merging two entries of one, and the
/// big name confirming a character added from its card's small name.</summary>
public sealed class HomeworkRenameTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "go-rename-" + Guid.NewGuid().ToString("N"));
    string Path_ => Path.Combine(_dir, "homework.json");

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    static HomeworkPick Pick(string raid, string difficulty = "하드") => new(raid, difficulty);

    static HomeworkSettings List(params HomeworkCharacter[] characters) => new() { Characters = [.. characters] };

    [Fact]
    public void A_rename_changes_the_name_and_confirms_the_character()
    {
        var hw = List(new HomeworkCharacter { Name = "밝기리", ClassName = "발키리", ItemLevel = 1700, FromCard = true, Picks = [Pick("act4")] });

        Assert.True(Homework.Rename(hw, "밝기리", "밟키리"));

        var c = Assert.Single(hw.Characters);
        Assert.Equal("밟키리", c.Name);
        Assert.False(c.FromCard);
        Assert.Equal([Pick("act4")], c.Picks);
        Assert.Equal(1700, c.ItemLevel);
    }

    [Fact]
    public void A_rename_onto_a_listed_name_merges_the_two()
    {
        var hw = List(
            new HomeworkCharacter { Name = "밟키리", ItemLevel = 1700, Picks = [Pick("act4"), Pick("serka")] },
            new HomeworkCharacter { Name = "밝기리", ClassName = "발키리", Subclass = "빛의 기사", ItemLevel = 1720, FromCard = true,
                Picks = [Pick("serka", "노말"), Pick("final")] });

        Assert.True(Homework.Rename(hw, "밝기리", "밟키리"));

        var c = Assert.Single(hw.Characters);
        Assert.Equal("밟키리", c.Name);
        Assert.Equal([Pick("act4"), Pick("serka"), Pick("final")], c.Picks);   // the listed one's first, then raids only the other had
        Assert.Equal(1720, c.ItemLevel);   // the higher level
        Assert.Equal("발키리", c.ClassName);   // filled from the other
        Assert.Equal("빛의 기사", c.Subclass);
        Assert.False(c.FromCard);
    }

    [Fact]
    public void A_merge_keeps_at_most_five_picks()
    {
        var hw = List(
            new HomeworkCharacter { Name = "a1", Picks = [Pick("r1"), Pick("r2"), Pick("r3"), Pick("r4")] },
            new HomeworkCharacter { Name = "a2", Picks = [Pick("r5"), Pick("r6")] });

        Homework.Rename(hw, "a2", "a1");

        Assert.Equal(HomeworkCharacter.MaxPicks, Assert.Single(hw.Characters).Picks.Count);
    }

    [Fact]
    public void A_rename_to_the_same_name_or_of_an_unlisted_one_changes_nothing()
    {
        var hw = List(new HomeworkCharacter { Name = "밟키리" });

        Assert.False(Homework.Rename(hw, "밟키리", "밟키리"));
        Assert.False(Homework.Rename(hw, "없는이름", "밟키리"));
        Assert.Equal("밟키리", Assert.Single(hw.Characters).Name);
    }

    [Fact]
    public void Clears_follow_a_rename()
    {
        var at = new DateTime(2026, 11, 5, 20, 0, 0);
        HomeworkClear[] clears = [new("밝기리", "act4", "하드", at), new("밟키리", "act4", "하드", at), new("다른캐릭", "act4", "하드", at)];

        var renamed = Homework.RenameClears(clears, "밝기리", "밟키리");

        Assert.Equal([new HomeworkClear("밟키리", "act4", "하드", at), new HomeworkClear("다른캐릭", "act4", "하드", at)], renamed);
    }

    [Fact]
    public void The_store_renames_its_clears()
    {
        var at = new DateTime(2026, 11, 5, 20, 0, 0);
        var store = new HomeworkStore(Path_);
        store.Add(new HomeworkClear("밝기리", "act4", "하드", at), at);

        store.Rename("밝기리", "밟키리");

        Assert.Equal([new HomeworkClear("밟키리", "act4", "하드", at)], new HomeworkStore(Path_).Load());
    }

    [Fact]
    public void A_clicked_cards_big_name_renames_the_character_from_that_card()
    {
        HomeworkCharacter[] list = [new() { Name = "밝기리", FromCard = true }, new() { Name = "너무아파요살살" }];
        string[] cards = ["밝기리", "너무아파요살살"];

        Assert.Equal("밝기리", Homework.ConfirmedBy("밟키리", list, cards, played: false)?.Name);
        Assert.Null(Homework.ConfirmedBy("밝기리", list, cards, played: false));   // listed already: nothing to confirm
        Assert.Null(Homework.ConfirmedBy("완전다른이름", list, cards, played: false));
    }

    [Fact]
    public void A_near_name_with_its_own_card_is_another_character()
    {
        HomeworkCharacter[] list = [new() { Name = "밝기리", FromCard = true }];

        Assert.Null(Homework.ConfirmedBy("밝기라", list, ["밝기리", "밝기라"], played: false));   // its own card is nearer
    }

    [Fact]
    public void The_played_name_renames_only_a_character_whose_card_is_not_shown()
    {
        HomeworkCharacter[] list = [new() { Name = "밝기리", FromCard = true }];

        Assert.Equal("밝기리", Homework.ConfirmedBy("밟키리", list, ["바드바드"], played: true)?.Name);   // its card is the 접속 중 one
        Assert.Null(Homework.ConfirmedBy("밟키리", list, ["밝기리", "바드바드"], played: true));   // its card is there: another character
    }

    [Fact]
    public void A_big_name_read_as_listed_confirms_a_character_from_its_card()
    {
        var hw = List(new HomeworkCharacter { Name = "밟키리", FromCard = true });

        Assert.True(Homework.ConfirmName(hw, "밟키리"));
        Assert.False(hw.Characters[0].FromCard);
        Assert.False(Homework.ConfirmName(hw, "밟키리"));   // confirmed already
    }

    [Fact]
    public void A_merge_shows_the_character_when_either_was_shown_and_keeps_the_latest_clear()
    {
        var hw = List(new HomeworkCharacter { Name = "a1", Hidden = true }, new HomeworkCharacter { Name = "a2" });
        Homework.Rename(hw, "a2", "a1");
        Assert.False(Assert.Single(hw.Characters).Hidden);

        var early = new DateTime(2026, 11, 5, 20, 0, 0);
        HomeworkClear[] clears = [new("a1", "act4", "노말", early), new("a2", "act4", "하드", early.AddHours(1)), new("a1", "serka", "하드", early)];
        Assert.Equal([new HomeworkClear("a1", "act4", "하드", early.AddHours(1)), new HomeworkClear("a1", "serka", "하드", early)],
            Homework.RenameClears(clears, "a2", "a1"));
    }

    [Fact]
    public void A_big_name_never_renames_a_confirmed_character()
    {
        HomeworkCharacter[] list = [new() { Name = "밝기리" }];

        Assert.Null(Homework.ConfirmedBy("밟키리", list, ["밝기리"], played: false));
    }
}
