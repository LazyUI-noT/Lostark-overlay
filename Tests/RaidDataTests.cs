using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Profiles/raids.json: the raids and gates the 레이드 table lists, their boss names and default alert counts.</summary>
public class RaidDataTests
{
    static readonly string Profiles = Path.Combine(Helpers.RepoRoot(), "Profiles");

    [Fact]
    public void Loads_the_shipped_gates()
    {
        var gates = RaidData.Load(Profiles);
        var g = Assert.Single(gates, x => x.Id == "act3-g3");
        Assert.Equal(("3막", "3관문"), (g.Raid, g.Gate));
        Assert.Contains("심연의 징벌자, 모르둠", g.Bosses);
        Assert.Empty(g.Alerts);   // the user gives the counts; none ship yet
        Assert.Equal(Raid.모르둠, g.DebuffRaid);
    }

    [Fact]
    public void Only_세르카_and_구원의_종탑_are_4_man()
    {
        var small = RaidData.Load(Profiles).Where(g => g.SmallParty).Select(g => g.Id).Order();
        Assert.Equal(["belltower-g1", "belltower-g2", "serka-g1", "serka-g2"], small);
    }

    [Fact]
    public void A_missing_or_broken_file_gives_no_gates()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Empty(RaidData.Load(dir));
            File.WriteAllText(Path.Combine(dir, RaidData.FileName), "{ not json");
            Assert.Empty(RaidData.Load(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void An_unknown_debuff_raid_reads_as_none_and_alerts_are_sorted_high_to_low()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, RaidData.FileName),
                """{ "gates": [ { "id": "x", "raid": "R", "gate": "G", "bosses": ["B"], "alerts": [100, 750, 300], "debuffRaid": "nope" } ] }""");
            var g = Assert.Single(RaidData.Load(dir));
            Assert.Equal(Raid.없음, g.DebuffRaid);
            Assert.Equal([750, 300, 100], g.Alerts);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Label_reads_raid_and_gate()
    {
        var g = RaidData.Find(RaidData.Load(Profiles), "act3-g3")!;
        Assert.Equal("3막 · 3관문", RaidData.Label(g));
        Assert.Null(RaidData.Find(RaidData.Load(Profiles), "nope"));
    }

    [Fact]
    public void A_gate_is_found_by_its_boss_max_hp_with_the_difficulty()
    {
        var gates = RaidData.Load(Profiles);
        var (gate, difficulty) = RaidData.ByHp(gates, 1_950_577_229_952)!.Value;
        Assert.Equal(("belgardin-g2", "노말"), (gate.Id, difficulty));   // 페투스 안 크라그마 (test subject.mp4)
        Assert.Equal("벨가르딘 · 2관문", RaidData.Label(gate));
        Assert.Equal([240, 195, 115, 75], RaidData.Find(gates, "belgardin-g1")!.Alerts);   // 1관문's 기믹 줄 수
        Assert.Equal([360, 320, 280, 200, 100], gate.Alerts);   // 2관문's
        var tower = RaidData.ByHp(gates, 1_197_954_508_761)!.Value;   // 구원의 종탑: 1–3단계 instead of 노말/하드 (a friend's 1440p clip, 2026-10-31)
        Assert.Equal(("구원의 종탑 · 2관문", "3단계"), (RaidData.Label(tower.Gate), tower.Difficulty));
        Assert.Equal([160, 100, 60], RaidData.Find(gates, "belltower-g1")!.Alerts);
        var single = RaidData.ByHp(gates, 25_807_352_833)!.Value;   // 4막 1관문 [싱글 모드] (a friend's 1440p clip)
        Assert.Equal(("act4-g1", "싱글"), (single.Gate.Id, single.Difficulty));
        var ex = RaidData.ByHp(gates, 11_123_200_336_247)!.Value;   // 종막: EX has its own gate rows (own alerts)
        Assert.Equal(("종막: 최후의 날 · EX 2-1관문", "EX 나이트메어"), (RaidData.Label(ex.Gate), ex.Difficulty));
        Assert.Equal("final-g1", RaidData.ByHp(gates, 41_816_032_557)!.Value.Gate.Id);
        Assert.Equal(("final-ex-g2-2", "EX 하드"), (RaidData.ByHp(gates, 2_223_536_657_594)!.Value.Gate.Id, RaidData.ByHp(gates, 2_223_536_657_594)!.Value.Difficulty));
        var serka = RaidData.ByHp(gates, 1_539_242_432_318)!.Value;   // 고통의 마녀 2관문 (코르부스 툴 라크)
        Assert.Equal(("고통의 마녀 · 2관문", "나이트메어"), (RaidData.Label(serka.Gate), serka.Difficulty));
        Assert.Equal([285, 240, 195, 120, 60], serka.Gate.Alerts);
        Assert.All(gates.SelectMany(g => g.Hp.Values).GroupBy(h => h), g => Assert.Single(g));   // an HP names one gate only
        Assert.Equal(("belgardin-g1", "매칭"), (RaidData.ByHp(gates, 869_658_397_729)!.Value.Gate.Id, RaidData.ByHp(gates, 869_658_397_729)!.Value.Difficulty));
        Assert.Equal("3막", RaidData.Label(new("x", "3막", "", [], [], Raid.없음)));   // no gate name: the raid alone
        Assert.Null(RaidData.ByHp(gates, 1_950_577_229_953));
        Assert.Null(RaidData.ByHp(gates, null));
    }

