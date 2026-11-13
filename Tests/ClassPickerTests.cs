using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정's 편집 중 list: 기본, then every shipped "클래스 · 직업각인", never a class alone.</summary>
public class ClassPickerTests
{
    static readonly IReadOnlyList<ClassInfo> Shipped = ProfileLoader.LoadClasses(Path.Combine(Helpers.RepoRoot(), "Profiles"));

    const string Warlord = "워로드", Combat = "워로드 · 전투태세", Lone = "워로드 · 고독한기사";

    static ClassInfo Made(string name, params string[] subs) =>
        new("직업군", name, [SlotId.아덴1], subs.ToDictionary(s => s, _ => (GaugeRule?)null));

    [Fact]
    public void 기본_comes_first_then_each_직업각인_in_the_order_of_classes_json()
    {
        var entries = ClassPicker.PickerEntries([Made("가", "나", "다"), Made("라", "마")]);
        Assert.Equal(["", "가 · 나", "가 · 다", "라 · 마"], entries);
    }

    [Fact]
    public void No_class_alone_entry_is_listed()
    {
        var entries = ClassPicker.PickerEntries(Shipped);
        Assert.Equal(ClassProfiles.Template, entries[0]);
        Assert.All(entries.Skip(1), e => Assert.Contains(" · ", e));
        Assert.DoesNotContain(Warlord, entries);
    }

    [Fact]
    public void Every_shipped_직업각인_is_listed_once()
    {
        var entries = ClassPicker.PickerEntries(Shipped);
        var expected = Shipped.SelectMany(c => c.Subclasses.Keys.Select(sub => Settings.CharacterKey(c.Name, sub)));
        Assert.Equal(expected, entries.Skip(1));
        Assert.Equal(entries.Count, entries.Distinct().Count());
    }

    [Fact]
    public void Only_기본_with_no_classes() => Assert.Equal([ClassProfiles.Template], ClassPicker.PickerEntries([]));

    [Theory]
    [InlineData("바드 · 절실한 구원", "바드", true)]          // the class
    [InlineData("바드 · 절실한 구원", "구원", true)]          // the 직업각인
    [InlineData("바드 · 절실한 구원", "바드 절실", true)]     // both, any order of words
    [InlineData("바드 · 절실한 구원", "절실 바드", true)]
    [InlineData("바드 · 절실한 구원", "절실한구원", true)]    // spaces typed or not
    [InlineData("바드 · 절실한 구원", "워로드", false)]
    [InlineData("바드 · 절실한 구원", "", true)]              // nothing typed: everything
    [InlineData("바드 · 절실한 구원", "  ", true)]
    [InlineData("Valkyrie", "valk", true)]                   // case doesn't matter
    public void Search_matches_class_or_직업각인(string shown, string typed, bool expected) =>
        Assert.Equal(expected, ClassPicker.Matches(shown, typed));

    // ── Normalize ────────────────────────────────────────────────────────────────────

    static IReadOnlyList<string> Entries => ClassPicker.PickerEntries(Shipped);

    [Fact]
    public void A_listed_entry_stays()
    {
        Assert.Equal(Lone, ClassPicker.Normalize(Lone, Combat, Entries));
        Assert.Equal(ClassProfiles.Template, ClassPicker.Normalize(ClassProfiles.Template, Combat, Entries));
    }

    [Fact]
    public void An_old_class_alone_key_becomes_the_active_직업각인()
    {
        Assert.Equal(Combat, ClassPicker.Normalize(Warlord, Combat, Entries));
    }

    [Fact]
    public void An_old_class_alone_key_becomes_기본_when_no_직업각인_is_active()
    {
        Assert.Equal(ClassProfiles.Template, ClassPicker.Normalize(Warlord, null, Entries));
        Assert.Equal(ClassProfiles.Template, ClassPicker.Normalize(Warlord, Warlord, Entries));   // an active class alone is not listed either
    }

