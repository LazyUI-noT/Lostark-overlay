using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정 → 캐릭터's list of supported 직업각인: built from the shipped data (classes.json, Profiles/subclass, Profiles/aden),
/// and what is new since the user last looked.</summary>
public class ClassSupportTests
{
    static readonly string Profiles = Path.Combine(Helpers.RepoRoot(), "Profiles");
    static string Key(SlotId slot) => slot switch { SlotId.아덴1 => "Z", SlotId.아덴2 => "C", _ => slot.ToString() };

    static IReadOnlyList<SupportRow> Rows() => ClassSupport.Rows(Profiles, ProfileLoader.LoadClasses(Profiles), Key);

    [Fact]
    public void Every_shipped_class_with_icons_or_a_gauge_is_listed_and_no_other()
    {
        var rows = Rows();
        Assert.Contains(rows, r => (r.Class, r.Subclass) == ("워로드", "전투태세"));
        Assert.Contains(rows, r => (r.Class, r.Subclass) == ("홀리나이트", "심판자") && r.Detect && r.Gauge is null && r.Skills is null);
        Assert.DoesNotContain(rows, r => r.Class == "디스트로이어");   // one 초각스 icon for both: not told apart
        Assert.DoesNotContain(rows, r => r.Class == "소서리스");       // nothing yet
        Assert.All(rows, r => Assert.True(r.Detect || r.Gauge is not null));
    }

    [Fact]
    public void The_gauge_says_how_it_is_read_and_shown()
    {
        var rows = Rows();
        SupportRow Of(string cls, string sub) => rows.Single(r => (r.Class, r.Subclass) == (cls, sub));
        Assert.Equal(("차오르는 높이를 읽습니다.", "막대"), (Of("워로드", "고독한기사").Gauge!.Read, Of("워로드", "고독한기사").Gauge!.Shown));
        Assert.Equal("2구간, 50% 이상이고 C가 준비되면 빛남", Of("워로드", "전투태세").Gauge!.Shown);
        Assert.Equal("다 차고 C가 준비되면 빛남", Of("발키리", "빛의기사").Gauge!.Shown);
        Assert.Equal("결정 4개가 하나씩 차는 것을 읽습니다.", Of("바드", "절실한구원").Gauge!.Read);
        Assert.Equal("4구간, 다 찬 구간마다 빛남", Of("바드", "절실한구원").Gauge!.Shown);
        Assert.Equal("구슬 3개가 시계 방향으로 차는 것을 읽습니다.", Of("도화가", "만개").Gauge!.Read);
    }

    [Fact]
    public void The_aden_skills_name_their_keys_and_how_unusable_shows()
    {
        var rows = Rows();
        SupportRow Of(string cls, string sub) => rows.Single(r => (r.Class, r.Subclass) == (cls, sub));
        Assert.Equal("C", Of("워로드", "고독한기사").Skills);
        Assert.Equal("C (게이지가 모자라면 키 글자가 회색)", Of("워로드", "전투태세").Skills);
        Assert.Equal("Z, C (게이지가 모자라면 아이콘이 어두움)", Of("발키리", "해방자").Skills);
    }

