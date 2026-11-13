using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>복사 ① ② ③ and 되돌리기 (class-profiles.plan.md C, phase 4).</summary>
public class ProfileCopyTests
{
    const string Lone = "워로드 · 고독한기사", Combat = "워로드 · 전투태세", Bard = "바드 · 용맹";
    const string LoneBar = "워로드/고독한기사", CombatBar = "워로드/전투태세";
    static readonly IReadOnlySet<LookGroup> Plate = new HashSet<LookGroup> { LookGroup.판 };

    static Settings Playing(string? key)
    {
        var s = new Settings();
        s.Characters[Lone] = new() { Class = "워로드", Subclass = "고독한기사" };
        s.Characters[Combat] = new() { Class = "워로드", Subclass = "전투태세" };
        s.Characters[Bard] = new() { Class = "바드", Subclass = "용맹" };
        Go(s, key);
        return s;
    }

    static void Go(Settings s, string? key)
    {
        s.ActiveCharacter = key;
        s.SyncClass();
    }

    static CopyRequest All(int profile, string? from, IReadOnlySet<LookGroup> looks) => new(CopyMode.AllClasses, profile, from, profile, null, looks, false);

    static CopyRequest Class(int fp, string? fc, int tp, string? tc, IReadOnlySet<LookGroup> looks, bool classOnly = false) =>
        new(CopyMode.ClassToClass, fp, fc, tp, tc, looks, classOnly);

    [Fact]
    public void AllClasses_copies_looks_keeps_spots()
    {
        var s = Playing(Lone);
        (s.Overlay.Left, s.Overlay.Opacity) = (100, 0.5);
        Go(s, Combat);
        (s.Overlay.Left, s.Overlay.Opacity) = (500, 0.7);
        Go(s, Lone);

        Assert.NotNull(s.Copy(All(0, Lone, Plate)));

        Assert.Equal(0.5, s.Profiles[0].Classes![Combat].Opacity);
        Assert.Equal(500, s.Profiles[0].Classes![Combat].Overlay.Left);
        Assert.Equal((100.0, 0.5), (s.Overlay.Left, s.Overlay.Opacity));
    }

    [Fact]
    public void AllClasses_updates_shared_for_unset_classes()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.4;

        s.Copy(All(0, Lone, Plate));
        Go(s, Combat);   // never set up: draws the shared look