    [Theory]
    [InlineData("없는클래스")]
    [InlineData("없는클래스 · 어떤각인")]
    [InlineData("워로드 · 없는각인")]
    [InlineData(" · ")]
    public void An_unknown_key_becomes_the_active_직업각인(string unknown)
    {
        Assert.Equal(Combat, ClassPicker.Normalize(unknown, Combat, Entries));
        Assert.Equal(ClassProfiles.Template, ClassPicker.Normalize(unknown, null, Entries));
    }

    [Fact]
    public void Nothing_picked_yet_shows_the_active_직업각인_or_기본()
    {
        Assert.Equal(Combat, ClassPicker.Normalize(null, Combat, Entries));
        Assert.Equal(ClassProfiles.Template, ClassPicker.Normalize(null, null, Entries));
    }

    // ── CopyTargets (복사 → 대상) ──────────────────────────────────────────────────────────

    [Fact]
    public void One_직업각인_copies_to_itself_for_me_and_to_its_class_for_party_members()
    {
        Assert.Equal([Combat], ClassPicker.CopyTargets(Combat, Shipped, toMembers: false));
        Assert.Equal([Warlord], ClassPicker.CopyTargets(Combat, Shipped, toMembers: true));
    }

    [Fact]
    public void 기본_is_the_same_entry_for_me_and_for_party_members()
    {
        Assert.Equal([ClassProfiles.Template], ClassPicker.CopyTargets(ClassProfiles.Template, Shipped, toMembers: false));
        Assert.Equal([ClassProfiles.Template], ClassPicker.CopyTargets(ClassProfiles.Template, Shipped, toMembers: true));
    }

    [Fact]
    public void All_is_기본_and_every_직업각인_for_me()
    {
        Assert.Equal(ClassPicker.PickerEntries(Shipped), ClassPicker.CopyTargets(null, Shipped, toMembers: false));
    }

    [Fact]
    public void All_for_party_members_is_기본_and_each_class_once()
    {
        var targets = ClassPicker.CopyTargets(null, Shipped, toMembers: true);
        Assert.Equal([ClassProfiles.Template, .. Shipped.Where(c => c.Subclasses.Count > 0).Select(c => c.Name)], targets);
    }

    // ── CopyRefusal / CopyNeedsConfirm (복사 page) ─────────────────────────────────────────

    const string Other = "워로드 · 고독한기사";

    [Fact]
    public void Copying_a_직업각인_onto_itself_on_the_same_side_is_refused()
    {
        Assert.NotNull(ClassPicker.CopyRefusal(CopyParts.Color, Combat, false, Combat, false, Shipped));
        Assert.NotNull(ClassPicker.CopyRefusal(CopyParts.Color, ClassProfiles.Template, true, ClassProfiles.Template, true, Shipped));
    }

    [Fact]
    public void Party_members_share_the_class_so_two_직업각인_of_one_class_are_the_same_target()
    {
        Assert.NotNull(ClassPicker.CopyRefusal(CopyParts.Color, Combat, true, Other, true, Shipped));
        Assert.Null(ClassPicker.CopyRefusal(CopyParts.Color, Combat, false, Other, false, Shipped));
    }

    [Fact]
    public void Another_side_or_another_target_or_all_is_allowed()
    {
        Assert.Null(ClassPicker.CopyRefusal(CopyParts.Color, Combat, false, Combat, true, Shipped));
        Assert.Null(ClassPicker.CopyRefusal(CopyParts.Color, Combat, true, Combat, false, Shipped));
        Assert.Null(ClassPicker.CopyRefusal(CopyParts.Color, Combat, false, null, false, Shipped));
    }

    [Fact]
    public void Nothing_ticked_is_refused()
    {
        Assert.Equal("복사할 것을 고르세요.", ClassPicker.CopyRefusal(0, Combat, false, Other, false, Shipped));
    }

