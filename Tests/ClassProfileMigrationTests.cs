using System.Text;
using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Every shipped 직업각인 has its own entry (<see cref="ClassProfileMigration.EnsureOwn"/>), holding what it showed
/// before: nothing on screen changes, and the window spots saved per 직업각인 still apply.</summary>
public class ClassProfileMigrationTests
{
    const string Warlord = "워로드", Lone = "워로드 · 고독한기사", Combat = "워로드 · 전투태세";

    static readonly IReadOnlyList<ClassInfo> Shipped = ProfileLoader.LoadClasses(Path.Combine(Helpers.RepoRoot(), "Profiles"));

    static string Key(ClassInfo c, string sub) => Settings.CharacterKey(c.Name, sub);

    static IEnumerable<string> ShippedKeys => Shipped.SelectMany(c => c.Subclasses.Keys.Select(sub => Key(c, sub)));

    static string Json(object o) => Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(o, SettingsStore.Json));

    static Dictionary<Part, Cell> OneCell(double x) => new() { [Part.각성기] = new(x, 0, 34, 34) };

    static void Fill(ClassProfile p, double seed)
    {
        p.Gauge = false;
        p.Layout = OneCell(seed);
        p.Groups[SlotGroup.초각스] = false;
        p.HiddenSlots = [SlotId.일반3];
        (p.Color, p.Colors[SlotGroup.각성기]) = (ColorSource.Personal, "#112233");
        if (p is MineProfile m) (m.Icons, m.Pulse, m.KeyLetters) = ([SlotGroup.일반스킬], [SlotGroup.각성기], [SlotId.일반1]);
        if (p is MemberProfile mem) (mem.Width, mem.Height) = (0.6, 1.4);
    }

    // ── EnsureOwn ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_shipped_직업각인_gets_an_own_entry_and_nothing_else_is_added()
    {
        var s = new Settings();
        Assert.True(ClassProfileMigration.EnsureOwn(s, Shipped));
        Assert.Equal(ShippedKeys.Order(), s.Classes.Keys.Order());
        Assert.All(ShippedKeys, k => Assert.True(ClassProfiles.IsOwn(s, k)));
    }

    [Fact]
    public void A_직업각인_that_followed_its_class_gets_a_copy_of_the_class_values()
    {
        var s = new Settings();
        Fill(ClassProfiles.Edit(s, Warlord).Mine, 5);
        ClassProfileMigration.EnsureOwn(s, Shipped);
        Assert.Equal(Json(s.Classes[Warlord].Mine), Json(s.Classes[Combat].Mine));
        Assert.NotSame(s.Classes[Warlord].Mine, s.Classes[Combat].Mine);
        s.Classes[Combat].Mine.Layout![Part.각성기] = new(99, 0, 34, 34);   // a later change in one does not reach the other
        Assert.Equal(5, s.Classes[Warlord].Mine.Layout![Part.각성기].X);
        Assert.Equal(5, s.Classes[Lone].Mine.Layout![Part.각성기].X);
    }

    [Fact]
    public void A_직업각인_of_a_class_without_its_own_entry_gets_a_copy_of_기본()
    {
        var s = new Settings();
        Fill(ClassProfiles.Edit(s, ClassProfiles.Template).Mine, 7);
        ClassProfileMigration.EnsureOwn(s, Shipped);
        Assert.Equal(Json(s.Classes[ClassProfiles.Template].Mine), Json(s.Classes[Combat].Mine));
        Assert.NotSame(s.Classes[ClassProfiles.Template].Mine, s.Classes[Combat].Mine);
    }

    [Fact]
    public void A_직업각인_that_is_already_own_keeps_its_values_untouched()
    {
        var s = new Settings();
        Fill(ClassProfiles.Edit(s, Warlord).Mine, 5);
        var own = ClassProfiles.Edit(s, Combat);
        Fill(own.Mine, 9);
        var before = Json(own);
        ClassProfileMigration.EnsureOwn(s, Shipped);
        Assert.Same(own, s.Classes[Combat]);
        Assert.Equal(before, Json(s.Classes[Combat]));
    }

    [Fact]
    public void Values_a_직업각인_kept_from_an_earlier_개별_are_replaced_by_what_it_showed_following_its_class()
    {
        var s = new Settings();
        Fill(ClassProfiles.Edit(s, Warlord).Mine, 5);
        Fill(ClassProfiles.Entry(s, Combat).Mine, 9);   // left over, 개별 off: not what it shows
        Assert.False(ClassProfiles.IsOwn(s, Combat));
        ClassProfileMigration.EnsureOwn(s, Shipped);
        Assert.True(ClassProfiles.IsOwn(s, Combat));
        Assert.Equal(5, s.Classes[Combat].Mine.Layout![Part.각성기].X);
    }

    [Fact]
    public void A_second_run_changes_nothing_and_so_does_one_after_saving_and_loading()
    {
        var s = new Settings();
        Fill(ClassProfiles.Edit(s, Warlord).Mine, 5);
        Assert.True(ClassProfileMigration.EnsureOwn(s, Shipped));
        var once = Json(s);
        Assert.False(ClassProfileMigration.EnsureOwn(s, Shipped));
        Assert.Equal(once, Json(s));

        var loaded = JsonSerializer.Deserialize<Settings>(once, SettingsStore.Json)!;
        Assert.False(ClassProfileMigration.EnsureOwn(loaded, Shipped));
        Assert.Equal(once, Json(loaded));
    }

    [Fact]
    public void The_class_entries_and_기본_are_never_changed_or_added()
    {
        var s = new Settings();
        Fill(ClassProfiles.Edit(s, ClassProfiles.Template).Mine, 7);
        Fill(ClassProfiles.Edit(s, Warlord).Members, 3);
        ClassProfiles.Entry(s, "바드").Mine.Gauge = false;   // not own: still not own
        var before = Json(s.Classes);
        ClassProfileMigration.EnsureOwn(s, Shipped);
        var classLevel = s.Classes.Where(c => !c.Key.Contains(" · ")).ToDictionary(c => c.Key, c => c.Value);
        Assert.Equal(before, Json(classLevel));
        Assert.Equal(["", "바드", Warlord], classLevel.Keys.Order());
    }

    [Fact]
    public void A_class_or_직업각인_that_is_not_shipped_is_ignored()
    {
        var s = new Settings();
        Fill(ClassProfiles.Edit(s, "없는클래스").Mine, 1);
        Fill(ClassProfiles.Edit(s, "없는클래스 · 어떤각인").Mine, 2);
        Fill(ClassProfiles.Edit(s, "워로드 · 없는각인").Mine, 3);
        ClassProfiles.Entry(s, "바드 · 없는각인");
        var before = Json(new { a = s.Classes["없는클래스"], b = s.Classes["없는클래스 · 어떤각인"], c = s.Classes["워로드 · 없는각인"], d = s.Classes["바드 · 없는각인"] });
        ClassProfileMigration.EnsureOwn(s, Shipped);
        Assert.Equal(before, Json(new { a = s.Classes["없는클래스"], b = s.Classes["없는클래스 · 어떤각인"], c = s.Classes["워로드 · 없는각인"], d = s.Classes["바드 · 없는각인"] }));
        Assert.False(ClassProfiles.IsOwn(s, "바드 · 없는각인"));
        Assert.Equal(ShippedKeys.Count() + 4, s.Classes.Count);
    }

    [Fact]
    public void Nothing_to_do_with_no_classes()
    {
        var s = new Settings();
        Assert.False(ClassProfileMigration.EnsureOwn(s, []));
        Assert.Empty(s.Classes);
    }

    // ── the migration changes nothing that is drawn ──────────────────────────────────

    static Settings OldState(string name)
    {
        var s = new Settings();
        var other = Shipped.First(c => c.Name != Warlord);
        switch (name)
        {
            case "nothing saved": break;
            case "기본 only":
                Fill(ClassProfiles.Edit(s, ClassProfiles.Template).Mine, 7);
                Fill(ClassProfiles.Edit(s, ClassProfiles.Template).Members, 8);
                break;
            case "class values only":
                Fill(ClassProfiles.Edit(s, ClassProfiles.Template).Mine, 7);
                foreach (var (c, i) in Shipped.Select((c, i) => (c, i)))
                {
                    Fill(ClassProfiles.Edit(s, c.Name).Mine, 10 + i);
                    Fill(ClassProfiles.Edit(s, c.Name).Members, 40 + i);
                }
                break;
            case "one 직업각인 own":
                Fill(ClassProfiles.Edit(s, ClassProfiles.Template).Mine, 7);
                Fill(ClassProfiles.Edit(s, Warlord).Mine, 5);
                Fill(ClassProfiles.Edit(s, Warlord).Members, 6);
                Fill(ClassProfiles.Edit(s, Combat).Mine, 9);
                Fill(ClassProfiles.Entry(s, Lone).Mine, 11);   // left over from 개별, off
                break;
            case "내 설정 적용 on":   // party members follow 나, which the class entry held
                Fill(ClassProfiles.Edit(s, ClassProfiles.Template).Mine, 7);
                foreach (var (c, i) in Shipped.Select((c, i) => (c, i)))
                {
                    var entry = ClassProfiles.Edit(s, c.Name);
                    Fill(entry.Mine, 10 + i);
                    (entry.Members.UseMineColor, entry.Members.UseMineDisplay, entry.Members.UseMineSize) = (true, true, true);
                }
                break;
            case "own on both":
                Fill(ClassProfiles.Edit(s, Warlord).Mine, 5);
                Fill(ClassProfiles.Edit(s, Combat).Mine, 9);
                Fill(ClassProfiles.Edit(s, Lone).Mine, 13);
                Fill(ClassProfiles.Edit(s, other.Name).Members, 14);
                break;
            default: throw new ArgumentException(name);
        }
        return s;
    }

    public static TheoryData<string> OldStates => ["nothing saved", "기본 only", "class values only", "one 직업각인 own", "own on both", "내 설정 적용 on"];

    /// <summary>Everything the overlay is drawn from, for every shipped 직업각인 as the active character and for a party
    /// member of every class in both roles.</summary>
    static List<string> Drawn(Settings s)
    {
        var seen = new List<string>();
        var active = s.ActiveCharacter;
        foreach (var c in Shipped)
        {
            foreach (var role in Enum.GetValues<Role>()) seen.Add(Json(ClassProfiles.Member(s, c.Name, role, null, c)));
            foreach (var sub in c.Subclasses.Keys)
            {
                var key = Key(c, sub);
                var of = ClassProfiles.Of(s, key);
                seen.Add(Json(new { of.Mine, of.Members }));
                s.Characters[key] = new() { Class = c.Name, Subclass = sub };
                s.ActiveCharacter = key;
                seen.Add(Json(ClassProfiles.Mine(s)));
                seen.Add(Json(ClassProfiles.Reads(s, c.AdenSlotsOf(sub))));
            }
        }
        s.ActiveCharacter = active;
        return seen;
    }

    [Theory, MemberData(nameof(OldStates))]
    public void Migrating_an_old_state_changes_nothing_that_is_drawn(string state)
    {
        var s = OldState(state);
        var before = Drawn(s);

        Assert.True(ClassProfileMigration.EnsureOwn(s, Shipped));

        Assert.All(ShippedKeys, k => Assert.True(ClassProfiles.IsOwn(s, k)));
        Assert.Equal(before, Drawn(s));
    }

    [Fact]
    public void The_drawn_check_would_notice_a_changed_value()
    {
        var s = OldState("class values only");
        var before = Drawn(s);
        ClassProfileMigration.EnsureOwn(s, Shipped);
        s.Classes[Combat].Mine.Layout = OneCell(1234);
        Assert.NotEqual(before, Drawn(s));
    }

    // ── window spot and size ─────────────────────────────────────────────────────────

    static Settings Playing(string key)
    {
        var s = new Settings();
        s.Characters[Lone] = new() { Class = Warlord, Subclass = "고독한기사" };
        s.Characters[Combat] = new() { Class = Warlord, Subclass = "전투태세" };
        s.ActiveCharacter = key;
        return s;
    }

    static void Place(Settings s, double left, double width) => (s.Overlay.Left, s.Overlay.Width) = (left, width);

    static (double, double) Spot(Settings s) => (s.Overlay.Left, s.Overlay.Width);

    static void Go(Settings s, string key)
    {
        s.ActiveCharacter = key;
        s.SyncPlace();
    }

    /// <summary>Old settings: 전투태세 is 개별 with its own spot (500) and 고독한기사 follows the class (100), in two 프로필.</summary>
    static Settings WithSpots(string playing)
    {
        var s = Playing(Lone);
        ClassProfiles.SetOwn(s, Combat, true);
        Place(s, 100, 1.0);
        s.SyncPlace();
        Go(s, Combat);
        Place(s, 500, 1.4);
        Go(s, Lone);
        s.SwitchProfile(1);
        Place(s, 700, 1.0);   // 프로필 2: the class spot
        s.SwitchProfile(0);
        Go(s, playing);
        return s;
    }

    [Theory]
    [InlineData(Lone)]
    [InlineData(Combat)]
    public void Spots_saved_per_직업각인_still_apply_to_the_same_직업각인(string playing)
    {
        var s = WithSpots(playing);
        // what the old settings showed: 고독한기사 at the class spot, 전투태세 at its own, 프로필 2 at its class spot
        Assert.True(ClassProfileMigration.EnsureOwn(s, Shipped));

        Go(s, Lone);
        Assert.Equal((100, 1.0), Spot(s));
        Go(s, Combat);
        Assert.Equal((500, 1.4), Spot(s));
        Go(s, Lone);
        s.SwitchProfile(1);
        Assert.Equal((700, 1.0), Spot(s));
        s.SwitchProfile(0);
        Assert.Equal((100, 1.0), Spot(s));
    }

    [Fact]
    public void A_moved_window_of_a_former_class_follower_stays_its_own_afterwards()
    {
        var s = WithSpots(Lone);
        ClassProfileMigration.EnsureOwn(s, Shipped);
        Go(s, Lone);
        Place(s, 300, 0.9);
        Go(s, Combat);
        Assert.Equal((500, 1.4), Spot(s));
        Go(s, Lone);
        Assert.Equal((300, 0.9), Spot(s));
    }

    [Fact]
    public void With_one_spot_for_everything_the_window_stays_put_through_the_migration()
    {
        var s = Playing(Lone);
        Place(s, 100, 1.0);
        s.SyncPlace();
        ClassProfileMigration.EnsureOwn(s, Shipped);
        s.SyncPlace();
        Assert.Equal((100, 1.0), Spot(s));
        Go(s, Combat);   // placed for the first time: starts where the window is
        Assert.Equal((100, 1.0), Spot(s));
    }

    // ── resets ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_classes_reset_leaves_every_직업각인_own_with_fresh_values()
    {
        var s = OldState("class values only");
        ClassProfileMigration.EnsureOwn(s, Shipped);

        SettingsResets.Classes(s);

        Assert.All(ShippedKeys, k => Assert.True(ClassProfiles.IsOwn(s, k)));
        var fresh = Json(new MineProfile());
        Assert.All(ShippedKeys, k => Assert.Equal(fresh, Json(ClassProfiles.Of(s, k).Mine)));
        Assert.False(ClassProfileMigration.EnsureOwn(s, Shipped));
    }

    [Fact]
    public void After_reset_all_a_run_of_EnsureOwn_restores_the_invariant_from_fresh_values()
    {
        var s = OldState("one 직업각인 own");
        ClassProfileMigration.EnsureOwn(s, Shipped);

        SettingsResets.All(s);

        Assert.False(ClassProfileMigration.EnsureOwn(s, Shipped));   // nothing lost: still own
        Assert.All(ShippedKeys, k => Assert.Equal(Json(new MineProfile()), Json(ClassProfiles.Of(s, k).Mine)));
        var fresh = new Settings();   // as after a settings file that could not be read
        Assert.True(ClassProfileMigration.EnsureOwn(fresh, Shipped));
        Assert.All(ShippedKeys, k => Assert.True(ClassProfiles.IsOwn(fresh, k)));
    }
}
