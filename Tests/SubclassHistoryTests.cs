using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The 직업각인 last played per class (Settings.SubclassHistory): a class detected with one takes it and isn't read
/// again; a pick by hand and an automatic read write it; a class whose 직업각인 have the same skills (디스트로이어) is never
/// read and starts from its first.</summary>
public class SubclassHistoryTests
{
    static readonly ClassInfo[] Classes =
    [
        new("전사", "워로드", [], new Dictionary<string, GaugeRule?> { ["전투 태세"] = null, ["고독한 기사"] = null }),
        new("전사", "홀리나이트", [], new Dictionary<string, GaugeRule?> { ["축복의 오라"] = null, ["심판자"] = null }),
        new("전사", "디스트로이어", [], new Dictionary<string, GaugeRule?> { ["분노의망치"] = null, ["중력수련"] = null }, SameSkills: true),
    ];
    const string Holy = "홀리나이트 · 축복의 오라", Lone = "워로드 · 고독한 기사", Stance = "워로드 · 전투 태세";

    static Settings With(params string[] keys)
    {
        var s = new Settings();
        foreach (var k in keys)
        {
            var parts = k.Split(" · ");
            s.PickCharacter("전사", parts[0], parts[1]);
            s.Characters[k].EmblemBgra = "learned";
        }
        return s;
    }

    [Fact]
    public void A_class_detected_with_a_history_takes_its_직업각인()
    {
        var s = With(Holy);
        s.SubclassHistory["워로드"] = "고독한 기사";
        var next = CharacterDetect.Resolve(s, Classes, [], fits: c => c == "워로드", shown: "워로드", emblem: "new");
        Assert.Equal(Lone, next);
        Assert.Equal("new", s.Characters[Lone].EmblemBgra);
    }

    [Fact]
    public void Among_characters_of_one_class_the_history_wins()
    {
        var s = With(Stance, Lone);   // 고독한 기사 active, but 전투 태세 played last
        s.SubclassHistory["워로드"] = "전투 태세";
        var next = CharacterDetect.Resolve(s, Classes, [(Stance, 0.9), (Lone, 0.97)], fits: _ => true, shown: "워로드", emblem: "new");
        Assert.Equal(Stance, next);
    }

    [Fact]
    public void Without_a_history_the_first_직업각인_is_set_and_not_remembered()
    {
        var s = With(Holy);
        var next = CharacterDetect.Resolve(s, Classes, [], fits: _ => true, shown: "워로드", emblem: "new");
        Assert.Equal(Stance, next);                             // the 초각스 read corrects it and writes the history
        Assert.False(s.SubclassHistory.ContainsKey("워로드"));
    }

    [Fact]
    public void A_same_skills_class_takes_its_first_직업각인_and_remembers_it()
    {
        var s = With(Holy);
        var next = CharacterDetect.Resolve(s, Classes, [], fits: _ => true, shown: "디스트로이어", emblem: "new");
        Assert.Equal("디스트로이어 · 분노의망치", next);
        Assert.Equal("분노의망치", s.SubclassHistory["디스트로이어"]);
    }

    [Fact]
    public void A_history_naming_no_직업각인_of_the_class_is_ignored()
    {
        var s = With(Holy);
        s.SubclassHistory["워로드"] = "없는 각인";
        Assert.Equal(Stance, CharacterDetect.Resolve(s, Classes, [], fits: _ => true, shown: "워로드", emblem: "new"));
    }

    [Fact]
    public void A_직업각인_read_off_the_bar_is_remembered()
    {
        var s = With(Lone);
        s.DetectedSubclass("전투 태세");
        Assert.Equal("전투 태세", s.SubclassHistory["워로드"]);
        s.DetectedSubclass("전투 태세");                        // confirmed again: still remembered
        Assert.Equal(Stance, s.ActiveCharacter);
    }

    [Fact]
    public void A_pick_by_hand_is_remembered()
    {
        var s = new Settings();
        s.PickCharacter("전사", "워로드", "고독한 기사", remember: true);
        Assert.Equal("고독한 기사", s.SubclassHistory["워로드"]);
        s.PickCharacter("전사", "홀리나이트", "심판자");        // not by hand (detection's placeholder): not remembered
        Assert.False(s.SubclassHistory.ContainsKey("홀리나이트"));
    }

    [Fact]
    public void Only_디스트로이어_is_marked_as_having_the_same_skills()
    {
        var classes = ProfileLoader.LoadClasses(Path.Combine(RepoRoot(), "Profiles"));
        Assert.Equal(["디스트로이어"], classes.Where(c => c.SameSkills).Select(c => c.Name));
    }

    [Fact]
    public void Settings_from_before_the_history_start_it_from_the_active_character()
    {
        var dir = Path.Combine(Path.GetTempPath(), "GaugeOverlayTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            var old = new Settings { Version = 2 };
            old.PickCharacter("전사", "워로드", "고독한 기사");
            store.Save(old);
            Assert.Equal("고독한 기사", store.Load(out _).SubclassHistory["워로드"]);

            // A file written since: an empty history stays empty (its 직업각인 may be detection's unread placeholder).
            var now = new Settings();
            now.PickCharacter("전사", "워로드", "전투 태세");
            store.Save(now);
            Assert.Empty(store.Load(out _).SubclassHistory);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_redetect_hotkey_is_unset_by_default()
    {
        var h = new HotkeySettings();
        Assert.Equal("", h.DetectSubclass);
        Assert.Contains((nameof(HotkeySettings.DetectSubclass), ""), h.All());
    }
}
