using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Who is being played (homework.plan.md): the menu's name, a switch from the grid, the class shortcut.</summary>
public class CharacterWatchTests
{
    static readonly DateTime Started = new(2026, 11, 4, 20, 0, 0);
    static readonly TimeSpan T0 = TimeSpan.FromMinutes(10);

    static MenuRead Menu(string? current = null, string? picked = null, bool waiting = false) => new(true, current, picked, [], waiting);

    static HomeworkCharacter C(string name, string cls, string? sub) => new() { Name = name, ClassName = cls, Subclass = sub };

    [Fact]
    public void Unknown_until_the_menu_names_the_character_then_its_fights_since_start_are_its()
    {
        var watch = CharacterWatch.Start(Started);
        Assert.Null(watch.Current);

        var (next, change) = watch.Menu(Menu(current: "살살"), [], T0);

        Assert.Equal("살살", next.Current);
        Assert.Equal(new CharacterChange("살살", Started), change);
    }

    [Fact]
    public void The_same_name_again_changes_nothing()
    {
        var (watch, _) = CharacterWatch.Start(Started).Menu(Menu(current: "살살"), [], T0);

        Assert.Null(watch.Menu(Menu(current: "살살"), [], T0).Change);
    }

    [Fact]
    public void A_misread_name_snaps_to_the_known_one()
    {
        var (watch, _) = CharacterWatch.Start(Started).Menu(Menu(current: "너무아파오살살"), ["너무아파요살살"], T0);

        Assert.Equal("너무아파요살살", watch.Current);
    }

    [Fact]
    public void A_pick_then_waiting_then_a_loading_screen_switches_without_backfill()
    {
        var (watch, _) = CharacterWatch.Start(Started).Menu(Menu(current: "살살"), [], T0);
        (watch, _) = watch.Menu(Menu(current: "살살", picked: "매우"), [], T0);
        (watch, var waiting) = watch.Menu(Menu(current: "살살", picked: "매우", waiting: true), [], T0 + TimeSpan.FromSeconds(1));
        Assert.Null(waiting);
        Assert.Equal("살살", watch.Current);

        var (loaded, change) = watch.Loaded(T0 + TimeSpan.FromSeconds(20), Started.AddMinutes(11));

        Assert.Equal("매우", loaded.Current);
        Assert.Equal(new CharacterChange("매우", null), change);
    }

    [Fact]
    public void A_pick_closed_with_cancel_then_a_loading_screen_keeps_the_character()
    {
        var (watch, _) = CharacterWatch.Start(Started).Menu(Menu(current: "살살", picked: "매우"), [], T0);

        var (loaded, change) = watch.Loaded(T0 + TimeSpan.FromSeconds(20), Started);

        Assert.Equal("살살", loaded.Current);
        Assert.Null(change);
    }

    [Fact]
    public void A_waiting_pick_too_long_before_the_loading_screen_is_dropped()
    {
        var (watch, _) = CharacterWatch.Start(Started).Menu(Menu(current: "살살", picked: "매우", waiting: true), [], T0);

        var (loaded, change) = watch.Loaded(T0 + CharacterWatch.SwitchWait + TimeSpan.FromSeconds(1), Started);

        Assert.Equal("살살", loaded.Current);
        Assert.Null(change);
        Assert.Null(loaded.Pending);
    }

    [Fact]
    public void The_app_class_moving_away_from_the_characters_class_makes_it_unknown()
    {
        var (watch, _) = CharacterWatch.Start(Started).Menu(Menu(current: "살살"), [], T0);
        var at = Started.AddHours(1);

        var (next, change) = watch.Playing("바드", "진실된용맹", [C("살살", "워로드", "고독한기사")], at);

        Assert.Null(next.Current);
        Assert.Equal(at, next.UnknownSince);
        Assert.Equal(new CharacterChange(null, null), change);
        Assert.Null(watch.Playing("워로드", "전투태세", [C("살살", "워로드", "고독한기사")], at).Change);   // same class: kept
    }

    [Fact]
    public void Unknown_with_a_subclass_only_one_character_has_is_that_character()
    {
        var (next, change) = CharacterWatch.Start(Started).Playing("워로드", "고독한기사", [C("살살", "워로드", "고독한기사"), C("바드짱", "바드", "진실된용맹")], Started.AddHours(1));

        Assert.Equal("살살", next.Current);
        Assert.Equal(new CharacterChange("살살", Started), change);
    }

    [Fact]
    public void Two_characters_on_the_same_class_and_subclass_are_never_guessed()
    {
        var roster = new[] { C("살살", "가디언나이트", "드레드로어"), C("용살살", "가디언나이트", "드레드로어") };

        Assert.Null(CharacterWatch.Start(Started).Playing("가디언나이트", "드레드로어", roster, Started).Change);
    }

    [Fact]
    public void A_same_class_character_with_its_subclass_not_yet_learned_blocks_the_guess()
    {
        var roster = new[] { C("살살", "워로드", "고독한기사"), C("워로드2", "워로드", null) };

        Assert.Null(CharacterWatch.Start(Started).Playing("워로드", "고독한기사", roster, Started).Change);
    }
}
