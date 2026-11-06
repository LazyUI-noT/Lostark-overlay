using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class FightSkillsTests
{
    static Image Art(int seed, int size = 64)
    {
        var rng = new Random(seed);
        var blobs = Enumerable.Range(0, 9).Select(_ => (X: rng.NextDouble(), Y: rng.NextDouble(), R: 0.08 + rng.NextDouble() * 0.15,
            B: rng.Next(256), G: rng.Next(256), Rd: rng.Next(256))).ToArray();
        var img = new Image(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double b = 20, g = 20, r = 20, u = (x + 0.5) / size, v = (y + 0.5) / size;
                foreach (var o in blobs)
                {
                    double w = Math.Exp(-((u - o.X) * (u - o.X) + (v - o.Y) * (v - o.Y)) / (o.R * o.R));
                    b += o.B * w; g += o.G * w; r += o.Rd * w;
                }
                int i = y * img.Stride + x * 4;
                (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]) = ((byte)Math.Min(255, b), (byte)Math.Min(255, g), (byte)Math.Min(255, r), 255);
            }
        return img;
    }

    static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);
    static IReadOnlyList<IconPrint> Capture(int seed) => IconPrint.Framings(Art(seed, 50));

    /// <summary>Class "a": 일반 seeds 1–8, 초각스 21 (직업각인 "x") and 22, 각성기 31; class "b": 일반 41–48, 초각스 51, 각성기 61.</summary>
    static SkillLibrary Library()
    {
        LibrarySkill S(string cls, int seed, SlotGroup kind, string? sub = null) =>
            new(cls, $"{cls}{seed}", kind, $"{cls}_skill/{cls}_skill_01_{seed}.png", sub, IconPrint.References(Art(seed)));
        var dir = Path.Combine(Path.GetTempPath(), "fightskills-" + Guid.NewGuid().ToString("N"));
        SkillLibrary.Save(dir, "a", [.. Enumerable.Range(1, 8).Select(i => S("a", i, SlotGroup.일반스킬)),
            S("a", 21, SlotGroup.초각스, "x"), S("a", 22, SlotGroup.초각스, "y"), S("a", 31, SlotGroup.각성기)]);
        SkillLibrary.Save(dir, "b", [.. Enumerable.Range(41, 8).Select(i => S("b", i, SlotGroup.일반스킬)), S("b", 51, SlotGroup.초각스), S("b", 61, SlotGroup.각성기)]);
        return SkillLibrary.Load(dir);
    }

    static readonly SkillLibrary Lib = Library();

    static FightRecord Record() => new(DateTime.Today, null, 60, [], "me", 1, null, [], [], [], []);

    [Fact]
    public void Two_agreeing_reads_commit_a_slot()
    {
        var once = FightSkills.For("a").Add(SlotId.일반1, Capture(3), Lib, T0);
        Assert.Null(once.Skill(SlotId.일반1));
        var twice = once.Add(SlotId.일반1, Capture(3), Lib, T0 + TimeSpan.FromSeconds(1));
        Assert.Equal("a3", twice.Skill(SlotId.일반1)?.Name);
        Assert.Equal(SlotGroup.일반스킬, twice.Skill(SlotId.일반1)?.Kind);
    }

    [Fact]
    public void A_slot_matches_only_its_own_kind()
    {
        var s = FightSkills.For("a").Add(SlotId.초각스, Capture(3), Lib, T0).Add(SlotId.초각스, Capture(3), Lib, T0);
        Assert.Null(s.Skill(SlotId.초각스));   // a 일반 skill's art on the 초각스 slot is no 초각스
        Assert.Null(FightSkills.KindOf(SlotId.아덴1));
        Assert.False(FightSkills.For("a").Wanted(SlotId.아덴1, T0));
    }

    [Fact]
    public void A_different_read_starts_the_count_again_and_two_of_it_replace_the_skill()
    {
        var s = FightSkills.For("a").Add(SlotId.일반2, Capture(4), Lib, T0).Add(SlotId.일반2, Capture(4), Lib, T0);
        s = s.Add(SlotId.일반2, Capture(5), Lib, T0);
        Assert.Equal("a4", s.Skill(SlotId.일반2)?.Name);   // one other read: still the old skill
        s = s.Add(SlotId.일반2, Capture(5), Lib, T0);
        Assert.Equal("a5", s.Skill(SlotId.일반2)?.Name);   // swapped in town before the pull
    }

    [Fact]
    public void An_unknown_slot_is_watched_each_second_until_its_tries_run_out_and_keeps_candidates()
    {
        var s = FightSkills.For("a");
        Assert.True(s.Wanted(SlotId.일반3, T0));
        s = s.Add(SlotId.일반3, Capture(99), Lib, T0);
        Assert.False(s.Wanted(SlotId.일반3, T0 + TimeSpan.FromSeconds(0.5)));
        Assert.True(s.Wanted(SlotId.일반3, T0 + FightSkills.WatchInterval));
        for (int i = 1; i < FightSkills.MaxTries; i++) s = s.Add(SlotId.일반3, Capture(99), Lib, T0 + TimeSpan.FromSeconds(i));
        Assert.False(s.Wanted(SlotId.일반3, T0 + TimeSpan.FromHours(1)));
        var r = s.Apply(Record());
        Assert.Null(r.Skills);
        Assert.Equal(3, r.Unsure![SlotId.일반3].Count);
    }

    [Fact]
    public void A_committed_slot_is_checked_again_now_and_then()
    {
        var s = FightSkills.For("a").Add(SlotId.일반1, Capture(1), Lib, T0).Add(SlotId.일반1, Capture(1), Lib, T0);
        Assert.False(s.Wanted(SlotId.일반1, T0 + FightSkills.WatchInterval));
        Assert.True(s.Wanted(SlotId.일반1, T0 + FightSkills.RecheckInterval));
    }

    [Fact]
    public void Apply_writes_the_identified_skills_and_their_icons()
    {
        var s = FightSkills.For("a").Add(SlotId.각성기, Capture(31), Lib, T0).Add(SlotId.각성기, Capture(31), Lib, T0);
        var r = s.Apply(Record());
        var skill = r.Skills![SlotId.각성기];
        Assert.Equal(("a31", "a_skill/a_skill_01_31.png", SlotGroup.각성기), (skill.Name, skill.Icon, skill.Kind));
        Assert.Null(r.ProposedClass);
        Assert.Null(FightSkills.For("a").Apply(Record()).Skills);
    }

    [Fact]
    public void A_fight_of_unknown_class_votes_then_identifies_in_the_winner()
    {
        var s = FightSkills.For(null);
        for (int i = 1; i <= 5; i++) s = s.Add(SlotId.일반1 + (i - 1), Capture(i), Lib, T0);
        Assert.Equal("a", s.Class);
        s = s.Add(SlotId.초각스, Capture(21), Lib, T0).Add(SlotId.초각스, Capture(21), Lib, T0);
        for (int i = 1; i <= 5; i++) s = s.Add(SlotId.일반1 + (i - 1), Capture(i), Lib, T0);
        var r = s.Apply(Record());
        Assert.Equal("a · x", r.ProposedClass);   // the 초각스 names its 직업각인
        Assert.Equal(5, r.Skills!.Count(k => k.Value.Kind == SlotGroup.일반스킬));
    }

    static readonly RecordSkill Q = new("a1", "a_skill/a_skill_01_1.png", SlotGroup.일반스킬), W = new("a2", "a_skill/a_skill_01_2.png", SlotGroup.일반스킬),
        W2 = new("a3", "a_skill/a_skill_01_3.png", SlotGroup.일반스킬);

    static FightRecord Proposed() => Record() with
    {
        MyClassName = RecordCharacter.TestClass, ProposedClass = "a · x",
        Skills = new Dictionary<SlotId, RecordSkill> { [SlotId.일반1] = Q },
        Unsure = new Dictionary<SlotId, IReadOnlyList<RecordSkill>> { [SlotId.일반2] = [W, W2] },
    };

    [Fact]
    public void Confirming_the_proposed_class_keeps_the_skills_and_a_pick_settles_an_unsure_slot()
    {
        var r = Proposed();
        var done = FightSkills.Confirm(r, r with { MyClassName = "a" }, new Dictionary<SlotId, RecordSkill> { [SlotId.일반2] = W2 });
        Assert.Equal(Q, done.Skills![SlotId.일반1]);
        Assert.Equal(W2, done.Skills[SlotId.일반2]);
        Assert.Null(done.Unsure);
        Assert.Null(done.ProposedClass);   // confirmed
    }

    [Fact]
    public void Another_class_than_proposed_drops_the_skills_found_in_the_proposal()
    {
        var r = Proposed();
        var done = FightSkills.Confirm(r, r with { MyClassName = "b" }, new Dictionary<SlotId, RecordSkill>());
        Assert.Null(done.Skills);
        Assert.Null(done.Unsure);
        Assert.Null(done.ProposedClass);
    }

    [Fact]
    public void Without_a_class_set_the_proposal_stays_and_unpicked_slots_stay_unsure()
    {
        var r = Proposed();
        var done = FightSkills.Confirm(r, r, new Dictionary<SlotId, RecordSkill>());
        Assert.Equal("a · x", done.ProposedClass);
        Assert.Equal(2, done.Unsure![SlotId.일반2].Count);
        Assert.Equal(Q, done.Skills![SlotId.일반1]);
    }

    [Fact]
    public void A_match_that_failed_waits_like_a_try_instead_of_asking_every_frame()
    {
        var s = FightSkills.For("a").Failed(SlotId.일반1, T0);
        Assert.False(s.Wanted(SlotId.일반1, T0 + TimeSpan.FromSeconds(0.1)));
        Assert.True(s.Wanted(SlotId.일반1, T0 + FightSkills.WatchInterval));
        Assert.Same(s, s.Failed(SlotId.아덴1, T0));
    }

    [Fact]
    public void The_map_follows_the_class_played()
    {
        var a = FightSkills.For("a");
        Assert.Same(a, a.Current("a"));
        Assert.Equal("b", a.Current("b").Class);
        Assert.Null(a.Current(null).Class);   // 테스트 모드: a vote again
        var voted = FightSkills.For(null);
        for (int i = 1; i <= 5; i++) voted = voted.Add(SlotId.일반1 + (i - 1), Capture(i), Lib, T0);
        Assert.Same(voted, voted.Current(null));                      // a [Test] fight keeps the class it voted
        Assert.Empty(voted.VoteCrops);                                // its crops are let go once the class is known
        Assert.False(voted.Current("a").Voted);                       // a real character's fight proposes nothing
    }

    [Fact]
    public void Confirm_leaves_a_fight_that_is_not_test_as_it_was()
    {
        var r = Proposed() with { MyClassName = "a" };
        var done = FightSkills.Confirm(r, r with { MyClassName = "b", Difficulty = "하드" }, new Dictionary<SlotId, RecordSkill>());
        Assert.Equal(Q, done.Skills![SlotId.일반1]);
    }

    [Fact]
    public void An_unknown_class_without_a_winner_proposes_nothing_and_names_nothing()
    {
        var s = FightSkills.For(null).Add(SlotId.일반1, Capture(1), Lib, T0).Add(SlotId.일반2, Capture(41), Lib, T0);
        var r = s.Apply(Record());
        Assert.Null(s.Class);
        Assert.Null(r.ProposedClass);
        Assert.Null(r.Skills);
    }
}
