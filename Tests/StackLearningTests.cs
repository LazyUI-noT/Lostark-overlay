using System.Text.Json;
using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>Which slots are stack skills, learned per 직업각인 and remembered (user, 2026-10-13): a stack can come from the
/// skill, a tripod or a core, so it isn't class data; the screen shows it. Known before a cast, the slot's "1s" lockout
/// is never taken for a 1 s cooldown (CooldownTracker), from the first cast after a restart or a loading screen.</summary>
public class StackLearningTests
{
    // ── 스킬창 맞추기: a ready slot showing its count ──────────────────────────────────────────────────────────────

    // Every ready frame in the fixtures (2,376 on 2026-10-13): only live test / live_test2's Q, a 2-use stack skill, shows a
    // count. A count on any other ready icon would mark an ordinary skill as a stack skill at 스킬창 맞추기.
    static readonly HashSet<string> StackQ = ["gold_lt_1_일반1", "lt_count2_일반1", "lt_lockout_일반1", "lt2_clear_일반1", "lt2_count2_일반1"];

    [Fact]
    public void No_ready_icon_in_the_fixtures_shows_a_count_but_the_stack_skill_Q()
    {
        var counted = new List<string>();
        foreach (var json in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(json);
            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            if (!doc.RootElement.TryGetProperty("slot", out var s) || !Enum.TryParse<SlotId>(s.GetString(), out var slot)
                || !doc.RootElement.TryGetProperty("hud", out var h)) continue;
            var digits = new DigitReader(h.GetDouble() / 110.0);
            if (Fixture.Load(name).Frames.Any(img => SlotClassifier.Read(slot, img, TimeSpan.Zero, null, digits).Kind == ReadKind.Ready && digits.ShowsCount(img)))
                counted.Add(name);
        }
        Assert.Subset(StackQ, counted.ToHashSet());
        Assert.NotEmpty(counted);   // and Q's is seen
    }

    // ── During play: the count beside a timer on separate casts ────────────────────────────────────────────────

    static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);
    static TimeSpan At(double s) => T0 + TimeSpan.FromSeconds(s);
    static Reading Count(SlotId slot, double t, bool stacked = true) => new(slot, ReadKind.Seconds, 8, At(t), Stacked: stacked);
    static SlotState CastAt(SlotId slot, double t) => new(slot, SlotPhase.Cooldown, At(t + 9), null, false, At(t));

    [Fact]
    public void A_slot_is_learned_on_its_third_cast_with_the_count_seen()
    {
        var learner = new StackLearner();
        Assert.False(learner.Add(Count(SlotId.일반3, 1), CastAt(SlotId.일반3, 0)));
        Assert.False(learner.Add(Count(SlotId.일반3, 1.1), CastAt(SlotId.일반3, 0)));   // the same cast, read again
        Assert.False(learner.Add(Count(SlotId.일반4, 2), CastAt(SlotId.일반4, 1)));     // another slot
        Assert.False(learner.Add(Count(SlotId.일반3, 21), CastAt(SlotId.일반3, 20)));
        Assert.True(learner.Add(Count(SlotId.일반3, 41), CastAt(SlotId.일반3, 40)));
        Assert.False(learner.Add(Count(SlotId.일반3, 61), CastAt(SlotId.일반3, 60)));    // told once
    }

    // Through the tracker, as the engine runs it: an unknown slot's "1s" is first taken for a 1 s cooldown and restarted
    // when the recharge timer follows, so one cast has two states with the count beside them.
    [Fact]
    public void Through_the_tracker_one_cast_counts_once()
    {
        var (learner, state) = (new StackLearner(), SlotState.Unknown(SlotId.일반3));
        int learnedAt = 0;
        for (int cast = 1; cast <= 4; cast++)
        {
            double b = cast * 20;
            Reading[] reads =
            [
                new(SlotId.일반3, ReadKind.Ready, 0, At(b - 1)),
                new(SlotId.일반3, ReadKind.Seconds, 1, At(b)),
                new(SlotId.일반3, ReadKind.Seconds, 1, At(b + 0.15), Stacked: true),
                .. Enumerable.Range(0, 10).Select(i => new Reading(SlotId.일반3, ReadKind.Seconds, 8, At(b + 1.1 + i * 0.05), Stacked: true)),
                new(SlotId.일반3, ReadKind.Seconds, 7, At(b + 2.1), Stacked: true),
            ];
            foreach (var r in reads)
            {
                state = CooldownTracker.Update(state, r);
                if (learner.Add(r, state)) learnedAt = cast;
            }
        }
        Assert.Equal(StackLearner.Casts, learnedAt);
    }

    [Fact]
    public void Reads_without_the_count_never_teach()
    {
        var learner = new StackLearner();
        for (int i = 0; i < 5; i++)
            Assert.False(learner.Add(Count(SlotId.일반3, i * 20 + 1, stacked: false), CastAt(SlotId.일반3, i * 20)));
    }

    [Fact]
    public void Another_직업각인_starts_over()
    {
        var learner = new StackLearner();
        learner.Add(Count(SlotId.일반3, 1), CastAt(SlotId.일반3, 0));
        learner.Add(Count(SlotId.일반3, 21), CastAt(SlotId.일반3, 20));
        learner.Reset();
        Assert.False(learner.Add(Count(SlotId.일반3, 41), CastAt(SlotId.일반3, 40)));
    }

    // ── Remembered per 직업각인 in 설정 ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Settings_remember_stack_skills_per_직업각인_once_each()
    {
        var s = new Settings();
        Assert.True(s.AddStackSkill("발키리 · 해방자", SlotId.일반3));
        Assert.False(s.AddStackSkill("발키리 · 해방자", SlotId.일반3));
        Assert.True(s.AddStackSkill("발키리 · 해방자", SlotId.일반1));
        Assert.Equal([SlotId.일반1, SlotId.일반3], s.StackSkillsOf("발키리 · 해방자").Order());
        Assert.Empty(s.StackSkillsOf("발키리 · 빛의기사"));
    }

    [Fact]
    public void Stack_skills_survive_a_restart_and_a_hand_edited_null_is_dropped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stack-{Guid.NewGuid():N}.json");
        try
        {
            var s = new Settings();
            s.AddStackSkill("발키리 · 해방자", SlotId.일반3);
            s.StackSkills["도화가 · 회귀"] = null!;
            s.StackSkills["워로드 · 고독한 기사"] = ["일반9", null!, "일반2", "3"];
            new SettingsStore(path).Save(s);
            var back = new SettingsStore(path).Load(out bool reset);
            Assert.False(reset);
            Assert.Equal([SlotId.일반3], back.StackSkillsOf("발키리 · 해방자"));
            Assert.False(back.StackSkills.ContainsKey("도화가 · 회귀"));
            Assert.Equal(["일반2"], back.StackSkills["워로드 · 고독한 기사"]);   // not a slot, null, a bare number: dropped
        }
        finally { File.Delete(path); }
    }
}
