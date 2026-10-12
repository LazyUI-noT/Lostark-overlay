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
}
