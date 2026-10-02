using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Checks Profiles/ stays consistent as classes are added, so a new class works everywhere without code
/// changes: in the 캐릭터 tab, over the party protocol and in the party rows.</summary>
public class ClassDataTests
{
    private static readonly string Profiles = Path.Combine(RepoRoot(), "Profiles");
    private static IReadOnlyList<ClassInfo> Classes => ProfileLoader.LoadClasses(Profiles);

    /// <summary>The class numbers already given out (sent to the party, so every member must agree): Numbered[i] is
    /// "id": i + 1 in classes.json. A new class may go anywhere in the file with the next unused id, and is added here;
    /// never renumber or rename one.</summary>
    private static readonly string[] Numbered =
    [
        "워로드", "홀리나이트", "버서커", "디스트로이어", "가디언나이트", "발키리", "슬레이어",
        "배틀마스터", "인파이터", "기공사", "창술사", "스트라이커", "브레이커",
        "데빌헌터", "블래스터", "호크아이", "스카우터", "건슬링어",
        "바드", "서머너", "아르카나", "소서리스",
        "블레이드", "데모닉", "리퍼", "소울이터",
        "도화가", "기상술사", "환수사", "차원술사",
    ];

    [Fact]
    public void Existing_class_numbers_never_change()
    {
        var byName = Classes.ToDictionary(c => c.Name, c => c.Id);
        Assert.Equal(Numbered.Select((name, i) => (name, i + 1)), Numbered.Select(name => (name, byName.GetValueOrDefault(name))));
    }

    [Fact]
    public void Every_class_is_numbered_here() =>
        Assert.Equal(Numbered.Length, Classes.Count);

