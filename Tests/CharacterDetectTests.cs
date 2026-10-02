using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>What 스킬바 다시 맞추기 makes active (CharacterDetect.Resolve): a character is kept only when the capture
/// confirms it; nothing confirmed is 기본.</summary>
public class CharacterDetectTests
{
    static readonly ClassInfo[] Classes =
    [
        new("전사", "워로드", [], new Dictionary<string, GaugeRule?> { ["전투 태세"] = null, ["고독한 기사"] = null }),
        new("전사", "홀리나이트", [], new Dictionary<string, GaugeRule?> { ["축복의 오라"] = null, ["심판자"] = null }),
    ];
    const string Holy = "홀리나이트 · 축복의 오라", Lone = "워로드 · 고독한 기사";

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
    public void A_wrongly_learned_class_gives_way_to_the_class_the_centre_piece_shows()
    {
        // 홀리나이트 was active when 워로드's bar was learned: its emblem matches, but the centre piece shows 워로드.
        var s = With(Lone, Holy);
        var next = CharacterDetect.Resolve(s, Classes, [(Holy, 0.95), (Lone, 0.93)], fits: c => c == "워로드", shown: "워로드", emblem: "new");
        Assert.Equal(Lone, next);
        Assert.Equal(Lone, s.ActiveCharacter);
        Assert.Null(s.Characters[Holy].EmblemBgra);   // unlearned, so it can't win again
    }

    [Fact]
    public void The_class_shown_with_no_character_of_it_yet_adds_one()
    {
        var s = With(Holy);
        var next = CharacterDetect.Resolve(s, Classes, [(Holy, 0.95)], fits: c => c == "워로드", shown: "워로드", emblem: "new");
        Assert.Equal("워로드 · 전투 태세", next);   // the first 직업각인; the 초각스 read corrects it
        Assert.Equal("new", s.Characters[next!].EmblemBgra);
    }

    [Fact]
    public void Nothing_confirmed_returns_to_the_default()
    {
        var s = With(Holy);
        Assert.Null(CharacterDetect.Resolve(s, Classes, [], fits: _ => true, shown: null, emblem: "new"));
        Assert.Null(s.ActiveCharacter);
        Assert.Equal("learned", s.Characters[Holy].EmblemBgra);   // kept for a later detection
    }

    [Fact]
    public void A_confirmed_character_stays_active_among_others_alike()
    {
        var s = With("워로드 · 전투 태세", Lone);   // 고독한 기사 active
        var next = CharacterDetect.Resolve(s, Classes, [("워로드 · 전투 태세", 0.97), (Lone, 0.9)], fits: _ => true, shown: "워로드", emblem: "new");
        Assert.Equal(Lone, next);
    }
}
