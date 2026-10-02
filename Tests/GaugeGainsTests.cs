using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>What each skill's cast adds to the gauge, learned from the rise after it (GaugeGains).</summary>
public class GaugeGainsTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    /// <summary>Casts <paramref name="slots"/> at <paramref name="at"/> on a gauge at <paramref name="from"/>, then reads it
    /// <paramref name="gain"/> higher every 100 ms until the window has closed. Returns the time after.</summary>
    static double Round(GaugeGains g, double at, double from, double gain, params SlotId[] slots)
    {
        foreach (var s in slots) g.Cast(s, S(at), from);
        for (double t = at + 0.1; t < at + 5; t += 0.1) g.Seen(from + gain, S(t));
        return at + 5;
    }

    [Fact]
    public void A_skill_cast_alone_learns_its_gain()
    {
        var g = new GaugeGains();
        double t = 0;
        for (int i = 0; i < 12; i++) t = Round(g, t, 0.2, 0.1, SlotId.일반1);
        Assert.Equal(0.1, g.Expected(SlotId.일반1)!.Value, 0.01);
    }

    [Fact]
    public void A_few_casts_predict_nothing()
    {
        var g = new GaugeGains();
        double t = 0;
        for (int i = 0; i < 3; i++) t = Round(g, t, 0.2, 0.1, SlotId.일반1);
        Assert.Null(g.Expected(SlotId.일반1));
    }

    /// <summary>A skill swapped into the slot: the learned value moves to the new one within about 30 uses (the settings
    /// say so beside 배운 값 지우기).</summary>
    [Fact]
    public void A_changed_skill_is_learned_again_within_30_uses()
    {
        var g = new GaugeGains();
        double t = 0;
        for (int i = 0; i < 40; i++) t = Round(g, t, 0.2, 0.1, SlotId.일반1);
        for (int i = 0; i < 30; i++) t = Round(g, t, 0.2, 0.2, SlotId.일반1);
        Assert.Equal(0.2, g.Expected(SlotId.일반1)!.Value, 0.01);
    }

    /// <summary>일반1 alone adds 0.08 and 일반1 with 일반2 0.20: 일반2's share is told apart as 0.12.</summary>
    [Fact]
    public void Skills_cast_together_are_told_apart()
    {
        var g = new GaugeGains();
        double t = 0;
        for (int i = 0; i < 60; i++)
            t = i % 2 == 0 ? Round(g, t, 0.2, 0.08, SlotId.일반1) : Round(g, t, 0.2, 0.20, SlotId.일반1, SlotId.일반2);
        Assert.Equal(0.08, g.Expected(SlotId.일반1)!.Value, 0.02);
        Assert.Equal(0.12, g.Expected(SlotId.일반2)!.Value, 0.02);
    }

    /// <summary>A full gauge cuts the rise off, and a drop is a spend: neither is a skill's gain.</summary>
    [Fact]
    public void A_full_gauge_or_a_spend_teaches_nothing()
    {
        var g = new GaugeGains();
        double t = 0;
        for (int i = 0; i < 12; i++) t = Round(g, t, 0.95, 0.05, SlotId.일반1);
        for (int i = 0; i < 12; i++) t = Round(g, t, 0.9, -0.5, SlotId.일반1);
        Assert.Null(g.Expected(SlotId.일반1));
    }

    /// <summary>What was learned is kept: a new one from the saved gains predicts the same, and a key's gains stay
    /// apart from another's (GaugeGainsStore, gains.json).</summary>
    [Fact]
    public void Learned_gains_survive_a_save_and_a_restart()
    {
        var g = new GaugeGains();
        double t = 0;
        for (int i = 0; i < 12; i++) t = Round(g, t, 0.2, 0.1, SlotId.일반1);
        var path = Path.Combine(Path.GetTempPath(), $"gains_{Guid.NewGuid():N}.json");
        try
        {
            var store = new GaugeGainsStore(path);
            store.Save(new Dictionary<string, IReadOnlyDictionary<string, Gain>> { ["발키리 · 해방자"] = g.Export() });
            var loaded = new GaugeGainsStore(path).Load();
            var back = new GaugeGains(loaded["발키리 · 해방자"]);
            Assert.Equal(g.Expected(SlotId.일반1)!.Value, back.Expected(SlotId.일반1)!.Value, 1e-9);
            Assert.Null(back.Expected(SlotId.일반2));
            Assert.False(loaded.ContainsKey("바드 · 절실한구원"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_damaged_file_loads_as_nothing_learned()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gains_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Empty(new GaugeGainsStore(path).Load());
            Assert.Empty(new GaugeGainsStore(path + ".missing").Load());
        }
        finally { File.Delete(path); }
    }
}
