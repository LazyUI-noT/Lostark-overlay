using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>전투력 kept per character × 직업각인, the highest seen (user, 2026-11-08: a trash-mob preset lowers it; 발키리's
/// dealer and healer 직업각인 each keep their own); the menu shows a character's highest with its role.</summary>
public class CombatPowerBookTests
{
    static ApiProfile Profile(string name, string? subclass, double? power) =>
        new(name, "카마인", "발키리", 1700, power, subclass, [], []);

    static Role RoleOf(string subclass) => subclass == "해방자" ? Role.서포터 : Role.딜러;

    [Fact]
    public void A_lookup_is_kept_under_its_subclass_written_without_spaces()
    {
        var book = CombatPowerBook.Merge(new(), Profile("가나다", "빛의 기사", 3000));
        Assert.Equal(3000, CombatPowerBook.Of(book, "가나다", "빛의기사"));
        Assert.Equal(3000, CombatPowerBook.Of(book, "가나다", "빛의 기사"));
    }

    [Fact]
    public void A_lower_lookup_keeps_the_best_and_a_higher_one_replaces_it()
    {
        var book = CombatPowerBook.Merge(new(), Profile("가나다", "빛의 기사", 3000));
        book = CombatPowerBook.Merge(book, Profile("가나다", "빛의 기사", 2500));   // trash-mob preset
        Assert.Equal(3000, CombatPowerBook.Of(book, "가나다", "빛의기사"));
        book = CombatPowerBook.Merge(book, Profile("가나다", "빛의 기사", 3100.5));
        Assert.Equal(3100.5, CombatPowerBook.Of(book, "가나다", "빛의기사"));
    }

    [Fact]
    public void Each_subclass_keeps_its_own_and_the_shown_one_is_the_highest_with_its_role()
    {
        var book = CombatPowerBook.Merge(new(), Profile("가나다", "빛의 기사", 3000));
        book = CombatPowerBook.Merge(book, Profile("가나다", "해방자", 3500));
        Assert.Equal(3000, CombatPowerBook.Of(book, "가나다", "빛의기사"));
        Assert.Equal((3500, Role.서포터), CombatPowerBook.Shown(book, "가나다", RoleOf));
    }

    [Fact]
    public void Merging_leaves_the_old_book_as_it_was()
    {
        var before = CombatPowerBook.Merge(new(), Profile("가나다", "빛의 기사", 3000));
        _ = CombatPowerBook.Merge(before, Profile("가나다", "빛의 기사", 4000));
        _ = CombatPowerBook.Merge(before, Profile("라마바", "해방자", 1000));
        Assert.Equal(3000, CombatPowerBook.Of(before, "가나다", "빛의기사"));
        Assert.Null(CombatPowerBook.Of(before, "라마바", "해방자"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void No_or_no_real_combat_power_changes_nothing(double? power) =>
        Assert.Empty(CombatPowerBook.Merge(new(), Profile("가나다", "빛의 기사", power)));

    [Fact]
    public void Without_a_subclass_the_value_is_kept_under_none()
    {
        var book = CombatPowerBook.Merge(new(), Profile("가나다", null, 1200));
        Assert.Equal(1200, CombatPowerBook.Of(book, "가나다", null));
        Assert.Equal((1200, Role.딜러), CombatPowerBook.Shown(book, "가나다", RoleOf));
    }

    [Fact]
    public void Deleting_removes_one_subclass_and_the_character_once_empty()
    {
        var book = CombatPowerBook.Merge(new(), Profile("가나다", "빛의 기사", 3000));
        book = CombatPowerBook.Merge(book, Profile("가나다", "해방자", 3500));
        book = CombatPowerBook.Delete(book, "가나다", "해방자");
        Assert.Equal((3000, Role.딜러), CombatPowerBook.Shown(book, "가나다", RoleOf));
        book = CombatPowerBook.Delete(book, "가나다", "빛의 기사");
        Assert.False(book.ContainsKey("가나다"));
        Assert.Null(CombatPowerBook.Shown(book, "가나다", RoleOf));
    }

    [Fact]
    public void An_unknown_character_shows_nothing() =>
        Assert.Null(CombatPowerBook.Shown(new(), "없음", RoleOf));
}
