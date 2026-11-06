using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>클래스 인식 (class-detect.plan.md): the class from a ready 초각스 against the shipped library, what is done with it, and the
/// mode and its migration from 로딩 뒤 직업각인 자동 감지.</summary>
public class ClassDetectTests
{
    static readonly SkillLibrary Lib = SkillLibrary.Load(Path.Combine(Helpers.RepoRoot(), "Profiles", "skills"));
    static IReadOnlyList<IconPrint> Slot(string fixture) => IconPrint.Framings(Fixture.Load(fixture).Frames[0]);
    static SkillClassFinder.Found Sure(string cls) => new(cls, null, false, 0.9, 0.5);
    static readonly SkillClassFinder.Found Nothing = new(null, null, false, 0.5, 0.01);

    [Fact]
    public void A_ready_초각스_in_colour_tells_its_class_and_직업각인_with_no_last_class()
    {
        var found = SkillClassFinder.Find(Slot("Nocd_초각스"), Lib);

        Assert.Equal(("워로드", "고독한기사", false), (found.Class, found.Subclass, found.Fast));
    }

    [Fact]
    public void The_last_class_is_enough_when_the_초각스_is_its_own()
    {
        var found = SkillClassFinder.Find(Slot("Nocd_초각스"), Lib, last: "워로드");

        Assert.Equal(("워로드", true), (found.Class, found.Fast));
    }

    [Fact]
    public void Another_last_class_falls_through_to_the_search()
    {
        var found = SkillClassFinder.Find(Slot("Nocd_초각스"), Lib, last: "소서리스");

        Assert.Equal(("워로드", false), (found.Class, found.Fast));
    }

    [Fact]
    public void A_greyed_초각스_tells_its_class_by_brightness()
    {
        var found = SkillClassFinder.Find(Slot("greyed_초각스"), Lib, greyed: true);

        Assert.Equal("워로드", found.Class);
    }

    [Fact]
    public void A_greyed_초각스_read_in_colour_tells_nothing()
    {
        Assert.Null(SkillClassFinder.Find(Slot("greyed_초각스"), Lib).Class);
    }

    [Theory]
    [InlineData("Nocd_일반1")]
    [InlineData("Nocd_일반2")]
    [InlineData("lt2_11_초각스")]   // on cooldown: digits over the art, the nearest thing to a wrong pick the sweep found
    [InlineData("cd_초각스")]
    public void Another_slot_read_as_a_초각스_finds_no_class(string fixture)
    {
        Assert.Null(SkillClassFinder.Find(Slot(fixture), Lib).Class);
    }

    [Fact]
    public void No_framings_find_no_class() => Assert.Null(SkillClassFinder.Find([], Lib).Class);

    [Fact]
    public void Its_own_일반_slots_confirm_a_class_and_contradict_another()
    {
        var ordinary = Enumerable.Range(1, 8).Select(i => Slot($"Nocd_일반{i}")).ToList();

        Assert.Equal(SkillClassFinder.Verdict.Confirmed, SkillClassFinder.Confirm(ordinary, Lib, "워로드"));
        Assert.Equal(SkillClassFinder.Verdict.Contradicted, SkillClassFinder.Confirm(ordinary, Lib, "소서리스"));
    }

    [Fact]
    public void No_일반_slots_can_not_confirm() => Assert.Equal(SkillClassFinder.Verdict.Unsure, SkillClassFinder.Confirm([], Lib, "워로드"));

    [Theory]
    [InlineData(ClassDetectMode.자동, false, false, false, true)]
    [InlineData(ClassDetectMode.수동, false, false, false, false)]
    [InlineData(ClassDetectMode.끄기, false, false, false, false)]
    [InlineData(ClassDetectMode.자동, true, false, false, false)]
    [InlineData(ClassDetectMode.자동, false, true, false, false)]
    [InlineData(ClassDetectMode.자동, false, true, true, true)]
    public void The_bar_coming_back_reads_the_class_only_in_자동_outside_a_dungeon(ClassDetectMode mode, bool inDungeon, bool test, bool dev, bool reads)
    {
        Assert.Equal(reads, ClassDetect.OnBarBack(mode, inDungeon, test, dev));
    }

    [Fact]
    public void Another_class_confirmed_by_its_slots_is_switched_to() =>
        Assert.Equal(new ClassPick("워로드", null), ClassDetect.Switch(Sure("워로드"), SkillClassFinder.Verdict.Confirmed, "바드", greyed: false));

    [Theory]
    [InlineData(SkillClassFinder.Verdict.Unsure)]
    [InlineData(SkillClassFinder.Verdict.Contradicted)]
    public void Another_class_its_slots_do_not_confirm_keeps_the_class(SkillClassFinder.Verdict slots) =>
        Assert.Null(ClassDetect.Switch(Sure("워로드"), slots, "바드", greyed: false));

    [Fact]
    public void A_greyed_bar_needs_no_confirmation() =>
        Assert.NotNull(ClassDetect.Switch(Sure("워로드"), SkillClassFinder.Verdict.Unsure, "바드", greyed: true));

    [Fact]
    public void The_same_class_is_no_switch() =>
        Assert.Null(ClassDetect.Switch(Sure("워로드"), SkillClassFinder.Verdict.Confirmed, "워로드", greyed: false));

    [Fact]
    public void Nothing_found_keeps_the_class_and_is_worth_a_retry()
    {
        var pick = ClassDetect.Switch(Nothing, SkillClassFinder.Verdict.Confirmed, "바드", greyed: false);

        Assert.Null(pick);
        Assert.True(ClassDetect.Unsure(Nothing, pick, "바드"));
    }

    [Fact]
    public void The_same_class_or_a_switch_needs_no_retry()
    {
        Assert.False(ClassDetect.Unsure(Sure("바드"), null, "바드"));
        Assert.False(ClassDetect.Unsure(Sure("워로드"), new("워로드", null), "바드"));
    }

    [Fact]
    public void Another_class_left_unconfirmed_is_worth_a_retry() => Assert.True(ClassDetect.Unsure(Sure("워로드"), null, "바드"));

    [Fact]
    public void The_old_로딩_뒤_switch_becomes_자동_once()
    {
        var migrated = SettingsStore.Normalize(new Settings { AutoSubclass = true });

        Assert.Equal((ClassDetectMode.자동, false), (migrated.ClassDetect, migrated.AutoSubclass));
    }

    [Fact]
    public void Everyone_else_starts_on_수동() => Assert.Equal(ClassDetectMode.수동, SettingsStore.Normalize(new Settings()).ClassDetect);

    [Fact]
    public void The_map_box_reads_and_sets_the_mode()
    {
        var s = new Settings();

        Assert.Equal(1, FeatureMap.Selected(s, "cls", 0));
        FeatureMap.Choose(s, "cls", 0, 2);
        Assert.Equal(ClassDetectMode.자동, s.ClassDetect);
    }
}