    /// <summary>A broken gauge.json (as the engine treats it: no gauge) leaves the row out instead of breaking 설정 or start-up.</summary>
    [Fact]
    public void A_malformed_gauge_file_means_no_gauge_not_a_crash()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gauge-support-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "aden", "시험_가"));
        File.WriteAllText(Path.Combine(dir, "aden", "시험_가", GaugeLoader.JsonFile), "[not json");
        try
        {
            var cls = new ClassInfo("가족", "시험", [SlotId.아덴1], new Dictionary<string, GaugeRule?> { ["가"] = new GaugeRule([], null) });
            Assert.Empty(ClassSupport.Rows(dir, [cls], Key));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // --- 코어 빌드 ---

    [Fact]
    public void A_core_effect_in_the_data_is_described_for_its_row()
    {
        var row = Rows().Single(r => (r.Class, r.Subclass) == ("워로드", "고독한기사"));
        Assert.True(row.HasCoreData);
        Assert.Equal("3xx: 버스트 캐넌을 쓰면 아덴2(C) 쿨초를 빨리 읽습니다", row.Cores);
    }

    /// <summary>A temp profiles folder with a class 시험 whose 직업각인 가 and 나 both have a gauge, and a class 시험2 with one; the
    /// cores file (if any) is 시험.json.</summary>
    static (string Dir, IReadOnlyList<ClassInfo> Classes) Temp(string? coresJson)
    {
        var dir = Path.Combine(Path.GetTempPath(), "gauge-support-" + Guid.NewGuid().ToString("N"));
        foreach (var folder in new[] { "시험_가", "시험_나", "시험2_가" })
        {
            Directory.CreateDirectory(Path.Combine(dir, "aden", folder));
            File.WriteAllText(Path.Combine(dir, "aden", folder, GaugeLoader.JsonFile), """{ "order": { "kind": "fill" } }""");
        }
        if (coresJson is not null)
        {
            Directory.CreateDirectory(Path.Combine(dir, "cores"));
            File.WriteAllText(Path.Combine(dir, "cores", "시험.json"), coresJson);
        }
        var rule = new GaugeRule([], null);
        return (dir, [
            new ClassInfo("가족", "시험", [SlotId.아덴1], new Dictionary<string, GaugeRule?> { ["가"] = rule, ["나"] = rule }),
            new ClassInfo("가족", "시험2", [SlotId.아덴1], new Dictionary<string, GaugeRule?> { ["가"] = rule }),
        ]);
    }

    [Fact]
    public void A_row_without_core_data_has_no_core_line_like_a_row_without_a_gauge()
    {
        var (dir, classes) = Temp("""
            { "subclasses": { "가": { "effects": [ { "requires": { "해": 2 }, "trigger": "스킬", "effect": "cooldown_reduce", "target": "아덴1" } ] } } }
            """);
        try
        {
            var rows = ClassSupport.Rows(dir, classes, Key);
            Assert.Equal(("2xx: 스킬을 쓰면 아덴1(Z) 쿨초를 빨리 읽습니다", true), (rows.Single(r => r.Subclass == "가" && r.Class == "시험").Cores, rows.Single(r => r.Subclass == "가" && r.Class == "시험").HasCoreData));
            Assert.Equal((null, false), (rows.Single(r => r.Subclass == "나").Cores, rows.Single(r => r.Subclass == "나").HasCoreData));   // the class has a file, this 직업각인 not
            Assert.Null(rows.Single(r => r.Class == "시험2").Cores);   // no file at all
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_malformed_cores_file_means_a_row_without_data_not_a_crash()
    {
        var (dir, classes) = Temp("[not json");
        try { Assert.All(ClassSupport.Rows(dir, classes, Key), r => Assert.Equal((null, false), (r.Cores, r.HasCoreData))); }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_gauge_file_with_a_repeated_key_means_no_gauge_not_a_crash()
    {
        var (dir, classes) = Temp(null);
        File.WriteAllText(Path.Combine(dir, "aden", "시험2_가", GaugeLoader.JsonFile), """{ "order": { "kind": "a" }, "order": { "kind": "b" } }""");
        try { Assert.DoesNotContain(ClassSupport.Rows(dir, classes, Key), r => r.Class == "시험2"); }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_shipped_row_has_a_core_line_exactly_when_it_has_core_data() =>
        Assert.All(Rows(), r => Assert.Equal(r.HasCoreData, r.Cores is { Length: > 0 }));

    /// <summary>The 코어 feature is NEW only where it has data, so an update marks that one feature and no whole row; a first
    /// run marks nothing.</summary>
    [Fact]
    public void The_core_feature_is_a_seen_key_only_where_there_is_data_and_marks_only_itself_new()
    {
        var rows = Rows();
        var keys = ClassSupport.Keys(rows);
        Assert.Contains("워로드/고독한기사/코어", keys);
        Assert.Contains("워로드/전투태세/코어", keys);
        Assert.DoesNotContain(keys, k => k.StartsWith("발키리/") && k.EndsWith("/코어"));

        var seenBeforeCores = keys.Where(k => !k.EndsWith("/코어")).ToList();
        Assert.Equal(["워로드/고독한기사/코어", "워로드/전투태세/코어"], ClassSupport.New(rows, seenBeforeCores).Order());
        Assert.Empty(ClassSupport.New(rows, seen: null));
        Assert.Empty(ClassSupport.New(rows, keys));
    }

    [Fact]
    public void Nothing_is_new_the_first_time()
    {
        Assert.Empty(ClassSupport.New(Rows(), seen: null));
    }

    [Fact]
    public void A_new_class_and_a_new_feature_are_new_until_seen()
    {
        var rows = Rows();
        var seen = ClassSupport.Keys(rows).Where(k => !k.StartsWith("바드/") && k != "홀리나이트/심판자/감지").ToList();
        var fresh = ClassSupport.New(rows, seen);
        Assert.Contains("바드/절실한구원", fresh);           // the whole 직업각인
        Assert.Contains("바드/절실한구원/게이지", fresh);
        Assert.Contains("홀리나이트/심판자/감지", fresh);      // one feature of a row already seen
        Assert.DoesNotContain("홀리나이트/심판자", fresh);
        Assert.Empty(ClassSupport.New(rows, ClassSupport.Keys(rows)));
    }

    // --- the table's order (user, 2026-10-13): by 직업군, rows with anything new on top until seen ---

    static SupportRow Row(string family, string cls, string sub) => new(cls, sub, true, null, null) { Family = family };

    [Fact]
    public void Rows_are_grouped_by_직업군_with_new_ones_on_top()
    {
        SupportRow a = Row("워리어", "워로드", "가"), b = Row("워리어", "발키리", "나"), c = Row("마법사", "바드", "다");
        var groups = ClassSupport.Grouped([a, b, c], new HashSet<string> { c.Key, b.Key + "/감지" });
        Assert.Equal(["새로 추가됨", "워리어"], groups.Select(g => g.Heading));
        Assert.Equal([b, c], groups[0].Rows);   // a new feature counts as well as a new row
        Assert.Equal([a], groups[1].Rows);
    }

    [Fact]
    public void Seen_rows_stay_in_their_직업군()
    {
        SupportRow a = Row("워리어", "워로드", "가"), c = Row("마법사", "바드", "다");
        var groups = ClassSupport.Grouped([a, c], new HashSet<string>());
        Assert.Equal(["워리어", "마법사"], groups.Select(g => g.Heading));
    }
}