    [Fact]
    public void Cooldown_view_parts_alone_only_go_from_me_to_me()
    {
        const CopyParts mineOnly = CopyParts.Progress | CopyParts.Timer;
        Assert.NotNull(ClassPicker.CopyRefusal(mineOnly, Combat, false, Other, true, Shipped));
        Assert.NotNull(ClassPicker.CopyRefusal(mineOnly, Combat, true, Other, false, Shipped));
        Assert.Null(ClassPicker.CopyRefusal(mineOnly, Combat, false, Other, false, Shipped));
        Assert.Null(ClassPicker.CopyRefusal(mineOnly | CopyParts.Color, Combat, false, Other, true, Shipped));
    }

    [Fact]
    public void Only_모든_직업각인_asks_for_a_second_click()
    {
        Assert.True(ClassPicker.CopyNeedsConfirm(null));
        Assert.False(ClassPicker.CopyNeedsConfirm(Other));
    }

    // ── SharedPartyNote ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_party_note_counts_the_class_s_직업각인()
    {
        Assert.Equal("두 직업각인이 같이 씁니다", ClassPicker.SharedPartyNote(Made("가", "나", "다")));
        Assert.Equal("3개 직업각인이 같이 씁니다", ClassPicker.SharedPartyNote(Made("가", "나", "다", "라")));
    }

    [Fact]
    public void A_class_with_one_or_no_직업각인_shares_with_nobody()
    {
        Assert.Null(ClassPicker.SharedPartyNote(Made("가", "나")));
        Assert.Null(ClassPicker.SharedPartyNote(Made("가")));
        Assert.Null(ClassPicker.SharedPartyNote(null));
    }

    static readonly string[] Picked = ["", "바드 · 진실된 용기", "워로드 · 전투태세", "워로드 · 고독한기사", "바드 · 절실한 구원"];

    [Fact]
    public void A_favourite_class_moves_all_its_직업각인_to_the_top_in_list_order()
    {
        Assert.Equal(["바드 · 진실된 용기", "바드 · 절실한 구원", "", Combat, Lone], ClassPicker.FavoritesFirst(Picked, ["바드"]));
    }

    [Fact]
    public void A_favourite_직업각인_moves_only_that_entry() =>
        Assert.Equal([Lone, "", "바드 · 진실된 용기", Combat, "바드 · 절실한 구원"], ClassPicker.FavoritesFirst(Picked, [Lone]));

    [Theory]
    [InlineData("바드 · 진실된 용기", true)]    // its class is starred
    [InlineData("워로드 · 고독한기사", true)]   // starred itself
    [InlineData("워로드 · 전투태세", false)]
    [InlineData("", false)]                     // 기본 never
    public void An_entry_is_starred_by_itself_or_its_class(string entry, bool expected) =>
        Assert.Equal(expected, ClassPicker.Starred(entry, ["바드", Lone]));

    [Fact]
    public void An_unknown_favourite_changes_nothing() =>
        Assert.Equal(Picked, ClassPicker.FavoritesFirst(Picked, ["없는 클래스", "바드 · 없는 각인"]));

    [Fact]
    public void 복사_대상_favourites_add_each_assigned_character_직업각인()
    {
        var chars = new Dictionary<string, CharacterSettings>
        {
            ["a"] = new() { Class = "워로드", Subclass = "고독한기사" },
            ["b"] = new() { Class = "바드", Subclass = "" },   // no 직업각인 yet: not starred
            ["c"] = new() { Class = "워로드", Subclass = "고독한기사" },
        };
        var favs = ClassPicker.CopyFavorites(["바드 · 절실한 구원"], chars);
        Assert.Equal(["바드 · 절실한 구원", Lone], favs);
        Assert.Equal([Lone, "바드 · 절실한 구원", "", "바드 · 진실된 용기", Combat], ClassPicker.FavoritesFirst(Picked, favs));   // list order
    }
}

