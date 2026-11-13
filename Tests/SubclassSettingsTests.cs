using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>직업각인별 설정: a 직업각인 ("워로드 · 전투태세") turned 개별 has your boxes of its own (display, layout,
/// colours, effects); off, it follows its class. Its window spot and size are its own per 프로필 either way
/// (ClassLooksTests). Party members' boxes stay per class.</summary>
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
    public void Each_직업각인_keeps_its_own_window_spot_and_size_개별_or_not()
    {
        var s = Playing(Lone);
        Place(s, 100, 1.0);
        Assert.True(s.SyncClass());   // 고독한기사's from now on (class-profiles.plan.md: per 직업각인, 개별 or not)

        s.ActiveCharacter = Combat;
        Assert.True(s.SyncClass());
        Assert.Equal((100, 1.0), (s.Overlay.Left, s.Overlay.Width));   // first time: starts where the class's was
        Place(s, 500, 1.4);

        s.ActiveCharacter = Lone;
        Assert.True(s.SyncClass());
        Assert.Equal((100, 1.0), (s.Overlay.Left, s.Overlay.Width));
        s.ActiveCharacter = Combat;
        s.SyncClass();
        Assert.Equal((500, 1.4), (s.Overlay.Left, s.Overlay.Width));
    }

    [Fact]
    public void Each_프로필_keeps_its_own_spot_per_직업각인()
    {
        var s = Playing(Combat);
        ClassProfiles.SetOwn(s, Combat, true);
        s.SyncClass();
        Place(s, 500, 1.0);
        s.SwitchProfile(1);
        Place(s, 700, 1.0);   // profile 2's 전태 spot
        s.SwitchProfile(0);
        Assert.Equal(500, s.Overlay.Left);
        s.ActiveCharacter = Lone;
        s.SyncClass();
        s.SwitchProfile(1);
        Assert.True(double.IsNaN(s.Overlay.Left));   // profile 2's class spot was never set
        s.ActiveCharacter = Combat;
        s.SyncClass();
        Assert.Equal(700, s.Overlay.Left);
    }

    [Fact]
    public void Leaving_테스트_모드_with_another_character_puts_its_spot_on_and_keeps_the_old_one()
    {
        // Normal settings: 고독한기사 개별 at 825, the class spot at 300. In 테스트 모드 the app went to 기본 (ActiveCharacter is
        // app-written, so leaving keeps it): the window must not stay at 고독한기사's spot under no character.
        var normal = Playing(Lone);
        ClassProfiles.SetOwn(normal, Lone, true);
        Place(normal, 300, 1.0);
        normal.ActiveCharacter = Combat;
        normal.SyncClass();
        normal.ActiveCharacter = Lone;
        normal.SyncClass();
        Place(normal, 825, 0.93);

        var live = Playing(Lone);
        live.ActiveCharacter = null;
        live.SwapIn(normal);

        Assert.Null(live.PlaceKey);
        Assert.Equal((300, 1.0), (live.Overlay.Left, live.Overlay.Width));
        live.ActiveCharacter = Lone;
        live.SyncClass();
        Assert.Equal((825, 0.93), (live.Overlay.Left, live.Overlay.Width));
    }

    [Fact]
    public void Leaving_테스트_모드_onto_a_개별_직업각인_puts_its_own_spot_on()
    {
        var normal = Playing(Lone);
        ClassProfiles.SetOwn(normal, Lone, true);
        Place(normal, 300, 1.0);
        normal.SyncClass();
        Place(normal, 825, 0.93);
        normal.ActiveCharacter = null;   // saved on the class spot, 고독한기사's kept
        normal.SyncClass();

        var live = Playing(Lone);
        live.SwapIn(normal);

        Assert.Equal(Lone, live.PlaceKey);
        Assert.Equal((825, 0.93), (live.Overlay.Left, live.Overlay.Width));
    }

    [Fact]
    public void Turning_개별_off_keeps_the_직업각인_s_spot()
    {
        var s = Playing(Combat);
        Place(s, 100, 1.0);
        ClassProfiles.SetOwn(s, Combat, true);
        s.SyncClass();
        Place(s, 500, 1.0);
        ClassProfiles.SetOwn(s, Combat, false);   // 개별 is the boxes only (class-profiles.plan.md)
        Assert.False(s.SyncClass());
        Assert.Equal(500, s.Overlay.Left);
    }
}
