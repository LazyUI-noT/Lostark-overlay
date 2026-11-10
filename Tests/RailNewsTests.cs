using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The rail's yellow orbs (settings-reorg.plan.md, order 14): a tab lights when something under it is new since it was last opened.</summary>
public class RailNewsTests
{
    static Dictionary<string, List<string>> Tabs(params (string Tab, string[] Keys)[] tabs) => tabs.ToDictionary(t => t.Tab, t => t.Keys.ToList());

    [Fact]
    public void Nothing_is_lit_before_the_first_recording() =>
        Assert.Empty(RailNews.Lit(Tabs(("레이드", ["a"])), seen: null));

    [Fact]
    public void A_tab_lights_for_a_key_it_has_not_seen() =>
        Assert.Contains("레이드", RailNews.Lit(Tabs(("레이드", ["a", "b"])), Tabs(("레이드", ["a"]))));

    [Fact]
    public void A_tab_stays_dark_when_every_key_was_seen() =>
        Assert.Empty(RailNews.Lit(Tabs(("레이드", ["a"])), Tabs(("레이드", ["b", "a"]))));

    [Fact]
    public void Only_the_tab_with_the_new_key_lights()
    {
        var lit = RailNews.Lit(Tabs(("레이드", ["a"]), ("파티", ["p", "q"])), Tabs(("레이드", ["a"]), ("파티", ["p"])));
        Assert.Equal(["파티"], lit.ToList());
    }

    [Fact]
    public void A_tab_never_recorded_lights_for_its_keys() =>
        Assert.Contains("원정대 (레이드 선택)", RailNews.Lit(Tabs(("원정대 (레이드 선택)", ["ann"])), Tabs(("레이드", ["a"]))));

    [Fact]
    public void Keys_that_went_away_light_nothing() =>
        Assert.Empty(RailNews.Lit(Tabs(("레이드", ["a"])), Tabs(("레이드", ["a", "gone"]))));

    [Fact]
    public void Opening_a_tab_clears_it_and_keeps_the_others_lit()
    {
        var now = Tabs(("레이드", ["a"]), ("파티", ["p"]));
        var seen = RailNews.Opened(now, Tabs(("파티", [])), "레이드");
        Assert.Equal(["파티"], RailNews.Lit(now, seen).ToList());
    }

    [Fact]
    public void A_first_recording_marks_every_tab_seen() =>
        Assert.Empty(RailNews.Lit(Tabs(("레이드", ["a"]), ("파티", ["p"])), RailNews.Baseline(Tabs(("레이드", ["a"]), ("파티", ["p"])))));

    [Fact]
    public void Opening_a_tab_with_nothing_under_it_changes_no_other_tab() =>
        Assert.Empty(RailNews.Lit(Tabs(("레이드", ["a"])), RailNews.Opened(Tabs(("레이드", ["a"])), Tabs(("레이드", ["a"])), "파티")));

    [Fact]
    public void Raids_fingerprint_is_empty_without_the_file_and_changes_with_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rail-raids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal("", RaidData.Fingerprint(dir));
            File.WriteAllText(Path.Combine(dir, RaidData.FileName), "[1]");
            var first = RaidData.Fingerprint(dir);
            Assert.NotEqual("", first);
            Assert.Equal(first, RaidData.Fingerprint(dir));
            File.WriteAllText(Path.Combine(dir, RaidData.FileName), "[2]");
            Assert.NotEqual(first, RaidData.Fingerprint(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_undo_keeps_what_the_rail_has_seen()
    {
        var mine = new Settings { RailSeen = Tabs(("레이드", ["a"])) };
        mine.CopyFrom(new Settings { RailSeen = Tabs(("레이드", [])) });
        Assert.Equal(["a"], mine.RailSeen!["레이드"]);
    }
}