    [Fact]
    public void A_gates_hp_per_difficulty_is_read_from_the_file()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, RaidData.FileName),
                """{ "gates": [ { "id": "x", "raid": "R", "gate": "G", "hp": { "노말": 100, "하드": 200 } }, { "id": "y", "raid": "R2", "gate": "G2" } ] }""");
            var gates = RaidData.Load(dir);
            Assert.Equal(("x", "하드"), (RaidData.ByHp(gates, 200)!.Value.Gate.Id, RaidData.ByHp(gates, 200)!.Value.Difficulty));
            Assert.Empty(gates[1].Hp);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Gates_are_grouped_by_tab_then_raid_in_file_order()
    {
        var gates = RaidData.Load(Profiles);
        Assert.Equal(["카제로스 레이드", "그림자 레이드", "어비스 던전"], gates.Select(g => g.TabName).Distinct());
        Assert.Equal(["고통의 마녀", "벨가르딘"], gates.Where(g => g.TabName == "그림자 레이드").Select(g => g.Raid).Distinct());
        Assert.Equal(["3막", "4막: 파멸의 성채", "종막: 최후의 날"], gates.Where(g => g.TabName == "카제로스 레이드").Select(g => g.Raid).Distinct());
        var armoche = RaidData.ByHp(gates, 1_340_665_693_560)!.Value;
        Assert.Equal(("4막: 파멸의 성채 · 2관문", "하드"), (RaidData.Label(armoche.Gate), armoche.Difficulty));
        Assert.Equal("기타", new RaidGate("x", "R", "G", [], [], Raid.없음).TabName);
    }

    [Fact]
    public void Ex_gates_list_their_esther_skills()
    {
        var gates = RaidData.Load(Profiles);
        var g23 = RaidData.Find(gates, "final-ex-g2-3")!;
        Assert.Equal(["공아만 3칸", "공아만 1칸", "히든 아만 3칸"], g23.Esthers.Select(e => e.Name));
        Assert.Equal((3779.0, (int?)10), (g23.Esthers[2].Damage["EX 나이트메어"], g23.Esthers[2].At));
        Assert.Equal(5, RaidData.Find(gates, "final-ex-g2-1")!.Esthers.Count);
        Assert.Empty(RaidData.Find(gates, "act3-g3")!.Esthers);
        Assert.Equal([new GateShield(305, 10)], RaidData.Find(gates, "final-ex-g2-2")!.Shields);   // 2-2's shield: +10 bars at 305
        Assert.Equal([new GateShield(305, 10)], RaidData.Find(gates, "final-g2-2")!.Shields);
    }
}
