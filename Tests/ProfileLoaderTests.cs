using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class ProfileLoaderTests
{
    private static readonly string Profiles = Path.Combine(RepoRoot(), "Profiles");

    private static ClassInfo Warlord => ProfileLoader.LoadClasses(Profiles).Single(c => c.Name == "워로드");

    [Fact]
    public void Loads_every_class_with_its_aden_slots_and_subclasses()
    {
        var classes = ProfileLoader.LoadClasses(Profiles);
        Assert.Equal(30, classes.Count);
        Assert.Equal(("스페셜리스트", "차원술사", 30), (classes[^1].Family, classes[^1].Name, classes[^1].Id));
        Assert.Equal(["시간술사", "공간술사"], classes[^1].Subclasses.Keys);
        Assert.Equal("워리어", Warlord.Family);
        Assert.Equal([SlotId.아덴2], Warlord.AdenSlots);
        Assert.Equal(["고독한기사", "전투태세"], Warlord.Subclasses.Keys);
        Assert.Equal(([], null), (Warlord.Subclasses["고독한기사"]!.Splits, Warlord.Subclasses["고독한기사"]!.Glow));   // shared gauge, a plain bar
        Assert.Equal(new GlowRule(GlowWhen.SlotReady, SlotId.아덴2, 0.5), Warlord.Subclasses["전투태세"]!.Glow);   // C lit at 50%
        Assert.Equal(DarkCue.Label, Warlord.AdenDark("전투태세"));   // its key label turns white, not the icon lit
        Assert.Equal(DarkCue.None, Warlord.AdenDark("고독한기사"));
    }

    [Theory]
    [InlineData("""{ "워리어": { "A": { "id": 1, "adenSlots": [], "subclasses": { "a": null } } } }""", Role.딜러)]
    [InlineData("""{ "워리어": { "A": { "id": 1, "role": "서포터", "adenSlots": [], "subclasses": { "a": null } } } }""", Role.서포터)]
    [InlineData("""{ "워리어": { "A": { "id": 1, "role": "탱커", "adenSlots": [], "subclasses": { "a": null } } } }""", null)]
    public void Role_is_read_from_the_file_and_defaults_to_딜러(string json, Role? role)
    {
        var dir = Directory.CreateTempSubdirectory("classes-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "classes.json"), json);
            if (role is null) { Assert.Throws<InvalidDataException>(() => ProfileLoader.LoadClasses(dir)); return; }
            Assert.Equal(role, ProfileLoader.LoadClasses(dir).Single().Role);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>The number is written in the file, so a class can be added inside its 직업군 without renumbering others.</summary>
    [Theory]
    [InlineData("""{ "워리어": { "A": { "id": 7, "adenSlots": [], "subclasses": { "a": null } }, "B": { "id": 3, "adenSlots": [], "subclasses": { "b": null } } } }""", null)]
    [InlineData("""{ "워리어": { "A": { "adenSlots": [], "subclasses": { "a": null } } } }""", "no id")]
    [InlineData("""{ "워리어": { "A": { "id": 0, "adenSlots": [], "subclasses": { "a": null } } } }""", "id 0")]
    [InlineData("""{ "워리어": { "A": { "id": 256, "adenSlots": [], "subclasses": { "a": null } } } }""", "id 256")]
    [InlineData("""{ "워리어": { "A": { "id": "1", "adenSlots": [], "subclasses": { "a": null } } } }""", "id as text")]
    [InlineData("""{ "워리어": { "A": { "id": 3, "adenSlots": [], "subclasses": { "a": null } } }, "헌터": { "B": { "id": 3, "adenSlots": [], "subclasses": { "b": null } } } }""", "same id twice")]
    public void Class_numbers_are_read_from_the_file_and_checked(string json, string? error)
    {
        var dir = Directory.CreateTempSubdirectory("classes-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "classes.json"), json);
            if (error is not null) { Assert.Throws<InvalidDataException>(() => ProfileLoader.LoadClasses(dir)); return; }
            Assert.Equal([("A", 7), ("B", 3)], ProfileLoader.LoadClasses(dir).Select(c => (c.Name, c.Id)));   // file order kept
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    static GaugeRule? Gauge(string adenGauge)
    {
        var dir = Directory.CreateTempSubdirectory("classes-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "classes.json"),
                $$"""{ "워리어": { "A": { "id": 1, "adenSlots": [], "subclasses": { "a": { "adenGauge": {{adenGauge}} } } } } }""");
            return ProfileLoader.LoadClasses(dir).Single().Subclasses["a"];
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AdenGauge_null_or_false_is_no_gauge_and_true_a_plain_one()
    {
        Assert.Null(Gauge("null"));
        Assert.Null(Gauge("false"));
        var plain = Gauge("true")!;
        Assert.Empty(plain.Splits);
        Assert.Null(plain.Glow);
    }

    [Fact]
    public void AdenGauge_object_gives_splits_in_order_and_the_glow_rule()
    {
        var bard = Gauge("""{ "splits": [0.75, 0.25, 0.5], "glow": { "when": "sections" } }""")!;
        Assert.Equal([0.25, 0.5, 0.75], bard.Splits);
        Assert.Equal(new GlowRule(GlowWhen.Sections), bard.Glow);

        var warlord = Gauge("""{ "splits": [0.5], "glow": { "when": "slotReady", "slot": "아덴2", "minGauge": 0.5 } }""")!;
        Assert.Equal(new GlowRule(GlowWhen.SlotReady, SlotId.아덴2, 0.5), warlord.Glow);
        Assert.Null(Gauge("{}")!.Glow);
        Assert.Empty(Gauge("{}")!.Ramp);
        Assert.Equal([2, 3], Gauge("""{ "splits": [0.5], "ramp": [2, 3] }""")!.Ramp);
        Assert.Empty(Gauge("{}")!.Spend);
        var spend = Gauge("""{ "splits": [0.5], "spend": { "slots": ["아덴1", "아덴2"], "max": 3 } }""")!.Spend.Single();
        Assert.Equal([SlotId.아덴1, SlotId.아덴2], spend.Slots);
        Assert.Equal(3, spend.Max);
        Assert.Null(Gauge("{}")!.Colors);
        Assert.Equal(new GaugeColors("#0A84E0", "#D7B932"), Gauge("""{ "colors": { "full": "#0A84E0", "filling": "#D7B932" } }""")!.Colors);
    }

    /// <summary>Using a skill in "spend" takes the full sections, the lowest first, up to "max"; the rest of the fill stays
    /// (바드: 4 crystals full and a use keeps one).</summary>
    [Theory]
    [InlineData(0.78, 0.03)]
    [InlineData(1.0, 0.25)]
    [InlineData(0.30, 0.05)]
    [InlineData(0.25, 0.0)]
    public void A_use_spends_the_full_sections_up_to_the_max(double before, double after) =>
        Assert.Equal(after, Gauge("""{ "splits": [0.25, 0.5, 0.75], "spend": { "slots": ["아덴1"], "max": 3 } }""")!.AfterSpend(SlotId.아덴1, before)!.Value, 1e-9);

    /// <summary>도화가: Z takes at most 1 full orb and C at most 2; 1.5 orbs full, C takes 1 and leaves the half.</summary>
    [Theory]
    [InlineData("아덴1", 0.81, 0.4767)]
    [InlineData("아덴2", 0.81, 0.1433)]
    [InlineData("아덴2", 0.5, 0.1667)]
    public void Each_spend_entry_has_its_own_max(string slot, double before, double after) =>
        Assert.Equal(after, Gauge("""{ "splits": [0.3333, 0.6667], "spend": [{ "slots": ["아덴1"], "max": 1 }, { "slots": ["아덴2"], "max": 2 }] }""")!
            .AfterSpend(Enum.Parse<SlotId>(slot), before)!.Value, 0.001);

    /// <summary>A flat share of the full gauge (워로드 전투태세 50%, 발키리 80%), not of what is there, taken only when that
    /// much is there: 전투태세 at 65% leaves 15%, and at 49% can't be used.</summary>
    [Theory]
    [InlineData(0.8, 1.0, 0.2)]
    [InlineData(0.8, 0.9, 0.1)]
    [InlineData(0.5, 0.65, 0.15)]
    [InlineData(0.5, 0.5, 0.0)]
    public void An_amount_spend_takes_that_share(double amount, double before, double after) =>
        Assert.Equal(after, AmountSpend(amount).AfterSpend(SlotId.아덴1, before)!.Value, 0.001);

    [Theory]
    [InlineData(0.8, 0.7)]
    [InlineData(0.5, 0.49)]
    public void An_amount_spend_needs_that_much(double amount, double before) =>
        Assert.Null(AmountSpend(amount).AfterSpend(SlotId.아덴1, before));

    static GaugeRule AmountSpend(double amount) =>
        Gauge($$"""{ "spend": { "slots": ["아덴1"], "amount": {{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}} } }""")!;

    /// <summary>How near a read must be to what a use leaves to confirm it: half the narrowest section, so a read one
    /// section off is never taken; a share spend has no sections and takes 0.1.</summary>
    [Fact]
    public void A_spend_confirms_within_half_the_narrowest_section()
    {
        Assert.Equal(0.125, Gauge("""{ "splits": [0.25, 0.5, 0.75], "spend": { "slots": ["아덴1"], "max": 3 } }""")!.SpendMargin(SlotId.아덴1), 0.001);
        Assert.Equal(0.1, Gauge("""{ "splits": [0.2, 0.5], "spend": { "slots": ["아덴1"], "max": 2 } }""")!.SpendMargin(SlotId.아덴1), 0.001);
        Assert.Equal(0.1, Gauge("""{ "spend": { "slots": ["아덴1"], "amount": 0.8 } }""")!.SpendMargin(SlotId.아덴1), 0.001);
    }

    [Fact]
    public void A_skill_not_in_spend_spends_nothing() =>
        Assert.Null(Gauge("""{ "splits": [0.5], "spend": { "slots": ["아덴1"], "max": 1 } }""")!.AfterSpend(SlotId.아덴2, 0.8));

    [Fact]
    public void A_use_with_no_full_section_or_no_spend_rule_spends_nothing()
    {
        Assert.Null(Gauge("""{ "splits": [0.25, 0.5, 0.75], "spend": { "slots": ["아덴1"], "max": 3 } }""")!.AfterSpend(SlotId.아덴1, 0.2));
        Assert.Null(Gauge("""{ "splits": [0.5] }""")!.AfterSpend(SlotId.아덴1, 0.8));
    }

    [Theory]
    [InlineData("""{ "splits": [1.2] }""")]
    [InlineData("""{ "splits": [0] }""")]
    [InlineData("""{ "splits": "half" }""")]
    [InlineData("""{ "glow": { "when": "always" } }""")]
    [InlineData("""{ "glow": { "when": "slotReady" } }""")]
    [InlineData("""{ "glow": { "when": "slotReady", "slot": "Z" } }""")]
    [InlineData("""{ "glow": { "when": "slotReady", "slot": "아덴1", "minGauge": 2 } }""")]
    [InlineData("""{ "glow": true }""")]
    [InlineData("""{ "glow": "sections" }""")]
    [InlineData("3")]
    [InlineData("""{ "ramp": [0] }""")]
    [InlineData("""{ "ramp": [1, 4] }""")]
    [InlineData("""{ "ramp": "up" }""")]
    [InlineData("""{ "spend": { "max": 3 } }""")]
    [InlineData("""{ "spend": { "slots": ["Z"], "max": 3 } }""")]
    [InlineData("""{ "spend": { "slots": ["아덴1"], "max": 0 } }""")]
    [InlineData("""{ "spend": { "slots": ["아덴1"] } }""")]
    [InlineData("""{ "spend": true }""")]
    [InlineData("""{ "colors": { "full": "#0A84E0" } }""")]
    [InlineData("""{ "colors": { "full": "blue", "filling": "#D7B932" } }""")]
    [InlineData("""{ "colors": "gold" }""")]
    [InlineData("""{ "splits": [0.5], "spend": { "slots": ["아덴1"], "max": 1, "immediate": "true" } }""")]
    public void A_bad_adenGauge_is_rejected(string adenGauge) =>
        Assert.Throws<InvalidDataException>(() => Gauge(adenGauge));

    [Fact]
    public void A_glow_may_need_a_full_gauge() =>
        Assert.Equal(1, Gauge("""{ "glow": { "when": "slotReady", "slot": "아덴1", "minGauge": 1 } }""")!.Glow!.MinGauge);

    static ClassInfo Load(string json)
    {
        var dir = Directory.CreateTempSubdirectory("classes-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "classes.json"), json);
            return ProfileLoader.LoadClasses(dir).Single();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>A 직업각인 can set its own role and 아덴 slots; both go dark without enough resource, and
    /// the class draws Z and C at its own spots.</summary>
    [Fact]
    public void A_직업각인_can_set_its_own_role_aden_slots_and_dark_state()
    {
        var c = Load("""
            { "워리어": { "A": { "id": 1, "role": "서포터", "adenSlots": [], "adenRects": { "아덴1": [888, 1026, 40, 43] },
              "subclasses": { "딜": { "role": "딜러", "adenSlots": ["아덴1"], "adenDark": true, "adenGauge": null }, "폿": { "adenGauge": null } } } } }
            """);
        Assert.Equal((Role.딜러, Role.서포터, Role.서포터), (c.RoleOf("딜"), c.RoleOf("폿"), c.RoleOf(null)));
        Assert.Equal([SlotId.아덴1], c.AdenSlotsOf("딜"));
        Assert.Empty(c.AdenSlotsOf("폿"));
        Assert.Equal(DarkCue.Icon, c.AdenDark("딜"));
        Assert.Equal(DarkCue.None, c.AdenDark("폿"));
        Assert.Equal(new Dictionary<SlotId, PixelRect> { [SlotId.아덴1] = new(888, 1026, 40, 43) }, c.AdenRects);
    }

    [Theory]
    [InlineData("""{ "워리어": { "A": { "id": 1, "adenSlots": [], "adenRects": { "일반1": [1, 2, 3, 4] }, "subclasses": { "a": null } } } }""")]
    [InlineData("""{ "워리어": { "A": { "id": 1, "adenSlots": [], "adenRects": { "아덴1": [1, 2, 3] }, "subclasses": { "a": null } } } }""")]
    [InlineData("""{ "워리어": { "A": { "id": 1, "adenSlots": [], "adenRects": { "아덴1": [1, 2, 0, 4] }, "subclasses": { "a": null } } } }""")]
    [InlineData("""{ "워리어": { "A": { "id": 1, "adenSlots": [], "subclasses": { "a": { "role": "탱커" } } } } }""")]
    [InlineData("""{ "워리어": { "A": { "id": 1, "adenSlots": [], "subclasses": { "a": { "adenDark": "yes" } } } } }""")]
    public void Bad_class_or_직업각인_data_is_rejected(string json) => Assert.Throws<InvalidDataException>(() => Load(json));
}
