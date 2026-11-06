using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class SkillLibraryTests
{
    static IconPrint Print(byte seed)
    {
        var img = new Image(48, 48);
        var rng = new Random(seed);
        rng.NextBytes(img.Bgra);
        return IconPrint.Of(img);
    }

    static LibrarySkill Skill(string cls, string name, SlotGroup kind, string icon, string? sub = null) =>
        new(cls, name, kind, icon, sub, [Print((byte)name.Length), Print((byte)(name.Length + 1))]);

    static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "skilllib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void A_saved_class_loads_back_with_its_pools_by_kind()
    {
        var dir = TempDir();
        SkillLibrary.Save(dir, "워로드", [Skill("워로드", "차지 스팅거", SlotGroup.일반스킬, "gl_skill/gl_skill_01_30.png"),
            Skill("워로드", "풀배럴 캐넌", SlotGroup.초각스, "gl_skill/gl_skill_01_41.png", "고독한 기사"),
            Skill("워로드", "가디언의 수호", SlotGroup.각성기, "gl_skill/gl_skill_01_28.png")]);

        var lib = SkillLibrary.Load(dir);

        Assert.Equal(["워로드"], lib.Classes);
        var normal = Assert.Single(lib.Pool("워로드", SlotGroup.일반스킬));
        Assert.Equal("차지 스팅거", normal.Name);
        Assert.Equal(2, normal.Prints.Count);
        Assert.Equal(1, normal.Prints[0].Similarity(Print((byte)"차지 스팅거".Length)), 6);
        Assert.Equal("고독한 기사", Assert.Single(lib.Pool("워로드", SlotGroup.초각스)).Subclass);
        Assert.Single(lib.Pool("워로드", SlotGroup.각성기));
    }

    [Fact]
    public void An_unknown_or_damaged_class_has_no_skills()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "바드.json"), "{ not json");
        var lib = SkillLibrary.Load(dir);
        Assert.Empty(lib.Pool("바드", SlotGroup.일반스킬));
        Assert.Empty(lib.Pool("없는클래스", SlotGroup.일반스킬));
        Assert.Empty(SkillLibrary.Load(Path.Combine(dir, "missing")).Classes);
    }

    [Fact]
    public void An_icon_two_classes_use_is_shared()
    {
        var dir = TempDir();
        SkillLibrary.Save(dir, "a", [Skill("a", "x", SlotGroup.일반스킬, "c_skill/c_skill_01_1.png"), Skill("a", "y", SlotGroup.일반스킬, "a_skill/a_skill_01_2.png")]);
        SkillLibrary.Save(dir, "b", [Skill("b", "x", SlotGroup.일반스킬, "c_skill/c_skill_01_1.png")]);
        var lib = SkillLibrary.Load(dir);
        Assert.True(lib.Shared("c_skill/c_skill_01_1.png"));
        Assert.False(lib.Shared("a_skill/a_skill_01_2.png"));
    }

    [Fact]
    public void Entries_without_prints_or_a_name_are_left_out_and_the_rest_still_loads()
    {
        var dir = TempDir();
        SkillLibrary.Save(dir, "a", [Skill("a", "ok", SlotGroup.일반스킬, "a_skill/a_skill_01_1.png")]);
        File.WriteAllText(Path.Combine(dir, "b.json"), """{"class":"b","skills":[null,{"name":"x","kind":"일반스킬","icon":"b_skill/b_skill_01_1.png"},{"name":"y","kind":"일반스킬","icon":"b_skill/b_skill_01_2.png","prints":[]}]}""");
        File.WriteAllText(Path.Combine(dir, "c.json"), """{"class":"c","skills":[{"name":"x","kind":"없는종류","icon":"c","prints":["AA=="]}]}""");
        var lib = SkillLibrary.Load(dir);
        Assert.Empty(lib.Pool("b", SlotGroup.일반스킬));
        Assert.Empty(lib.Pool("c", SlotGroup.일반스킬));
        Assert.Single(lib.Pool("a", SlotGroup.일반스킬));
        Assert.False(lib.Shared("a_skill/a_skill_01_1.png"));   // the damaged classes don't break the others
    }

    [Fact]
    public void A_class_name_that_is_not_listed_never_becomes_a_path()
    {
        var dir = TempDir();
        var inner = Path.Combine(dir, "lib");
        SkillLibrary.Save(dir, "outside", [Skill("outside", "x", SlotGroup.일반스킬, "o_skill/o_skill_01_1.png")]);
        var lib = SkillLibrary.Load(inner);
        Assert.Empty(lib.Pool(Path.Combine("..", "outside"), SlotGroup.일반스킬));
        Assert.Empty(lib.Pool(Path.Combine(dir, "outside"), SlotGroup.일반스킬));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("2연 베기", "연베")]
    [InlineData("차지 스팅거", "차스")]
    [InlineData("가디언의 낙뢰", "가낙")]
    [InlineData("배쉬", "배쉬")]
    [InlineData("오라토리오", "오라")]
    [InlineData("AT02 유탄", "A유")]
    [InlineData("극의 : 체술", "극체")]
    public void A_chip_shows_the_first_syllables(string name, string chip) => Assert.Equal(chip, SkillLibrary.Short(name));

    /// <summary>Dev: writes Profiles/skills from <c>GAUGE_SKILL_SRC</c> (reference/skill-icons: api/classes/*.json from the
    /// Open API, cdn/&lt;atlas&gt;/*.bin icons, subclass/&lt;클래스&gt;/&lt;직업각인&gt;.bin from Profiles/subclass). A 초각스 gets the
    /// 직업각인 whose icon picks it (IconPrint.Best over the class's 초각스).</summary>
    [Fact]
    public void Build_skill_library_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_SKILL_SRC") is not { Length: > 0 } src) return;
        var kinds = new Dictionary<string, SlotGroup> { ["normal"] = SlotGroup.일반스킬, ["hyper_awakening_technique"] = SlotGroup.초각스,
            ["awakening"] = SlotGroup.각성기, ["hyper_awakening"] = SlotGroup.각성기 };
        var dest = Path.Combine(Helpers.RepoRoot(), "Profiles", "skills");
        foreach (var file in Directory.GetFiles(Path.Combine(src, "api", "classes"), "*.json"))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            string cls = doc.RootElement.GetProperty("class").GetString()!;
            var skills = doc.RootElement.GetProperty("skills").EnumerateArray()
                .Select(s => (Name: s.GetProperty("name").GetString()!, Type: s.GetProperty("type").GetString()!, Icon: s.GetProperty("icon").GetString()!))
                .Where(s => kinds.ContainsKey(s.Type)).DistinctBy(s => (s.Name, s.Icon))
                .Select(s => new LibrarySkill(cls, s.Name, kinds[s.Type], s.Icon, null,
                    IconPrint.References(Image.FromBytes(File.ReadAllBytes(Path.Combine(src, "cdn", s.Icon[..^4] + ".bin")))!))).ToList();
            var subDir = Path.Combine(src, "subclass", cls);
            if (Directory.Exists(subDir))
                foreach (var sub in Directory.GetFiles(subDir, "*.bin"))
                {
                    var pool = skills.Where(s => s.Kind == SlotGroup.초각스).SelectMany(s => s.Prints.Select(p => (s.Name, p)));
                    if (IconPrint.Best(IconPrint.Framings(Image.FromBytes(File.ReadAllBytes(sub))!), pool) is { } name)
                        skills = [.. skills.Select(s => s.Name == name ? s with { Subclass = Path.GetFileNameWithoutExtension(sub) } : s)];
                }
            SkillLibrary.Save(dest, cls, skills);
        }
    }

    [Fact]
    public void The_shipped_library_has_every_class_with_all_three_kinds()
    {
        var lib = SkillLibrary.Load(Path.Combine(Helpers.RepoRoot(), "Profiles", "skills"));
        if (lib.Classes.Count == 0) return;   // not generated yet
        Assert.True(lib.Classes.Count >= 30, $"{lib.Classes.Count} classes");
        foreach (var cls in lib.Classes)
            foreach (var kind in new[] { SlotGroup.일반스킬, SlotGroup.초각스, SlotGroup.각성기 })
                Assert.True(lib.Pool(cls, kind).Count > 0, $"{cls} {kind}");
    }
}
