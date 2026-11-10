using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>전투력 on the 게임 메뉴 (lostark-api.plan.md phase 4): right of each card's item level and of the portrait's, the
/// character's best 직업각인 value in that 직업각인's role colour. Positions at 1080p measured on
/// reference/homework_project/tags/goal-tags.png (a card's level digits end 119 px right of the card, the portrait's at x 432).</summary>
public class MenuPowerTests
{
    static readonly Dictionary<string, Dictionary<string, double>> Book = new()
    {
        ["매우아파요살살"] = new() { ["전투태세"] = 2345.67 },
        ["밝키리"] = new() { ["빛의기사"] = 1800, ["해방자"] = 2100.5 },
        ["너무아파요살살"] = new() { ["포식자"] = 5004.5 },
    };

    static Role RoleOf(string key) => key == "해방자" ? Role.서포터 : Role.딜러;

    // A card from its "Lv" word at (854, 424), 12 px high: the card starts 8 px left, its bottom 7 px below.
    static MenuCard Card(string name, double x = 846, double y = 424) => new(name, null, x, y, GameMenu.CardWidth, y + 12 + 7, 1771);

    static MenuRead Open(params MenuCard[] cards) => new(true, "너무아파요살살", null, cards, false);

    [Fact]
    public void A_card_shows_its_owner_best_value_right_of_its_item_level()
    {
        var card = Card("매우아파요살살");

        var tags = MenuPower.Tags(Open(card), current: null, [(card, "매우아파요살살")], Book, RoleOf);

        var tag = Assert.Single(tags);
        Assert.Equal("2,345", tag.Text);
        Assert.Equal(Role.딜러, tag.Role);
        Assert.Equal(new PixelRect(846 + 126, 424, 0, 12), tag.Line);
    }

    [Fact]
    public void The_highest_subclass_value_is_shown_with_its_own_role()
    {
        var card = Card("밝키리");

        var tag = Assert.Single(MenuPower.Tags(Open(card), null, [(card, "밝키리")], Book, RoleOf));

        Assert.Equal("2,100", tag.Text);
        Assert.Equal(Role.서포터, tag.Role);
    }

    [Fact]
    public void Cards_without_an_owner_or_a_value_show_nothing()
    {
        var (unknown, unlooked) = (Card("누구"), Card("새캐릭", x: 1106));

        var tags = MenuPower.Tags(Open(unknown, unlooked), null, [(unknown, null), (unlooked, "새캐릭")], Book, RoleOf);

        Assert.Empty(tags);
    }

    [Fact]
    public void The_played_character_shows_right_of_the_portrait_item_level()
    {
        var tag = Assert.Single(MenuPower.Tags(Open(), "너무아파요살살", [], Book, RoleOf));

        var level = Geometry.RefMenuPortraitLevel;
        Assert.Equal("5,004", tag.Text);
        Assert.Equal(new PixelRect(level.X + level.Width + 7, level.Y, 0, level.Height), tag.Line);
    }

    [Fact]
    public void A_closed_menu_shows_nothing()
    {
        var card = Card("매우아파요살살");

        Assert.Empty(MenuPower.Tags(MenuRead.Closed with { Cards = [card] }, "너무아파요살살", [(card, "매우아파요살살")], Book, RoleOf));
    }

    [Fact]
    public void A_subclass_role_comes_from_the_class_that_has_it()
    {
        var classes = new[]
        {
            new ClassInfo("전사", "워로드", [], new Dictionary<string, GaugeRule?> { ["전투태세"] = null }),
            new ClassInfo("전사", "발키리", [], new Dictionary<string, GaugeRule?> { ["빛의기사"] = null, ["해방자"] = null },
                Own: new Dictionary<string, SubclassInfo?> { ["해방자"] = new(Role.서포터, null, DarkCue.None) }),
            new ClassInfo("스페셜리스트", "도화가", [], new Dictionary<string, GaugeRule?> { ["만개"] = null }, Role: Role.서포터),
        };

        Assert.Equal(Role.서포터, MenuPower.RoleIn(classes, "해방자"));
        Assert.Equal(Role.딜러, MenuPower.RoleIn(classes, "빛의기사"));
        Assert.Equal(Role.서포터, MenuPower.RoleIn(classes, "만개"));
        Assert.Equal(Role.딜러, MenuPower.RoleIn(classes, ""));
    }
}
