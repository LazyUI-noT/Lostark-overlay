using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>코어 빌드's saved choice: per "클래스 · 직업각인", shared by all profiles, independent of the active character,
/// loaded at default from older files and sanitised like the other hand-editable settings.</summary>
public sealed class CoreChoiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GaugeOverlayTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");
    const string Lonely = "워로드 · 고독한기사", Stance = "워로드 · 전투태세";

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    static CoreChoice Digits(int sun, int moon, int star) => new() { Sun = sun, Moon = moon, Star = star };

    static (bool, int, int, int) Fields(CoreChoice c) => (c.Apply, c.Sun, c.Moon, c.Star);

    [Fact]
    public void A_core_choice_is_off_at_1_1_1_with_no_slots_by_default()
    {
        var c = new CoreChoice();
        Assert.Equal((false, 1, 1, 1), Fields(c));
        Assert.Empty(c.Slots);
        Assert.Equal((1, 2, 3), (Digits(1, 2, 3).Of(CoreType.해), Digits(1, 2, 3).Of(CoreType.달), Digits(1, 2, 3).Of(CoreType.별)));
    }

    [Fact]
    public void A_character_never_chosen_reads_the_default_and_stores_nothing()
    {
        var s = new Settings();
        Assert.Equal((false, 1, 1, 1), Fields(s.CoreOf(Lonely)));
        Assert.Empty(s.Cores);
    }

    /// <summary>Pick 고독한기사 111, switch to 전투태세 333, switch back: 111 again.</summary>
    [Fact]
    public void Each_subclass_keeps_its_own_choice_across_a_switch()
    {
        var s = new Settings();
        s.PickCharacter("전사", "워로드", "고독한기사");
        s.SetCore(Lonely, Digits(1, 1, 1) with { Apply = true });
        s.PickCharacter("전사", "워로드", "전투태세");
        s.SetCore(Stance, Digits(3, 3, 3));
        s.PickCharacter("전사", "워로드", "고독한기사");

        Assert.Equal((true, 1, 1, 1), Fields(s.CoreOf(Lonely)));
        Assert.Equal((false, 3, 3, 3), Fields(s.CoreOf(Stance)));
    }

    [Fact]
    public void Setting_one_character_touches_no_other_and_not_the_active_one()
    {
        var s = new Settings();
        s.PickCharacter("전사", "워로드", "고독한기사");
        s.SetCore(Lonely, Digits(2, 2, 2));
        var active = s.ActiveCharacter;
        s.SetCore(Stance, Digits(3, 1, 2));
        Assert.Equal((false, 2, 2, 2), Fields(s.CoreOf(Lonely)));
        Assert.Equal(active, s.ActiveCharacter);
        Assert.Equal(2, s.Cores.Count);
    }

    [Fact]
    public void All_three_profiles_share_one_choice()
    {
        var s = new Settings();
        s.SetCore(Lonely, Digits(3, 1, 1) with { Apply = true });
        foreach (int p in new[] { 1, 2, 0 }) { s.SwitchProfile(p); Assert.Equal((true, 3, 1, 1), Fields(s.CoreOf(Lonely))); }
    }

    [Fact]
    public void Setting_digits_outside_1_to_3_clamps_them()
    {
        var s = new Settings();
        s.SetCore(Lonely, Digits(9, 0, -4));
        Assert.Equal((false, 3, 1, 1), Fields(s.CoreOf(Lonely)));
    }

    [Fact]
    public void WithSlot_adds_replaces_and_clears_a_trigger_slot_without_touching_the_original()
    {
        var a = new CoreChoice();
        var b = a.WithSlot("버스트 캐넌", SlotId.일반3).WithSlot("다른 스킬", SlotId.일반1);
        var c = b.WithSlot("버스트 캐넌", SlotId.일반4);
        var d = c.WithSlot("다른 스킬", null);
        Assert.Empty(a.Slots);
        Assert.Equal(SlotId.일반3, b.Slots["버스트 캐넌"]);
        Assert.Equal(SlotId.일반4, c.Slots["버스트 캐넌"]);
        Assert.Equal(SlotId.일반1, c.Slots["다른 스킬"]);
        Assert.Equal(["버스트 캐넌"], d.Slots.Keys);
    }

    [Fact]
    public void A_choice_round_trips_through_the_settings_file()
    {
        var store = new SettingsStore(FilePath);
        var s = new Settings();
        s.SetCore(Lonely, Digits(3, 2, 1).WithSlot("버스트 캐넌", SlotId.일반3) with { Apply = true });
        store.Save(s);

        var back = store.Load(out _).CoreOf(Lonely);
        Assert.Equal((true, 3, 2, 1), Fields(back));
        Assert.Equal(SlotId.일반3, back.Slots["버스트 캐넌"]);
    }

    /// <summary>No schema bump: a file from before 코어 빌드 loads with no choices, at the same version.</summary>
    [Fact]
    public void A_file_from_before_core_builds_loads_with_no_choices()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"Version\": 4, \"FirstRunDone\": true }");
        var back = new SettingsStore(FilePath).Load(out bool reset);
        Assert.False(reset);
        Assert.True(back.FirstRunDone);
        Assert.Empty(back.Cores);
        Assert.Equal(Settings.CurrentVersion, back.Version);
    }

    /// <summary>A hand edit: digits clamp to 1–3, a null entry and a slot the bar doesn't have are dropped, the rest stays.</summary>
    [Fact]
    public void A_hand_edited_file_is_sanitised_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            { "Cores": {
                "워로드 · 고독한기사": { "Apply": true, "Sun": 9, "Moon": 0, "Star": -5,
                                       "Slots": { "버스트 캐넌": 99, "다른 스킬": "일반3" } },
                "워로드 · 전투태세": null } }
            """);
        var back = new SettingsStore(FilePath).Load(out bool reset);
        Assert.False(reset);
        Assert.Equal(["워로드 · 고독한기사"], back.Cores.Keys);
        var c = back.CoreOf(Lonely);
        Assert.Equal((true, 3, 1, 1), Fields(c));
        Assert.Equal(["다른 스킬"], c.Slots.Keys);
        Assert.Equal(SlotId.일반3, c.Slots["다른 스킬"]);
    }

    static string Show(CoreChoice c) =>
        $"{c.Apply} {c.Sun} {c.Moon} {c.Star} {string.Join(",", c.Slots.Select(p => $"{p.Key}={p.Value}"))}";

    const string Fine = """{ "Apply": true, "Sun": 2, "Moon": 3, "Star": 2 }""";

    /// <summary>A hand edit of one choice loses only what is wrong with it: a bad slot name, a float or string level, a
    /// non-boolean Apply, a null or non-text slot value or a Slots that isn't an object. Never the whole settings file, and never
    /// the next entry.</summary>
    [Theory]
    [InlineData("""{ "Apply": true, "Sun": 2, "Moon": 3, "Star": 2, "Slots": { "x": "일반99", "y": "일반3" } }""", "True 2 3 2 y=일반3")]
    [InlineData("""{ "Apply": true, "Sun": 2, "Moon": 3, "Star": 2, "Slots": { "x": 99, "y": 3 } }""", "True 2 3 2 ")]
    [InlineData("""{ "Apply": true, "Sun": 1.5, "Moon": 3, "Star": 2 }""", "True 1 3 2 ")]
    [InlineData("""{ "Apply": true, "Sun": "2", "Moon": 3, "Star": 2 }""", "True 1 3 2 ")]
    [InlineData("""{ "Apply": "yes", "Sun": 2, "Moon": 3, "Star": 2 }""", "False 2 3 2 ")]
    [InlineData("""{ "Apply": true, "Sun": 2, "Moon": 3, "Star": 2, "Slots": { "x": null, "z": "일반1" } }""", "True 2 3 2 z=일반1")]
    [InlineData("""{ "Apply": true, "Sun": 2, "Moon": 3, "Star": 2, "Slots": null }""", "True 2 3 2 ")]
    [InlineData("""{ "Apply": true, "Sun": 2, "Moon": 3, "Star": 2, "Slots": [1] }""", "True 2 3 2 ")]
    [InlineData("""{ "Apply": true, "Sun": 2, "Moon": 3, "Star": 2, "Slots": { "y": "일반3", "y": "일반4" } }""", "True 2 3 2 y=일반4")]
    [InlineData("""{ "Apply": true, "Sun": 9, "Moon": 3, "Star": -1 }""", "True 3 3 1 ")]
    [InlineData("""{ }""", "False 1 1 1 ")]
    [InlineData("\"text\"", "dropped")]
    [InlineData("[1]", "dropped")]
    [InlineData("null", "dropped")]
    public void One_bad_choice_in_a_hand_edited_file_never_resets_the_file(string entry, string expected)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, $$"""{ "FirstRunDone": true, "Cores": { "워로드 · 고독한기사": {{entry}}, "워로드 · 전투태세": {{Fine}} } }""");
        var back = new SettingsStore(FilePath).Load(out bool reset);
        Assert.False(reset);
        Assert.True(back.FirstRunDone);
        Assert.Equal("True 2 3 2 ", Show(back.CoreOf(Stance)));
        Assert.Equal(expected, back.Cores.ContainsKey(Lonely) ? Show(back.Cores[Lonely]) : "dropped");
    }

    [Fact]
    public void Choices_compare_by_value_including_their_slots()
    {
        var a = new CoreChoice().WithSlot("x", SlotId.일반1);
        var same = a.WithSlot("x", SlotId.일반1);
        Assert.Equal(a, same);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(a, a.WithSlot("x", SlotId.일반2));
        Assert.NotEqual(a, a.WithSlot("y", SlotId.일반1));
        Assert.NotEqual(a, a with { Apply = true });
        Assert.NotEqual(a, a with { Star = 2 });
    }

    [Fact]
    public void Asking_for_a_core_that_does_not_exist_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CoreChoice().Of((CoreType)9));
    }

    [Fact]
    public void A_copy_with_one_core_level_changes_only_that_core_and_leaves_the_original()
    {
        var one = new CoreChoice { Apply = true };
        var three = one.With(CoreType.달, 3);
        Assert.Equal((true, 1, 3, 1), (three.Apply, three.Sun, three.Moon, three.Star));
        Assert.Equal((2, 1), (one.With(CoreType.해, 2).Sun, one.With(CoreType.별, 2).Sun));
        Assert.Equal(1, one.Moon);
        Assert.Throws<ArgumentOutOfRangeException>(() => one.With((CoreType)9, 2));
    }

    /// <summary>The slot a trigger skill can be in is a 일반 slot (the 설정 list offers 일반1–8): a hand-edited 아덴 or 각성기 slot would
    /// be shown as 선택 안 함 yet still used by the engine, so it is dropped on every way in.</summary>
    [Fact]
    public void Only_a_일반_slot_is_kept_as_a_trigger_slot()
    {
        var wide = new CoreChoice { Apply = true }.WithSlot("가", SlotId.아덴1).WithSlot("나", SlotId.각성기).WithSlot("다", SlotId.초각스)
            .WithSlot("라", SlotId.일반1).WithSlot("마", SlotId.일반8);
        Assert.Equal(["라", "마"], wide.Clamped().Slots.Keys.Order());

        var s = new Settings();
        s.SetCore("워로드 · 고독한기사", wide);
        Assert.Equal(["라", "마"], s.CoreOf("워로드 · 고독한기사").Slots.Keys.Order());

        var read = System.Text.Json.JsonSerializer.Deserialize<CoreChoice>("""{ "Slots": { "가": "아덴2", "나": "초각스", "라": "일반8" } }""")!;
        Assert.Equal(["라"], read.Slots.Keys);
    }
}
