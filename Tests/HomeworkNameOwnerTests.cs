using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>A big name read is a listed character, never a new one (user, 2026-11-13): 밟키리 read 밟기리 made a new 숙제 row.</summary>
public class HomeworkNameOwnerTests
{
    static HomeworkCharacter C(string name, string? cls = null, int? level = null) => new() { Name = name, ClassName = cls, ItemLevel = level };

    [Fact]
    public void A_same_kind_swap_snaps_to_the_one_listed_name()
    {
        // 키 read 기: CharacterName.Snap refuses a same-kind swap (another character could be named so), this doesn't.
        Assert.Equal("밟키리", Homework.NameOwner("밟기리", [C("밟키리"), C("바드바드")], null, null));
    }

    [Fact]
    public void An_exact_name_is_its_own_even_beside_a_near_one()
    {
        HomeworkCharacter[] list = [C("너무아파요살살"), C("너무아파용살살")];
        Assert.Equal("너무아파용살살", Homework.NameOwner("너무아파용살살", list, null, null));
    }

    [Fact]
    public void A_name_near_none_is_no_one()
    {
        Assert.Null(Homework.NameOwner("완전다른이름", [C("밟키리")], null, null));
        Assert.Null(Homework.NameOwner("밟기리", [], null, null));
    }

    [Fact]
    public void Two_names_as_near_go_by_class_then_item_level_else_no_one()
    {
        HomeworkCharacter[] list = [C("너무아파요살살", "바드", 1720), C("너무아파용살살", "발키리", 1700)];

        Assert.Equal("너무아파용살살", Homework.NameOwner("너무아파옹살살", list, "발키리", null));
        Assert.Equal("너무아파요살살", Homework.NameOwner("너무아파옹살살", list, null, 1720));
        Assert.Null(Homework.NameOwner("너무아파옹살살", list, null, null));

        HomeworkCharacter[] sameClass = [C("너무아파요살살", "바드", 1720), C("너무아파용살살", "바드", 1720)];
        Assert.Null(Homework.NameOwner("너무아파옹살살", sameClass, "바드", 1720));   // unknown: no clear is kept for either
    }

    [Fact]
    public void The_nearer_name_wins_before_class_or_level()
    {
        HomeworkCharacter[] list = [C("밟키리", "발키리"), C("밟키리리", "바드")];
        Assert.Equal("밟키리", Homework.NameOwner("밟기리", list, "바드", null));
    }

    [Fact]
    public void Deleting_a_character_drops_it_from_the_list_and_the_card_order()
    {
        var hw = new HomeworkSettings { Characters = [C("밟키리"), C("밟기리")], Order = ["밟키리", "밟기리"] };

        Assert.True(Homework.Remove(hw, "밟기리"));

        Assert.Equal("밟키리", Assert.Single(hw.Characters).Name);
        Assert.Equal(["밟키리", ""], hw.Order);
        Assert.False(Homework.Remove(hw, "밟기리"));   // gone already
    }
}
