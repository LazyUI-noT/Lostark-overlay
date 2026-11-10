using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The API's 원정대 names are exact (lostark-api.plan.md): a name read off a grid card (often misread: 밝기리 for 밟키리)
/// that the API doesn't list is renamed to the nearest API name nobody has yet, instead of the API adding a twin.</summary>
public class ApiRosterTests
{
    static HomeworkCharacter C(string name, bool fromCard) => new() { Name = name, FromCard = fromCard };

    [Fact]
    public void A_misread_card_name_is_renamed_to_the_api_name()
    {
        var renames = ApiRoster.Renames([C("밝기리", fromCard: true), C("가나다", fromCard: false)], ["밟키리", "가나다"]);
        Assert.Equal([("밝기리", "밟키리")], renames);
    }

    [Theory]
    [InlineData(" 너무아파요살살 ", "밝기리", "너무아파요살살")]   // the typed 대표 캐릭터 wins over the screen's read
    [InlineData("", "밟키리", "밟키리")]
    [InlineData(null, "밟키리", "밟키리")]
    [InlineData("  ", null, null)]
    public void The_lookup_starts_from_the_main_character_when_one_is_typed(string? main, string? current, string? expected) =>
        Assert.Equal(expected, ApiRoster.Root(main, current));

    [Fact]
    public void A_name_the_api_lists_is_kept() =>
        Assert.Empty(ApiRoster.Renames([C("밟키리", fromCard: true)], ["밟키리"]));

    [Fact]
    public void A_confirmed_name_is_never_renamed() =>
        Assert.Empty(ApiRoster.Renames([C("밝기리", fromCard: false)], ["밟키리"]));

    [Fact]
    public void An_api_name_already_listed_takes_no_second_character() =>
        Assert.Empty(ApiRoster.Renames([C("밝기리", fromCard: true), C("밟키리", fromCard: false)], ["밟키리"]));

    [Fact]
    public void A_name_near_no_api_name_stays() =>
        Assert.Empty(ApiRoster.Renames([C("전혀다른이름", fromCard: true)], ["밟키리"]));

    [Fact]
    public void Two_misreads_never_take_the_same_api_name()
    {
        var renames = ApiRoster.Renames([C("밝기리", fromCard: true), C("밟기리", fromCard: true)], ["밟키리"]);
        Assert.Single(renames);
    }
}
