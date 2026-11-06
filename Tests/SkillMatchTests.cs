using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class SkillMatchTests
{
    /// <summary>A made-up icon with structure (several small blobs), the same for one seed at any size.</summary>
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

    static LibrarySkill Skill(string cls, int seed, SlotGroup kind = SlotGroup.일반스킬, string? icon = null) =>
        new(cls, $"{cls}{seed}", kind, icon ?? $"{cls}_skill/{cls}_skill_01_{seed}.png", null, IconPrint.References(Art(seed)));

    static IReadOnlyList<IconPrint> Capture(int seed) => IconPrint.Framings(Art(seed, 50));

    [Fact]
    public void A_capture_is_identified_in_its_pool_with_the_runners_up()
    {
        var pool = new[] { Skill("a", 1), Skill("a", 2), Skill("a", 3), Skill("a", 4) };
        var r = SkillMatch.Identify(Capture(3), pool);
        Assert.Equal("a3", r.Skill?.Name);
        Assert.Equal(3, r.Top.Count);
        Assert.Equal("a3", r.Top[0].Name);
        Assert.True(r.Score > 0.9);
    }

    [Fact]
    public void A_capture_of_another_skill_stays_unknown_but_keeps_candidates()
    {
        var r = SkillMatch.Identify(Capture(40), [Skill("a", 1), Skill("a", 2)]);
        Assert.Null(r.Skill);
        Assert.Equal(2, r.Top.Count);
    }

    [Fact]
    public void An_empty_pool_identifies_nothing() => Assert.Null(SkillMatch.Identify(Capture(1), []).Skill);

    [Fact]
    public void A_crop_too_small_or_a_skill_without_prints_identifies_nothing()
    {
        Assert.Null(SkillMatch.Identify([], [Skill("a", 1)]).Skill);
        Assert.Null(SkillMatch.Identify(Capture(1), [new LibrarySkill("a", "a1", SlotGroup.일반스킬, "a", null, [])]).Skill);
    }

    [Fact]
    public void Two_classes_with_equal_votes_propose_none()
    {
        var lib = Library(("a", [.. Enumerable.Range(1, 8).Select(i => Skill("a", i))]), ("b", [.. Enumerable.Range(11, 8).Select(i => Skill("b", i))]));
        Assert.Null(SkillMatch.ClassVote([.. new[] { 1, 2, 3, 4, 11, 12, 13, 14 }.Select(i => (SlotGroup.일반스킬, Capture(i)))], lib));
        Assert.Empty(SkillMatch.Votes([(SlotGroup.일반스킬, (IReadOnlyList<IconPrint>)[])], lib));
    }

    static SkillLibrary Library(params (string Cls, LibrarySkill[] Skills)[] classes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "skillvote-" + Guid.NewGuid().ToString("N"));
        foreach (var (cls, skills) in classes) SkillLibrary.Save(dir, cls, skills);
        return SkillLibrary.Load(dir);
    }

    [Fact]
    public void The_class_most_slots_agree_on_wins_the_vote()
    {
        var lib = Library(("a", [.. Enumerable.Range(1, 8).Select(i => Skill("a", i))]), ("b", [.. Enumerable.Range(11, 8).Select(i => Skill("b", i))]));
        var slots = Enumerable.Range(1, 6).Select(i => (SlotGroup.일반스킬, Capture(i))).ToList();
        Assert.Equal("a", SkillMatch.ClassVote(slots, lib));
    }

    [Fact]
    public void Too_few_votes_or_a_split_proposes_no_class()
    {
        var lib = Library(("a", [.. Enumerable.Range(1, 8).Select(i => Skill("a", i))]), ("b", [.. Enumerable.Range(11, 8).Select(i => Skill("b", i))]));
        Assert.Null(SkillMatch.ClassVote([(SlotGroup.일반스킬, Capture(1))], lib));
        Assert.Null(SkillMatch.ClassVote([.. new[] { 1, 2, 3, 11, 12, 13 }.Select(i => (SlotGroup.일반스킬, Capture(i)))], lib));
    }

    [Fact]
    public void An_icon_two_classes_share_casts_no_vote()
    {
        var shared = "x_skill/x_skill_01_1.png";
        var lib = Library(("a", [Skill("a", 1, icon: shared), Skill("a", 2), Skill("a", 3), Skill("a", 4), Skill("a", 5)]),
            ("b", [Skill("b", 1, icon: shared), Skill("b", 12)]));
        var votes = SkillMatch.Votes([.. new[] { 1, 2, 3, 4, 5 }.Select(i => (SlotGroup.일반스킬, Capture(i)))], lib);
        Assert.Equal(4, votes.GetValueOrDefault("a"));
        Assert.Equal(0, votes.GetValueOrDefault("b"));
    }
}
