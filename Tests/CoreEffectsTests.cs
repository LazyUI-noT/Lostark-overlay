using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>코어 빌드's data: Profiles/cores/&lt;클래스&gt;.json read into effects, which of them a choice switches on, and the
/// slots to watch because of them.</summary>
public sealed class CoreEffectsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GaugeOverlayTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    const string Burst = "버스트 캐넌";

    const string Sample = """
        { "subclasses": { "고독한기사": { "effects": [ {
            "requires": { "해": 3 }, "trigger": "버스트 캐넌", "effect": "cooldown_reset", "target": "아덴2",
            "watchSeconds": 1.0, "tag": "CURRENT", "source": "owner 2026-10-12" } ] } } }
        """;

    string WriteCores(string cls, string json)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "cores"));
        File.WriteAllText(Path.Combine(_dir, "cores", cls + ".json"), json);
        return _dir;
    }

    static CoreEffect Reset(Dictionary<CoreType, int> requires, string trigger = Burst, SlotId target = SlotId.아덴2, double seconds = 1.0) =>
        new(requires, CoreEffects.Reset, trigger, target, seconds, null, "CURRENT", "test");

    static CoreChoice Chosen(int sun = 1, int moon = 1, int star = 1, bool apply = true) =>
        new() { Apply = apply, Sun = sun, Moon = moon, Star = star };

    // --- loading ---

    [Fact]
    public void A_cores_file_loads_its_effects_per_subclass()
    {
        var data = CoreEffects.Load(WriteCores("워로드", Sample), "워로드");
        var e = Assert.Single(data["고독한기사"]);
        Assert.Equal(3, e.Requires[CoreType.해]);
        Assert.Equal((Burst, CoreEffects.Reset, SlotId.아덴2, 1.0), (e.Trigger, e.Kind, e.Target, e.WatchSeconds));
        Assert.Equal(("CURRENT", "owner 2026-10-12"), (e.Tag, e.Source));
    }

    [Fact]
    public void A_missing_file_or_class_means_no_effects_not_an_error()
    {
        Assert.Empty(CoreEffects.Load(WriteCores("워로드", Sample), "발키리"));
        Assert.Empty(CoreEffects.Load(Path.Combine(_dir, "nowhere"), "워로드"));
    }

    /// <summary>A valid file sits where the name would lead if it were let through (up out of cores/, into a folder, the file
    /// ".json"), so only the guard keeps these from loading.</summary>
    [Theory]
    [InlineData("..\\워로드", "워로드.json")]
    [InlineData("a/b", "cores/a/b.json")]
    [InlineData("", "cores/.json")]
    public void A_class_name_that_is_not_a_file_name_has_no_effects(string cls, string whereItWouldResolve)
    {
        var file = Path.Combine(_dir, whereItWouldResolve);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, Sample);
        Assert.Empty(CoreEffects.Load(_dir, cls));
    }

    [Theory]
    [InlineData("[not json")]
    [InlineData("[]")]
    [InlineData("""{ "subclasses": { "a": { "effects": [] }, "a": { "effects": [] } } }""")]                                          // duplicate 직업각인
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1, "해": 2 }, "effect": "text", "text": "t" } ] } } }""")]   // duplicate core
    [InlineData("{ \"subclasses\": { \"고독한기사\": { \"effects\": [ { \"requires\": { \"해\": 3 }, \"effect\": \"cooldown_reset\", \"trigger\": \"x\" } ] } } }")]
    public void A_malformed_file_means_no_effects_not_a_crash(string json)
    {
        var data = CoreEffects.Load(WriteCores("워로드", json), "워로드");
        Assert.Empty(data);
    }

    [Theory]
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "독": 3 }, "effect": "text", "text": "t" } ] } } }""")]      // unknown core
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 4 }, "effect": "text", "text": "t" } ] } } }""")]      // digit above 3
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 0 }, "effect": "text", "text": "t" } ] } } }""")]      // digit below 1
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { }, "effect": "text", "text": "t" } ] } } }""")]              // requires nothing
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "effect": "text", "text": "t" } ] } } }""")]                               // no requires
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "boom", "text": "t" } ] } } }""")]      // unknown kind
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "cooldown_reset", "target": "아덴2" } ] } } }""")]   // no trigger
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "cooldown_reduce", "trigger": "x" } ] } } }""")]    // no target
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "cooldown_reset", "trigger": "x", "target": "아덴9" } ] } } }""")]
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "cooldown_reset", "trigger": "x", "target": "아덴2", "watchSeconds": 0 } ] } } }""")]
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "cooldown_reset", "trigger": "x", "target": "아덴2", "watchSeconds": 5.5 } ] } } }""")]
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "text" } ] } } }""")]                       // text kind without text
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "0": 1 }, "effect": "text", "text": "t" } ] } } }""")]        // a number is not a core name
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해,달": 1 }, "effect": "text", "text": "t" } ] } } }""")]
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "cooldown_reset", "trigger": "x", "target": "3" } ] } } }""")]
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "cooldown_reset", "trigger": "x", "target": "아덴1,아덴2" } ] } } }""")]
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "cooldown_reset", "trigger": "", "target": "아덴2" } ] } } }""")]
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "cooldown_reset", "trigger": "  ", "target": "아덴2" } ] } } }""")]
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "cooldown_reset", "trigger": "x", "target": "아덴2", "watchSecond": 2 } ] } } }""")]   // misspelt key
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "text", "text": "t", "tags": "CURRENT" } ] } } }""")]
    [InlineData("""{ "subclasses": { "a": { "effects": [ { "requires": { "해": 1 }, "effect": "text", "text": "t", "sources": "me" } ] } } }""")]
    [InlineData("""{ "subclasses": { "a": { "effect": [] } } }""")]
    [InlineData("""{ "subclasses": { "a": { "effects": [], "extra": 1 } } }""")]
    [InlineData("""{ "subclass": { "a": { "effects": [] } } }""")]
    [InlineData("""{ "subclasses": [] }""")]
    [InlineData("""{ "subclasses": { "a": { "effects": {} } } }""")]
    public void Parse_rejects_what_would_silently_do_nothing(string json) =>
        Assert.Throws<InvalidDataException>(() => CoreEffects.Parse(json));

    [Fact]
    public void Parse_accepts_the_edges_of_the_ranges_and_a_text_only_effect()
    {
        var data = CoreEffects.Parse("""
            { "subclasses": { "a": { "effects": [
                { "requires": { "해": 1, "달": 3 }, "effect": "cooldown_reduce", "trigger": "x", "target": "아덴1", "watchSeconds": 5 },
                { "requires": { "별": 2 }, "effect": "text", "text": "피해만 늘어납니다" } ] } } }
            """);
        Assert.Equal(2, data["a"].Count);
        Assert.Equal(5, data["a"][0].WatchSeconds);
        Assert.Equal("피해만 늘어납니다", data["a"][1].Text);
        Assert.False(data["a"][1].Tracks);
    }

    /// <summary>Notes starting with an underscore are allowed anywhere, as in classes.json.</summary>
    [Fact]
    public void Parse_allows_underscore_notes_at_every_level()
    {
        var data = CoreEffects.Parse("""
            { "_note": "n", "subclasses": { "a": { "_why": 1, "effects": [
                { "_n": "x", "requires": { "해": 1 }, "effect": "text", "text": "t" } ] } } }
            """);
        Assert.Single(data["a"]);
    }

    // --- Active ---

    [Fact]
    public void Nothing_is_active_unless_the_build_is_applied()
    {
        var e = Reset(new() { [CoreType.해] = 3 });
        Assert.Empty(CoreEffects.Active(Chosen(sun: 3, apply: false), Role.딜러, [e]));
        Assert.Single(CoreEffects.Active(Chosen(sun: 3), Role.딜러, [e]));
    }

    [Fact]
    public void An_effect_is_active_only_when_the_chosen_digits_equal_what_it_requires()
    {
        var e = Reset(new() { [CoreType.해] = 3 });
        Assert.Empty(CoreEffects.Active(Chosen(sun: 2), Role.딜러, [e]));
        Assert.Empty(CoreEffects.Active(Chosen(sun: 1), Role.딜러, [e]));
        Assert.Single(CoreEffects.Active(Chosen(sun: 3), Role.딜러, [e]));
    }

    [Fact]
    public void An_effect_that_names_only_the_sun_ignores_the_other_cores()
    {
        var e = Reset(new() { [CoreType.해] = 3 });
        Assert.Single(CoreEffects.Active(Chosen(sun: 3, moon: 1, star: 1), Role.딜러, [e]));
        Assert.Single(CoreEffects.Active(Chosen(sun: 3, moon: 3, star: 2), Role.딜러, [e]));
    }

    [Fact]
    public void An_effect_that_names_two_cores_needs_both()
    {
        var e = Reset(new() { [CoreType.해] = 3, [CoreType.달] = 2 });
        Assert.Empty(CoreEffects.Active(Chosen(sun: 3, moon: 1), Role.딜러, [e]));
        Assert.Single(CoreEffects.Active(Chosen(sun: 3, moon: 2), Role.딜러, [e]));
    }

    [Fact]
    public void Dealers_choose_the_sun_and_moon_and_supporters_the_star_only()
    {
        var sun = Reset(new() { [CoreType.해] = 3 });
        var moon = Reset(new() { [CoreType.달] = 3 });
        var star = Reset(new() { [CoreType.별] = 3 });
        var all = new[] { sun, moon, star };
        var choice = Chosen(3, 3, 3);
        Assert.Equal([sun, moon], CoreEffects.Active(choice, Role.딜러, all));
        Assert.Equal([star], CoreEffects.Active(choice, Role.서포터, all));
    }

    /// <summary>Only a read that failed (a locked file) is worth trying again; a missing or malformed file will read the same next time.</summary>
    [Fact]
    public void Load_tells_a_read_failure_from_a_missing_or_malformed_file()
    {
        var dir = WriteCores("워로드", Sample);
        Assert.NotEmpty(CoreEffects.Load(dir, "워로드", out bool failed));
        Assert.False(failed);

        Assert.Empty(CoreEffects.Load(dir, "발키리", out failed));
        Assert.False(failed);

        WriteCores("바드", "[not json");
        Assert.Empty(CoreEffects.Load(dir, "바드", out failed));
        Assert.False(failed);

        using (new FileStream(Path.Combine(dir, "cores", "워로드.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Empty(CoreEffects.Load(dir, "워로드", out failed));
        Assert.True(failed);
    }

    // --- Watches ---

    [Fact]
    public void An_active_effect_with_a_chosen_trigger_slot_gives_one_watch()
    {
        var e = Reset(new() { [CoreType.해] = 3 }, seconds: 1.5);
        var choice = Chosen(sun: 3).WithSlot(Burst, SlotId.일반3);
        var w = Assert.Single(CoreEffects.Watches(choice, Role.딜러, [e]));
        Assert.Equal((SlotId.일반3, SlotId.아덴2, TimeSpan.FromSeconds(1.5)), (w.TriggerSlot, w.TargetSlot, w.Duration));
    }

    [Fact]
    public void No_slot_chosen_for_the_trigger_gives_no_watch()
    {
        var e = Reset(new() { [CoreType.해] = 3 });
        Assert.Empty(CoreEffects.Watches(Chosen(sun: 3), Role.딜러, [e]));
        Assert.Empty(CoreEffects.Watches(Chosen(sun: 3).WithSlot("다른 스킬", SlotId.일반3), Role.딜러, [e]));
    }

    [Fact]
    public void An_unapplied_or_unmatched_build_gives_no_watch()
    {
        var e = Reset(new() { [CoreType.해] = 3 });
        Assert.Empty(CoreEffects.Watches(Chosen(sun: 3, apply: false).WithSlot(Burst, SlotId.일반3), Role.딜러, [e]));
        Assert.Empty(CoreEffects.Watches(Chosen(sun: 2).WithSlot(Burst, SlotId.일반3), Role.딜러, [e]));
    }

    [Fact]
    public void A_text_only_effect_never_gives_a_watch()
    {
        var e = new CoreEffect(new Dictionary<CoreType, int> { [CoreType.해] = 3 }, CoreEffects.TextOnly, Burst, SlotId.아덴2, 1, "피해만", "CURRENT", "test");
        Assert.Empty(CoreEffects.Watches(Chosen(sun: 3).WithSlot(Burst, SlotId.일반3), Role.딜러, [e]));
    }

    [Fact]
    public void Two_effects_on_different_triggers_give_two_watches()
    {
        var a = Reset(new() { [CoreType.해] = 3 }, trigger: "가", target: SlotId.아덴1);
        var b = Reset(new() { [CoreType.해] = 3 }, trigger: "나", target: SlotId.아덴2);
        var choice = Chosen(sun: 3).WithSlot("가", SlotId.일반1).WithSlot("나", SlotId.일반2);
        var ws = CoreEffects.Watches(choice, Role.딜러, [a, b]);
        Assert.Equal([(SlotId.일반1, SlotId.아덴1), (SlotId.일반2, SlotId.아덴2)], ws.Select(w => (w.TriggerSlot, w.TargetSlot)));
    }

    // --- After a cast (what the engine asks) ---

    [Fact]
    public void A_cast_of_the_trigger_slot_watches_the_target_until_the_frame_time_plus_the_duration()
    {
        var watches = new[] { new CoreWatch(SlotId.일반3, SlotId.아덴2, TimeSpan.FromSeconds(1.5)) };
        var at = TimeSpan.FromSeconds(10);
        var until = Assert.Single(CoreEffects.WatchesAfterCast(watches, SlotId.일반3, at));
        Assert.Equal((SlotId.아덴2, TimeSpan.FromSeconds(11.5)), until);
    }

    [Fact]
    public void A_cast_of_another_slot_or_no_watches_at_all_watches_nothing()
    {
        var watches = new[] { new CoreWatch(SlotId.일반3, SlotId.아덴2, TimeSpan.FromSeconds(1)) };
        Assert.Empty(CoreEffects.WatchesAfterCast(watches, SlotId.일반4, TimeSpan.Zero));
        Assert.Empty(CoreEffects.WatchesAfterCast([], SlotId.일반3, TimeSpan.Zero));
        Assert.Empty(CoreEffects.WatchesAfterCast(null, SlotId.일반3, TimeSpan.Zero));
    }

    [Fact]
    public void Two_watches_on_one_trigger_slot_both_apply()
    {
        var watches = new[]
        {
            new CoreWatch(SlotId.일반1, SlotId.아덴1, TimeSpan.FromSeconds(1)),
            new CoreWatch(SlotId.일반1, SlotId.아덴2, TimeSpan.FromSeconds(2)),
        };
        var after = CoreEffects.WatchesAfterCast(watches, SlotId.일반1, TimeSpan.FromSeconds(5));
        Assert.Equal([(SlotId.아덴1, TimeSpan.FromSeconds(6)), (SlotId.아덴2, TimeSpan.FromSeconds(7))], after);
    }

    [Fact]
    public void Applying_after_a_cast_hands_each_watch_to_the_callback()
    {
        var got = new List<(SlotId, TimeSpan)>();
        var watches = new[] { new CoreWatch(SlotId.일반2, SlotId.아덴1, TimeSpan.FromSeconds(2)) };
        CoreEffects.ApplyAfterCast(watches, SlotId.일반2, TimeSpan.FromSeconds(3), (t, u) => got.Add((t, u)));
        CoreEffects.ApplyAfterCast(watches, SlotId.일반5, TimeSpan.FromSeconds(3), (t, u) => got.Add((t, u)));
        CoreEffects.ApplyAfterCast(null, SlotId.일반2, TimeSpan.FromSeconds(3), (t, u) => got.Add((t, u)));
        Assert.Equal([(SlotId.아덴1, TimeSpan.FromSeconds(5))], got);
    }

    // --- Describe ---

    [Fact]
    public void The_target_is_named_with_the_users_key_when_there_is_one()
    {
        var e = Reset(new() { [CoreType.해] = 3 });
        Assert.Equal("3xx: 버스트 캐넌을 쓰면 아덴2(C) 쿨초를 빨리 읽습니다", CoreEffects.Describe([e], _ => "C"));
        Assert.Equal("3xx: 버스트 캐넌을 쓰면 아덴2 쿨초를 빨리 읽습니다", CoreEffects.Describe([e], s => s.ToString()));   // no key set: the slot's name
        Assert.Equal("3xx: 버스트 캐넌을 쓰면 아덴2 쿨초를 빨리 읽습니다", CoreEffects.Describe([e]));
    }

    // --- The worked example, from the shipped file to the scheduler ---

    [Fact]
    public void The_shipped_example_reads_the_target_on_every_frame_for_its_time_after_the_chosen_slot_is_cast_and_not_before()
    {
        static TimeSpan T(double s) => TimeSpan.FromSeconds(s);
        var effects = CoreEffects.Load(Path.Combine(Helpers.RepoRoot(), "Profiles"), "워로드")["고독한기사"];
        var watches = CoreEffects.Watches(Chosen(sun: 3).WithSlot(Burst, SlotId.일반3), Role.딜러, effects);
        var scheduler = new GaugeOverlay.Core.Cooldown.ReadScheduler();
        SlotState[] states = [new(SlotId.아덴2, SlotPhase.Cooldown, T(30.5), T(30.5), false, TimeSpan.Zero)];   // far from an edge at the times below
        scheduler.MarkRead(SlotId.아덴2, T(10.0));
        Assert.Empty(scheduler.Due(T(10.01), states));

        CoreEffects.ApplyAfterCast(watches, SlotId.일반3, T(10.0), scheduler.Watch);   // the engine's own call

        for (int k = 0; k <= 60; k++)
        {
            Assert.Equal([SlotId.아덴2], scheduler.Due(T(10.0 + k / 60.0), states));
            scheduler.MarkRead(SlotId.아덴2, T(10.0 + k / 60.0));
        }
        Assert.Empty(scheduler.Due(T(11.01), states));   // the watch (1.0 s from the frame time) is over
        Assert.Empty(CoreEffects.WatchesAfterCast(watches, SlotId.일반4, T(20)));   // another slot's cast starts none
    }

    // --- "self": the trigger's own slot (워로드 전투태세 해 2) ---

    static CoreEffect SelfReset() => CoreEffects.Parse("""
        { "subclasses": { "a": { "effects": [
            { "requires": { "해": 2 }, "effect": "cooldown_reset", "trigger": "방패 돌진", "target": "self" } ] } } }
        """)["a"][0];

    [Fact]
    public void A_self_target_watches_and_resets_the_triggers_own_slot()
    {
        var e = SelfReset();
        Assert.True(e.Tracks);
        Assert.Null(e.Target);
        var w = Assert.Single(CoreEffects.Watches(Chosen(sun: 2).WithSlot("방패 돌진", SlotId.일반4), Role.딜러, [e]));
        Assert.Equal((SlotId.일반4, SlotId.일반4), (w.TriggerSlot, w.TargetSlot));
        Assert.True(CoreEffects.ResetsOnUse([w], SlotId.일반4));
        Assert.False(CoreEffects.ResetsOnUse([w], SlotId.일반8));
        Assert.False(CoreEffects.ResetsOnUse([new CoreWatch(SlotId.일반4, SlotId.아덴2, TimeSpan.FromSeconds(1))], SlotId.일반4));
        Assert.False(CoreEffects.ResetsOnUse(null, SlotId.일반4));
    }

    [Fact]
    public void A_self_target_is_described_as_a_reset_on_use() =>
        Assert.Equal("2xx: 방패 돌진을 쓴 뒤 바로 다시 준비되면 곧바로 보여 줍니다", CoreEffects.Describe([SelfReset()]));

    // --- the core code (user, 2026-10-13): 해, 달, 별 in that order, x where the effect doesn't ask ---

    [Theory]
    [InlineData("해", 3, null, 0, "3xx")]
    [InlineData("달", 2, null, 0, "x2x")]
    [InlineData("별", 1, null, 0, "xx1")]
    [InlineData("해", 1, "달", 3, "13x")]
    public void The_core_code_puts_each_level_in_its_place(string a, int la, string? b, int lb, string code)
    {
        var requires = new Dictionary<CoreType, int> { [Enum.Parse<CoreType>(a)] = la };
        if (b is not null) requires[Enum.Parse<CoreType>(b)] = lb;
        Assert.Equal(code, CoreEffects.Code(requires));
    }

    // --- the slot rows (user, 2026-10-13): a skill's slot is asked only when its core is picked ---

    [Fact]
    public void A_triggers_slot_is_asked_only_at_the_levels_its_effect_needs()
    {
        var e = Reset(new() { [CoreType.해] = 3 });
        Assert.Empty(CoreEffects.Triggers(Chosen(sun: 1), Role.딜러, [e]));
        Assert.Equal([Burst], CoreEffects.Triggers(Chosen(sun: 3), Role.딜러, [e]));
        Assert.Equal([Burst], CoreEffects.Triggers(Chosen(sun: 3, apply: false), Role.딜러, [e]));   // off: still shown, greyed in 설정
    }
}
