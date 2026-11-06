using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class SkillIconSourceTests
{
    [Theory]
    [InlineData("gl_skill/gl_skill_01_30.png", true)]
    [InlineData("ark_passive_dm/ark_passive_dm_5.png", true)]
    [InlineData("../gl_skill/x.png", false)]
    [InlineData("gl_skill/../../x.png", false)]
    [InlineData("gl_skill\\x.png", false)]
    [InlineData("/gl_skill/x.png", false)]
    [InlineData("C:/x/y.png", false)]
    [InlineData("http://evil/x.png", false)]
    [InlineData("GL_SKILL/X.PNG", false)]
    [InlineData("gl_skill/x.jpg", false)]
    [InlineData("gl_skill/a/b.png", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("gl_skill/x.png\n", false)]
    [InlineData("gl_skill/con.png", false)]
    [InlineData("gl_skill/com1.png", false)]
    [InlineData("nul/x.png", false)]
    [InlineData("gl_skill/console.png", true)]
    public void Only_a_plain_atlas_and_file_name_is_an_icon(string? icon, bool ok) => Assert.Equal(ok, SkillIconSource.IsValid(icon));

    [Fact]
    public void The_url_is_on_smilegates_cdn_and_the_file_stays_under_the_folder()
    {
        Assert.Equal("https://cdn-lostark.game.onstove.com/efui_iconatlas/gl_skill/gl_skill_01_30.png", SkillIconSource.Url("gl_skill/gl_skill_01_30.png").ToString());
        var root = Path.Combine(Path.GetTempPath(), "icons");
        Assert.Equal(Path.Combine(root, "gl_skill", "gl_skill_01_30.png"), SkillIconSource.LocalPath(root, "gl_skill/gl_skill_01_30.png"));
        Assert.Throws<ArgumentException>(() => SkillIconSource.LocalPath(root, "../x.png"));
        Assert.Throws<ArgumentException>(() => SkillIconSource.Url("../x.png"));
    }

    [Fact]
    public void Every_shipped_icon_is_valid()
    {
        var lib = SkillLibrary.Load(Path.Combine(Helpers.RepoRoot(), "Profiles", "skills"));
        foreach (var cls in lib.Classes)
            foreach (var kind in new[] { SlotGroup.일반스킬, SlotGroup.초각스, SlotGroup.각성기 })
                Assert.All(lib.Pool(cls, kind), s => Assert.True(SkillIconSource.IsValid(s.Icon), s.Icon));
    }

    static FightRecord R() => new(DateTime.Today, null, 60, [], "me", 1, null, [], [], [], []);

    [Fact]
    public void A_record_with_skills_survives_its_json_and_one_with_a_bad_icon_is_invalid()
    {
        var r = R() with
        {
            Skills = new Dictionary<SlotId, RecordSkill> { [SlotId.일반1] = new("차지 스팅거", "gl_skill/gl_skill_01_30.png", SlotGroup.일반스킬) },
            Unsure = new Dictionary<SlotId, IReadOnlyList<RecordSkill>> { [SlotId.초각스] = [new("실드 대시", "gl_skill/gl_skill_01_36.png", SlotGroup.초각스)] },
            ProposedClass = "워로드 · 전투태세",
        };
        var back = System.Text.Json.JsonSerializer.Deserialize<FightRecord>(System.Text.Json.JsonSerializer.Serialize(r, SettingsStore.Json), SettingsStore.Json)!;
        Assert.Equal("차지 스팅거", back.Skills![SlotId.일반1].Name);
        Assert.Equal("실드 대시", back.Unsure![SlotId.초각스][0].Name);
        Assert.Equal("워로드 · 전투태세", back.ProposedClass);
        Assert.True(RecordStore.IsValid(back));
        Assert.True(RecordStore.IsValid(R()));   // a record from before 0.19

        Assert.False(RecordStore.IsValid(r with { Skills = new Dictionary<SlotId, RecordSkill> { [SlotId.일반1] = new("x", "../x.png", SlotGroup.일반스킬) } }));
        Assert.False(RecordStore.IsValid(r with { Skills = new Dictionary<SlotId, RecordSkill> { [SlotId.일반1] = null! } }));
        Assert.False(RecordStore.IsValid(r with { Skills = new Dictionary<SlotId, RecordSkill> { [(SlotId)999] = new("x", "gl_skill/x.png", SlotGroup.일반스킬) } }));
        Assert.False(RecordStore.IsValid(r with { Unsure = new Dictionary<SlotId, IReadOnlyList<RecordSkill>> { [SlotId.일반1] = [new("x", "gl_skill/x.png", (SlotGroup)77)] } }));
    }

    [Fact]
    public void An_import_caps_the_skills_it_carries()
    {
        var many = Enumerable.Range(0, 4).Select(i => new RecordSkill($"s{i}", "gl_skill/x.png", SlotGroup.일반스킬)).ToList();
        Assert.True(RecordExport.Fits(R() with { Unsure = new Dictionary<SlotId, IReadOnlyList<RecordSkill>> { [SlotId.일반1] = many[..3] } }));
        Assert.False(RecordExport.Fits(R() with { Unsure = new Dictionary<SlotId, IReadOnlyList<RecordSkill>> { [SlotId.일반1] = many } }));
        Assert.False(RecordExport.Fits(R() with { Skills = new Dictionary<SlotId, RecordSkill> { [SlotId.일반1] = new(new string('a', 65), "gl_skill/x.png", SlotGroup.일반스킬) } }));
        Assert.False(RecordExport.Fits(R() with { ProposedClass = new string('a', 65) }));
        var r = RecordExport.Anonymous(R() with { Skills = new Dictionary<SlotId, RecordSkill> { [SlotId.일반1] = many[0] } });
        Assert.Equal("s0", r.Skills![SlotId.일반1].Name);   // shared with the fight
    }
}
