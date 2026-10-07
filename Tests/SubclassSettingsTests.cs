using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>직업각인별 설정: a 직업각인 ("워로드 · 전투태세") turned 개별 has your boxes of its own (display, layout,
/// colours, effects) and its own window spot and size per 프로필; off, it follows its class. Party members' boxes stay
/// per class.</summary>
public class SubclassSettingsTests
{
    const string Warlord = "워로드", Lone = "워로드 · 고독한기사", Combat = "워로드 · 전투태세";

    static Settings Playing(string key)
    {
        var s = new Settings();
        s.Characters[Lone] = new() { Class = Warlord, Subclass = "고독한기사" };
        s.Characters[Combat] = new() { Class = Warlord, Subclass = "전투태세" };
        s.ActiveCharacter = key;
        return s;
    }

    static Dictionary<Part, Cell> OneCell(double x) => new() { [Part.각성기] = new(x, 0, 34, 34) };

    [Fact]
    public void A_직업각인_follows_its_class_until_개별()
    {
        var s = Playing(Combat);
        ClassProfiles.Edit(s, Warlord).Mine.Layout = OneCell(5);
        Assert.Equal(Warlord, ClassProfiles.ActiveKey(s));
        Assert.Equal(5, ClassProfiles.Mine(s).Layout![Part.각성기].X);
    }

    [Fact]
    public void Turning_a_직업각인_개별_starts_from_its_class_and_then_keeps_its_own()
    {
        var s = Playing(Combat);
        ClassProfiles.Edit(s, Warlord).Mine.Layout = OneCell(5);
        ClassProfiles.SetOwn(s, Combat, true);
        Assert.Equal(Combat, ClassProfiles.ActiveKey(s));
        Assert.Equal(5, ClassProfiles.Mine(s).Layout![Part.각성기].X);

        ClassProfiles.Edit(s, Combat).Mine.Layout = OneCell(9);
        Assert.Equal(9, ClassProfiles.Mine(s).Layout![Part.각성기].X);
        s.ActiveCharacter = Lone;   // the other 직업각인 still draws the class's
        Assert.Equal(5, ClassProfiles.Mine(s).Layout![Part.각성기].X);
    }

    [Fact]
    public void Off_again_the_직업각인_follows_its_class_and_keeps_its_own_for_later()
    {
        var s = Playing(Combat);
        ClassProfiles.Edit(s, Combat).Mine.Layout = OneCell(9);
        ClassProfiles.SetOwn(s, Combat, false);
        Assert.Null(ClassProfiles.Mine(s).Layout);
        ClassProfiles.SetOwn(s, Combat, true);
        Assert.Equal(9, ClassProfiles.Mine(s).Layout![Part.각성기].X);
    }

    [Fact]
    public void Party_members_stay_per_class()
    {
        var s = Playing(Combat);
        ClassProfiles.Edit(s, Warlord).Members.Width = 0.7;
        ClassProfiles.Edit(s, Combat).Members.Width = 1.5;   // never used
        Assert.Equal(0.7, ClassProfiles.Of(s, Combat).Members.Width);
        Assert.Equal(0.7, ClassProfiles.Member(s, Warlord, Role.딜러, null).Width);
    }

    [Fact]
    public void A_drag_goes_into_the_직업각인_only_while_it_is_개별()
    {
        var s = Playing(Combat);
        Assert.Same(ClassProfiles.Entry(s, ClassProfiles.Template).Mine, ClassProfiles.BoxesShown(s, ClassProfiles.ActiveKey(s), member: false));
        ClassProfiles.SetOwn(s, Warlord, true);
        Assert.Same(s.Classes[Warlord].Mine, ClassProfiles.BoxesShown(s, Combat, member: false));
        ClassProfiles.SetOwn(s, Combat, true);
        Assert.Same(s.Classes[Combat].Mine, ClassProfiles.BoxesShown(s, ClassProfiles.ActiveKey(s), member: false));
    }

    [Fact]
    public void Parent_of_a_직업각인_is_its_class_and_of_a_class_기본()
    {
        Assert.Equal(Warlord, ClassProfiles.Parent(Combat));
        Assert.Equal(ClassProfiles.Template, ClassProfiles.Parent(Warlord));
        Assert.Null(ClassProfiles.Parent(ClassProfiles.Template));
    }

    // ── window spot and size ─────────────────────────────────────────────────────────

    static void Place(Settings s, double left, double width) => (s.Overlay.Left, s.Overlay.Width) = (left, width);

    [Fact]
    public void A_개별_직업각인_keeps_its_own_window_spot_and_size()
    {
        var s = Playing(Lone);
        ClassProfiles.SetOwn(s, Combat, true);
        Place(s, 100, 1.0);
        Assert.False(s.SyncPlace());   // still the class's spot

        s.ActiveCharacter = Combat;
        Assert.True(s.SyncPlace());
        Assert.Equal((100, 1.0), (s.Overlay.Left, s.Overlay.Width));   // first time: starts where the class's was
        Place(s, 500, 1.4);

        s.ActiveCharacter = Lone;
        Assert.True(s.SyncPlace());
        Assert.Equal((100, 1.0), (s.Overlay.Left, s.Overlay.Width));
        s.ActiveCharacter = Combat;
        s.SyncPlace();
        Assert.Equal((500, 1.4), (s.Overlay.Left, s.Overlay.Width));
    }

    [Fact]
    public void Each_프로필_keeps_its_own_spot_per_직업각인()
    {
        var s = Playing(Combat);
        ClassProfiles.SetOwn(s, Combat, true);
        s.SyncPlace();
        Place(s, 500, 1.0);
        s.SwitchProfile(1);
        Place(s, 700, 1.0);   // profile 2's 전태 spot
        s.SwitchProfile(0);
        Assert.Equal(500, s.Overlay.Left);
        s.ActiveCharacter = Lone;
        s.SyncPlace();
        s.SwitchProfile(1);
        Assert.True(double.IsNaN(s.Overlay.Left));   // profile 2's class spot was never set
        s.ActiveCharacter = Combat;
        s.SyncPlace();
        Assert.Equal(700, s.Overlay.Left);
    }

    [Fact]
    public void Turning_개별_off_puts_the_class_spot_back()
    {
        var s = Playing(Combat);
        Place(s, 100, 1.0);
        ClassProfiles.SetOwn(s, Combat, true);
        s.SyncPlace();
        Place(s, 500, 1.0);
        ClassProfiles.SetOwn(s, Combat, false);
        Assert.True(s.SyncPlace());
        Assert.Equal(100, s.Overlay.Left);
    }

    [Fact]
    public void 적용_from_an_editor_that_showed_another_직업각인_keeps_this_characters_spot()
    {
        var s = Playing(Lone);
        ClassProfiles.SetOwn(s, Combat, true);
        Place(s, 100, 1.0);
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(s, GaugeOverlay.Core.Data.SettingsStore.Json);
        var edited = System.Text.Json.JsonSerializer.Deserialize<Settings>(json, GaugeOverlay.Core.Data.SettingsStore.Json)!;   // as the editor copies
        edited.ActiveCharacter = Combat;   // the editor previews the other 직업각인
        edited.SyncPlace();
        Place(edited, 500, 1.0);
        s.ApplyLayoutFrom(edited);
        Assert.Equal((100, (string?)null), (s.Overlay.Left, s.PlaceKey));
        s.ActiveCharacter = Combat;
        s.SyncPlace();
        Assert.Equal(500, s.Overlay.Left);
    }
}
