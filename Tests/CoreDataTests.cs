using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Checks Profiles/cores stays consistent as effects are added: every file parses, belongs to a real class and 직업각인,
/// and asks only for cores that 직업각인's role can choose.</summary>
public class CoreDataTests
{
    static readonly string Profiles = Path.Combine(Helpers.RepoRoot(), "Profiles");
    static IReadOnlyList<ClassInfo> Classes => ProfileLoader.LoadClasses(Profiles);
    static string[] Files => Directory.GetFiles(Path.Combine(Profiles, "cores"), "*.json");

    [Fact]
    public void There_is_a_cores_folder_with_at_least_the_first_file() =>
        Assert.Contains(Files, f => Path.GetFileName(f) == "워로드.json");

    [Fact]
    public void Every_file_parses_and_belongs_to_a_class()
    {
        var names = Classes.Select(c => c.Name).ToHashSet();
        foreach (var file in Files)
        {
            Assert.Contains(Path.GetFileNameWithoutExtension(file), names);
            CoreEffects.Parse(File.ReadAllText(file));   // throws on a bad kind, slot, digit or watchSeconds
        }
    }

    [Fact]
    public void Every_subclass_exists_and_every_effect_asks_only_for_cores_its_role_can_choose()
    {
        foreach (var file in Files)
        {
            var cls = Classes.Single(c => c.Name == Path.GetFileNameWithoutExtension(file));
            foreach (var (sub, effects) in CoreEffects.Parse(File.ReadAllText(file)))
            {
                Assert.True(cls.Subclasses.ContainsKey(sub), $"{cls.Name} has no 직업각인 {sub}");
                var allowed = CoreEffects.Allowed(cls.RoleOf(sub));
                foreach (var e in effects)
                    Assert.All(e.Requires.Keys, core => Assert.True(allowed.Contains(core), $"{cls.Name} {sub}: {core} is not a core its role chooses"));
            }
        }
    }

    static readonly string[] Tags = ["CURRENT", "UNVERIFIED"];

    [Fact]
    public void Every_effect_has_a_known_tag_and_a_target_its_직업각인_can_show()
    {
        foreach (var file in Files)
        {
            var cls = Classes.Single(c => c.Name == Path.GetFileNameWithoutExtension(file));
            foreach (var (sub, effects) in CoreEffects.Parse(File.ReadAllText(file)))
                foreach (var e in effects)
                {
                    Assert.True(Tags.Contains(e.Tag), $"{cls.Name} {sub}: tag \"{e.Tag}\" must be CURRENT or UNVERIFIED");
                    if (e.Target is SlotId.아덴1 or SlotId.아덴2)
                        Assert.True(cls.AdenSlotsOf(sub).Contains(e.Target!.Value), $"{cls.Name} {sub}: {e.Target} is not one of its 아덴 slots");
                }
        }
    }

    /// <summary>Effects exist only for rows of the 지원하는 직업각인 table.</summary>
    [Fact]
    public void Every_subclass_with_effects_is_a_supported_row()
    {
        var rows = ClassSupport.Rows(Profiles, Classes, slot => slot.ToString());
        foreach (var file in Files)
            foreach (var sub in CoreEffects.Parse(File.ReadAllText(file)).Keys)
                Assert.Contains(rows, r => (r.Class, r.Subclass) == (Path.GetFileNameWithoutExtension(file), sub));
    }

    /// <summary>From the owner (2026-10-12): 워로드 고독한기사's 해 3 core resets 아덴2 (C) when 버스트 캐넌 is used.</summary>
    [Fact]
    public void The_worked_example_is_in_the_data()
    {
        var e = Assert.Single(CoreEffects.Load(Profiles, "워로드")["고독한기사"]);
        Assert.Equal([(CoreType.해, 3)], e.Requires.Select(r => (r.Key, r.Value)));
        Assert.Equal(("버스트 캐넌", CoreEffects.Reset, SlotId.아덴2, 1.0, "CURRENT"), (e.Trigger, e.Kind, e.Target, e.WatchSeconds, e.Tag));
        Assert.True(e.Tracks);
    }
}
