using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>A party member's 내 설정 적용 follows "mine" of one 직업각인 of their class (<see cref="ClassProfiles.MemberMineKey"/>):
/// the active character's when the class is the same, else the one last played, else the class's first.</summary>
public class MemberMineTests
{
    static readonly IReadOnlyList<ClassInfo> Shipped = ProfileLoader.LoadClasses(Path.Combine(Helpers.RepoRoot(), "Profiles"));

    static readonly ClassInfo Warlord = Shipped.Single(c => c.Name == "워로드");
    static readonly string First = Settings.CharacterKey(Warlord.Name, Warlord.Subclasses.Keys.First());
    static readonly string Second = Settings.CharacterKey(Warlord.Name, Warlord.Subclasses.Keys.Skip(1).First());
    static readonly ClassInfo Other = Shipped.First(c => c.Name != Warlord.Name && c.Subclasses.Count > 1);
    static readonly string OtherFirst = Settings.CharacterKey(Other.Name, Other.Subclasses.Keys.First());

    static Settings Playing(string key)
    {
        var s = new Settings();
        var (cls, sub) = (key[..key.IndexOf(" · ", StringComparison.Ordinal)], key[(key.IndexOf(" · ", StringComparison.Ordinal) + 3)..]);
        s.Characters[key] = new() { Class = cls, Subclass = sub };
        s.ActiveCharacter = key;
        ClassProfileMigration.EnsureOwn(s, Shipped);
        return s;
    }

    [Fact]
    public void The_same_class_as_the_active_character_follows_its_직업각인_whatever_was_played_last()
    {
        var s = Playing(Second);
        s.SubclassHistory[Warlord.Name] = Warlord.Subclasses.Keys.First();
        Assert.Equal(Second, ClassProfiles.MemberMineKey(s, Warlord));
    }

    [Fact]
    public void Another_class_follows_the_직업각인_last_played_of_it()
    {
        var s = Playing(First);
        var last = Other.Subclasses.Keys.Last();
        s.SubclassHistory[Other.Name] = last;
        Assert.Equal(Settings.CharacterKey(Other.Name, last), ClassProfiles.MemberMineKey(s, Other));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("없는각인")]
    public void No_or_an_invalid_history_takes_the_first_shipped_직업각인(string? history)
    {
        var s = Playing(First);
        if (history is not null) s.SubclassHistory[Other.Name] = history;
        Assert.Equal(OtherFirst, ClassProfiles.MemberMineKey(s, Other));
    }

    [Fact]
    public void An_active_character_without_a_직업각인_falls_back_to_the_history()
    {
        var s = Playing(First);
        s.ActiveCharacter = Warlord.Name;
        s.Characters[Warlord.Name] = new() { Class = Warlord.Name, Subclass = "" };
        s.SubclassHistory[Warlord.Name] = Warlord.Subclasses.Keys.Last();
        Assert.Equal(Settings.CharacterKey(Warlord.Name, Warlord.Subclasses.Keys.Last()), ClassProfiles.MemberMineKey(s, Warlord));
    }

    [Fact]
    public void A_class_with_no_직업각인_or_no_class_has_no_key()
    {
        var s = Playing(First);
        var none = new ClassInfo("직업군", "가", [SlotId.아덴1], new Dictionary<string, GaugeRule?>());
        Assert.Null(ClassProfiles.MemberMineKey(s, none));
        Assert.Null(ClassProfiles.MemberMineKey(s, null));
    }

    // ── what Member draws ────────────────────────────────────────────────────────────

    [Fact]
    public void 내_설정_적용_color_and_display_follow_an_edit_of_the_직업각인()
    {
        var s = Playing(OtherFirst);   // another class is active: Warlord follows the history or its first
        var key = ClassProfiles.MemberMineKey(s, Warlord)!;
        var mine = ClassProfiles.Edit(s, key).Mine;
        (mine.Color, mine.Colors[SlotGroup.각성기]) = (ColorSource.Personal, "#123456");
        mine.HiddenSlots = [SlotId.일반4];
        mine.Groups[SlotGroup.초각스] = false;
        var members = ClassProfiles.Edit(s, Warlord.Name).Members;
        (members.UseMineColor, members.UseMineDisplay) = (true, true);

        var drawn = ClassProfiles.Member(s, Warlord.Name, Role.딜러, null, Warlord);

        Assert.Equal("#123456", drawn.Colors[SlotGroup.각성기]);
        Assert.Contains(SlotId.일반4, drawn.Hidden);
        Assert.False(drawn.Groups[SlotGroup.초각스]);
    }

    [Fact]
    public void An_edit_of_the_class_entry_s_mine_no_longer_reaches_a_member()
    {
        var s = Playing(OtherFirst);
        var members = ClassProfiles.Edit(s, Warlord.Name).Members;
        members.UseMineColor = true;
        var classMine = ClassProfiles.Edit(s, Warlord.Name).Mine;
        (classMine.Color, classMine.Colors[SlotGroup.각성기]) = (ColorSource.Personal, "#ABCDEF");
        Assert.NotEqual("#ABCDEF", ClassProfiles.Member(s, Warlord.Name, Role.딜러, null, Warlord).Colors[SlotGroup.각성기]);
    }

    [Fact]
    public void Without_the_class_info_the_class_entry_is_used_as_before()
    {
        var s = Playing(OtherFirst);
        var c = ClassProfiles.Edit(s, Warlord.Name);
        (c.Mine.Color, c.Mine.Colors[SlotGroup.각성기], c.Members.UseMineColor) = (ColorSource.Personal, "#ABCDEF", true);
        Assert.Equal("#ABCDEF", ClassProfiles.Member(s, Warlord.Name, Role.딜러, null).Colors[SlotGroup.각성기]);
    }

    [Fact]
    public void The_active_characters_edit_shows_on_a_member_of_the_same_class()
    {
        var s = Playing(Second);
        var mine = ClassProfiles.Edit(s, Second).Mine;
        (mine.Color, mine.Colors[SlotGroup.일반스킬]) = (ColorSource.Personal, "#0A0B0C");
        ClassProfiles.Edit(s, Warlord.Name).Members.UseMineColor = true;
        Assert.Equal("#0A0B0C", ClassProfiles.Member(s, Warlord.Name, Role.딜러, null, Warlord).Colors[SlotGroup.일반스킬]);
    }
}