        Assert.Equal(0.4, s.Overlay.Opacity);
    }

    [Fact]
    public void ClassToClass_creates_entry_from_shared()
    {
        var s = Playing(Lone);
        s.Amsu.Color = "#aaaaaa";
        (s.Overlay.Left, s.Overlay.Opacity) = (100, 0.3);

        Assert.NotNull(s.Copy(Class(0, Lone, 1, Combat, Plate)));

        var made = s.Profiles[1].Classes![Combat];
        Assert.Equal(0.3, made.Opacity);
        Assert.NotEqual(100, made.Overlay.Left);   // places aren't copied
        Assert.Equal(s.Profiles[1].Amsu?.Color, made.Amsu?.Color);   // the rest is the profile's shared
        Assert.Contains(Combat, s.Profiles[1].SubclassPlaces.Keys);
    }

    [Fact]
    public void ClassOnly_only_same_class()
    {
        var s = Playing(Lone);
        s.GaugeBars[LoneBar] = new() { Splits = [0.5] };
        Assert.True(ProfileCopy.ClassOnlyAllowed(Class(0, Lone, 1, Lone, Plate)));
        Assert.False(ProfileCopy.ClassOnlyAllowed(Class(0, Lone, 1, Combat, Plate)));
        Assert.False(ProfileCopy.ClassOnlyAllowed(All(0, Lone, Plate)));
        Assert.False(ProfileCopy.ClassOnlyAllowed(Class(0, null, 1, null, Plate)));
        Assert.NotEqual("", ProfileCopy.ClassOnlyReason(Class(0, Lone, 1, Combat, Plate)));

        s.Copy(Class(0, Lone, 1, Combat, Plate, classOnly: true));   // asked, not allowed: not copied
        Assert.False(s.Profiles[1].GaugeBars!.ContainsKey(CombatBar));

        s.Copy(Class(0, Lone, 1, Lone, new HashSet<LookGroup>(), classOnly: true));
        Assert.Equal([0.5], s.Profiles[1].GaugeBars![LoneBar].Splits);
    }

    [Fact]
    public void Whole_profile_keeps_shot_and_copies_classes()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.3;
        s.GaugeBars[LoneBar] = new() { Splits = [0.25] };
        s.Profiles[1].Shot = new PixelRect(1, 2, 3, 4);

        Assert.NotNull(s.Copy(new(CopyMode.WholeProfile, 0, null, 1, null, new HashSet<LookGroup>(), false)));

        Assert.Equal(new PixelRect(1, 2, 3, 4), s.Profiles[1].Shot);
        Assert.Equal(0.3, s.Profiles[1].Classes![Lone].Opacity);
        Assert.Equal([0.25], s.Profiles[1].GaugeBars![LoneBar].Splits);
    }

    [Fact]
    public void Undo_restores_only_latest()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.3;
        s.Copy(Class(0, Lone, 1, Combat, Plate));
        var before = s.Profiles[1].Classes![Combat].Opacity;
        s.Overlay.Opacity = 0.6;
        var undo = s.Copy(Class(0, Lone, 1, Combat, Plate))!;
        Assert.Equal(0.6, s.Profiles[1].Classes![Combat].Opacity);

        Assert.True(s.UndoCopy(undo));

        Assert.Equal(before, s.Profiles[1].Classes![Combat].Opacity);
    }

    [Fact]
    public void Undo_of_a_new_entry_removes_it()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.3;
        var undo = s.Copy(Class(0, Lone, 1, Combat, Plate))!;

        s.UndoCopy(undo);

        Assert.False(s.Profiles[1].Classes!.ContainsKey(Combat));
        Assert.False(s.Profiles[1].SubclassPlaces.ContainsKey(Combat));
    }

    [Fact]
    public void Undo_after_switch_reapplies_live()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.3;
        var undo = s.Copy(Class(0, Lone, 2, Lone, Plate))!;
        s.SwitchProfile(2);
        Assert.Equal(0.3, s.Overlay.Opacity);

        Assert.True(s.UndoCopy(undo));

        Assert.NotEqual(0.3, s.Overlay.Opacity);
        Assert.Equal(2, s.ActiveProfile);
    }

    [Fact]
    public void Copy_into_the_live_class_shows_at_once_and_whole_undo_restores()
    {
        var s = Playing(Lone);
        s.SwitchProfile(1);
        var before = s.Overlay.Opacity;
        s.SwitchProfile(0);
        s.Overlay.Opacity = 0.2;
        s.SwitchProfile(1);

        var undo = s.Copy(new(CopyMode.WholeProfile, 0, null, 1, null, new HashSet<LookGroup>(), false))!;
        Assert.Equal(0.2, s.Overlay.Opacity);
        s.UndoCopy(undo);

        Assert.Equal(before, s.Overlay.Opacity);
    }

    [Fact]
    public void Nothing_to_copy_or_same_place_returns_null()
    {
        var s = Playing(Lone);
        Assert.Null(s.Copy(Class(0, Lone, 1, Combat, new HashSet<LookGroup>())));
        Assert.Null(s.Copy(Class(0, Lone, 0, Lone, Plate)));
        Assert.Null(s.Copy(new(CopyMode.WholeProfile, 1, null, 1, null, Plate, false)));
        Assert.Null(s.Copy(Class(0, Lone, 9, Lone, Plate)));
    }

    [Fact]
    public void Undo_of_a_copy_into_기본_restores_only_the_shared_entry()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.3;
        var other = s.Profiles[1];
        (other.Opacity, other.PartyTogether, other.GaugeBars) = (0.8, true, new() { ["x"] = new() });
        other.Classes = new() { [Combat] = new ProfileEntry { Opacity = 0.1 } };
        var undo = s.Copy(Class(0, Lone, 1, null, Plate))!;
        Assert.Equal(0.3, other.Opacity);

        (other.Classes[Combat].Opacity, other.PartyTogether) = (0.2, false);
        other.GaugeBars["y"] = new();
        Assert.True(s.UndoCopy(undo));

        var now = s.Profiles[1];
        Assert.Equal(0.8, now.Opacity);
        Assert.Equal(0.2, now.Classes![Combat].Opacity);
        Assert.False(now.PartyTogether);
        Assert.Contains("y", now.GaugeBars!.Keys);
    }

    [Fact]
    public void Undo_restores_the_live_gauge_bars_of_the_target_class()
    {
        var s = Playing(Lone);
        s.GaugeBars[LoneBar] = new() { Splits = [0.5] };
        s.Profiles[1].GaugeBars = new() { [LoneBar] = new() { Splits = [0.25] } };

        var undo = s.Copy(Class(1, Lone, 0, Lone, new HashSet<LookGroup>(), classOnly: true))!;
        Assert.Equal([0.25], s.GaugeBars[LoneBar].Splits);
        s.UndoCopy(undo);

        Assert.Equal([0.5], s.GaugeBars[LoneBar].Splits);
    }

    [Fact]
    public void Undo_brings_back_the_mirror_of_an_existing_entry_s_spot()
    {
        var s = Playing(Combat);
        s.Overlay.Left = 500;
        s.SwitchProfile(1);
        Go(s, Lone);
        s.Overlay.Opacity = 0.3;
        s.SwitchProfile(0);
        s.SwitchProfile(1);
        var undo = s.Copy(Class(1, Lone, 0, Combat, Plate))!;

        Assert.Equal(500, s.Profiles[0].SubclassPlaces[Combat].Left);
        s.UndoCopy(undo);

        Assert.Equal(500, s.Profiles[0].SubclassPlaces[Combat].Left);
        Assert.Equal(500, s.Profiles[0].Classes![Combat].Overlay.Left);
    }

    [Fact]
    public void AllClasses_from_a_class_entry_leaves_that_entry_and_lands_in_shared()
    {
        var s = Playing(Lone);
        s.Overlay.Opacity = 0.4;
        Go(s, Combat);
        s.Overlay.Opacity = 0.7;
        Go(s, Lone);
        var before = s.Profiles[0].Classes![Lone].Opacity;

        s.Copy(All(0, Lone, Plate));

        Assert.Equal(before, s.Profiles[0].Classes![Lone].Opacity);
        Assert.Equal(0.4, s.Profiles[0].Opacity);
        Assert.Equal(0.4, s.Profiles[0].Classes![Combat].Opacity);
    }

    [Fact]
    public void Valid_edges()
    {
        var none = new HashSet<LookGroup>();
        Assert.True(ProfileCopy.Valid(Class(0, Lone, 1, Lone, none, classOnly: true), 5));
        Assert.False(ProfileCopy.Valid(Class(0, Lone, 1, Combat, none, classOnly: true), 5));
        Assert.False(ProfileCopy.Valid(Class(0, Lone, 0, Lone, Plate), 5));
        Assert.False(ProfileCopy.Valid(Class(-1, Lone, 1, Lone, Plate), 5));
        Assert.False(ProfileCopy.Valid(Class(0, Lone, 5, Lone, Plate), 5));
        Assert.False(ProfileCopy.Valid(new(CopyMode.AllClasses, 0, Lone, 1, null, Plate, false), 5));
        Assert.False(ProfileCopy.Valid(All(0, Lone, none), 5));
        Assert.True(ProfileCopy.Valid(Class(0, null, 0, "", Plate), 5) == false);
    }

    [Fact]
    public void Confirm_names_target()
    {
        var text = ProfileCopy.Confirm(Class(0, Lone, 2, Combat, Plate), "워로드 · 전투태세");
        Assert.Contains("프로필 3", text);
        Assert.Contains("전투태세", text);
        Assert.Contains("판", text);
        Assert.Contains("프로필 4", ProfileCopy.Confirm(new(CopyMode.WholeProfile, 0, null, 3, null, Plate, false), null));
        Assert.Contains("클래스 전용", ProfileCopy.Confirm(Class(0, Lone, 1, Lone, Plate, classOnly: true), "워로드 · 고독한기사"));
    }
}