    [Fact]
    public void Class_names_are_unique() =>
        Assert.Empty(Classes.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key));

    /// <summary>A character is saved under "클래스 · 직업각인", so a class without one can't be saved (차원술사 was added
    /// with none: its character stayed "새 캐릭터" and the next new character overwrote it).</summary>
    [Fact]
    public void Every_class_has_a_subclass() =>
        Assert.Empty(Classes.Where(c => c.Subclasses.Count == 0).Select(c => c.Name));

    [Fact]
    public void Every_class_has_an_emblem() =>
        Assert.Empty(Classes.Where(c => !File.Exists(Path.Combine(Profiles, "emblems", c.Name + ".png"))).Select(c => c.Name));

    [Fact]
    public void Every_emblem_belongs_to_a_class()
    {
        var names = Classes.Select(c => c.Name).ToHashSet();
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(Profiles, "emblems"), "*.png")
            .Select(Path.GetFileNameWithoutExtension), n => !names.Contains(n!));
    }

    /// <summary>The default role, before the user's own way of telling each 직업각인 apart; drives the party's
    /// 딜러/서포터 colours.</summary>
    [Fact]
    public void Supporters_by_default_are_바드_홀리나이트_도화가_발키리() =>
        Assert.Equal(["홀리나이트", "발키리", "바드", "도화가"], Classes.Where(c => c.Role == Role.서포터).Select(c => c.Name));

    /// <summary>바드's gauge deepens as its sections fill: 절실한구원 weak/medium/strong at 1/2/3 full (the 4th changes
    /// nothing), 진실된용맹 medium/strong at 1/2 full.</summary>
    [Fact]
    public void 바드_ramps_by_full_sections()
    {
        var b = Classes.Single(c => c.Name == "바드");
        Assert.Equal([1, 2, 3], b.Subclasses["절실한구원"]!.Ramp);
        Assert.Equal([2, 3], b.Subclasses["진실된용맹"]!.Ramp);
    }

    /// <summary>바드's Z or X spends the full crystals: 절실한구원 at most 3 of its 4 (bard bug.mp4: 4 full, one kept),
    /// 진실된용맹 at most 2 of its 3 (from the user).</summary>
    [Theory]
    [InlineData("절실한구원", 3)]
    [InlineData("진실된용맹", 2)]
    public void 바드_z_and_x_spend_the_full_crystals(string sub, int max)
    {
        var spend = Classes.Single(c => c.Name == "바드").Subclasses[sub]!.Spend;
        foreach (var slot in new[] { SlotId.아덴1, SlotId.아덴2 })
            Assert.Equal(max, spend.Single(s => s.Slots.Contains(slot)).Max);
    }

    /// <summary>도화가 (both 직업각인): Z spends 1 or 2 full orbs, C at most 1 (from the user).</summary>
    [Theory]
    [InlineData("만개")]
    [InlineData("회귀")]
    public void 도화가_z_spends_up_to_2_orbs_and_c_1(string sub)
    {
        var spend = Classes.Single(c => c.Name == "도화가").Subclasses[sub]!.Spend;
        Assert.Equal(2, spend.Single(s => s.Slots.Contains(SlotId.아덴1)).Max);
        Assert.Equal(1, spend.Single(s => s.Slots.Contains(SlotId.아덴2)).Max);
        Assert.All(spend, s => Assert.True(s.Immediate));   // always spent: the gauge drops at the press
    }

    /// <summary>바드: the first Z or X spends at once and the next use within 8 s is free (절실한구원, up to 3 crystals); 진실된용맹 the
    /// same for Z only (from the user).</summary>
    [Fact]
    public void 바드_spends_at_the_press_with_a_free_recast()
    {
        var rescue = Classes.Single(c => c.Name == "바드").Subclasses["절실한구원"]!;
        foreach (var slot in new[] { SlotId.아덴1, SlotId.아덴2 }) { Assert.True(rescue.SpendsAtOnce(slot)); Assert.Equal(8, rescue.FreeRecast(slot)); }
        Assert.Equal(3, rescue.Spend.Single().Max);
        var valor = Classes.Single(c => c.Name == "바드").Subclasses["진실된용맹"]!;
        Assert.True(valor.SpendsAtOnce(SlotId.아덴1));
        Assert.False(valor.SpendsAtOnce(SlotId.아덴2));
    }

    /// <summary>From the user: 워로드 전투태세 spends 50%, 발키리 80% (the skill its glow waits for), 고독한기사 nothing.</summary>
    [Theory]
    [InlineData("워로드", "전투태세", SlotId.아덴2, 0.5)]
    [InlineData("발키리", "빛의기사", SlotId.아덴1, 0.8)]
    [InlineData("발키리", "해방자", SlotId.아덴1, 0.8)]
    public void Share_spends_follow_the_user(string cls, string sub, SlotId slot, double amount)
    {
        var spend = Classes.Single(c => c.Name == cls).Subclasses[sub]!.Spend.Single();
        Assert.Equal([slot], spend.Slots);
        Assert.Equal(amount, spend.Amount);
    }

    /// <summary>바드's bar by default: gold while a crystal fills, blue once it is full, as in the game.</summary>
    [Fact]
    public void 바드_fills_gold_and_turns_blue()
    {
        var b = Classes.Single(c => c.Name == "바드");
        foreach (var sub in new[] { "절실한구원", "진실된용맹" })
            Assert.Equal(new GaugeColors("#0A84E0", "#D7B932"), b.Subclasses[sub]!.Colors);
    }

    /// <summary>발키리 tracks Z and C in both 직업각인: 해방자's Z needs 80% of the gauge and unlocks C, 빛의 기사's C needs
    /// a full gauge and unlocks Z.</summary>
    [Fact]
    public void 발키리_glows_on_Z_at_80_for_해방자_and_on_C_at_100_for_빛의기사()
    {
        var v = Classes.Single(c => c.Name == "발키리");
        Assert.Equal([SlotId.아덴1, SlotId.아덴2], v.AdenSlotsOf("해방자"));
        Assert.Equal([SlotId.아덴1, SlotId.아덴2], v.AdenSlotsOf("빛의기사"));
        Assert.Equal(new GlowRule(GlowWhen.SlotReady, SlotId.아덴1, 0.8), v.Subclasses["해방자"]!.Glow);
        Assert.Equal(new GlowRule(GlowWhen.SlotReady, SlotId.아덴2, 1), v.Subclasses["빛의기사"]!.Glow);
    }
}
