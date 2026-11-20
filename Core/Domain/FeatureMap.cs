namespace GaugeOverlay.Core.Domain;

/// <summary>A group of 설정's rail: its name, the 기능 Map's colour for it ("": neutral), and its areas in order.</summary>
public sealed record RailGroup(string Name, string Color, IReadOnlyList<string> Areas);

/// <summary>The 기능 Map's groups, in one place so the rail and the map can't drift apart.</summary>
public static partial class FeatureMap
{
    const string Game = "#868E96", Skill = "#4C6EF5", Buff = "#2F9E44", Record = "#E8590C", Raid = "#AE3EC9", Homework = "#0C8599", Party = "#D6336C";

    /// <summary>The colours the map's boxes use; each group's colour is one of them.</summary>
    public static IReadOnlyList<string> Colours { get; } = [Game, Skill, Buff, Record, Raid, Homework, Party];

    /// <summary>설정's rail, top to bottom (settings-reorg.plan.md): 즐겨찾기 and 기능 Map alone, then the groups.</summary>
    public static IReadOnlyList<RailGroup> Rail { get; } =
    [
        new("", "", ["즐겨찾기"]),
        new("", "", ["기능 맵"]),
        new("게임 설정", Game, ["인게임 설정", "화면 인식 설정"]),   // 화면·인식's two sections are tabs (rail-split.plan.md, user 2026-11-17)
        new("전투 오버레이", Skill, ["직업 인식", "내 오버레이", "스킬창", "아덴 게이지", "버프 줄", "단심", "암수"]),   // 내 오버레이 is the canvas of these five windows (preview-images.plan.md 1, user 2026-11-20)
        new("레이드", Raid, ["레이드", "DPS 미터기"]),   // DPS 미터기 beside 레이드 (user, 2026-11-10)
        new("전투기록", Record, ["타임라인", "원정대"]),
        new("파티", Party, ["파티 공유"]),
        new("앱", "", ["키·단축키", "시스템 성능", "로아 API", "경고", "테마·업데이트"]),   // 시스템 성능: user, 2026-11-11
    ];

    /// <summary>Rail areas drawn indented: the windows 내 오버레이 shows on its canvas.</summary>
    public static IReadOnlySet<string> RailIndented { get; } = new HashSet<string> { "스킬창", "아덴 게이지", "버프 줄", "단심", "암수" };

    /// <summary>A rail tab's second line, smaller and dimmer under its name.</summary>
    public static IReadOnlyDictionary<string, string> RailSubtitles { get; } = new Dictionary<string, string>
    {
        ["레이드"] = "(HP 간소화, 줄수, 대난투 체력)",
    };

    /// <summary>Setting ids (<see cref="SettingEntry.Id"/>: area/section/label) saved under an area's old name (2026-11-08 and 2026-11-09 renames).</summary>
    static readonly (string Old, string New)[] RenamedPrefixes =
    [
        ("캐릭터/캐릭터/", "직업 인식/직업 인식/"), ("표시/표시/", "기능 맵/기능 맵/"),
        ("기능 Map/기능 Map/", "기능 맵/기능 맵/"), ("화면 선택 및 출력/", "표시할 화면/"), ("원정대 (레이드 선택)/", "레이드 숙제/"), ("파티/", "파티 공유/"),   // 2026-11-09
        // 2026-11-11: the two rate sliders moved from 화면·인식 → 스킬창 맞추기 to their own tab
        ("내 오버레이/아덴 게이지/", "아덴 게이지/아덴 게이지/"),   // 2026-11-13: the gauge has its own page
        ("화면·인식/감지/", "화면·인식/읽는 자리/"),   // 2026-11-16: the 감지 page is 읽는 자리 (word list)
        ("화면·인식/스킬창 맞추기/초당 읽기 횟수", "시스템 성능/시스템 성능/초당 읽기 횟수"), ("화면·인식/스킬창 맞추기/초당 그리기 횟수", "시스템 성능/시스템 성능/초당 그리기 횟수"),
        // reading-place.plan.md A: the fit section is 인게임 설정, and 표시할 화면's rows moved into it. After the two sliders above.
        ("화면·인식/스킬창 맞추기/", "화면·인식/인게임 설정/"), ("표시할 화면/표시할 화면/", "화면·인식/인게임 설정/"),
        // rail-split.plan.md: 레이드 숙제 is 원정대, and 화면·인식's sections are tabs of their own. The catch-alls last.
        ("레이드 숙제/레이드 숙제/", "원정대/원정대/"), ("레이드 숙제/", "원정대/"),
        ("화면·인식/인게임 설정/", "인게임 설정/인게임 설정/"), ("화면·인식/읽는 자리/", "화면 인식 설정/화면 인식 설정/"), ("화면·인식/", "인게임 설정/"),
    ];

    /// <summary>Rows that moved whole (2026-11-13: the 줄 수 창's switch, glow and look from 레이드 to 내 오버레이 → 줄 수,
    /// class-profiles.plan.md E). Exact ids: a prefix would also catch the gates' 「줄 수 창」 row, which stayed.</summary>
    static readonly Dictionary<string, string> MovedIds =
        new[] { "줄 수 읽기 (모든 레이드)", "빛나기", "빛날 때만 표시", "빛나는 동안", "초", "줄", "배경 색", "불투명도" }
            .ToDictionary(label => $"레이드/레이드/{label}", label => $"내 오버레이/줄 수 창/{label}")
            .Append(new("내 오버레이/단심/감지 방식", "내 오버레이/단심/읽는 방식"))
            .Append(new("화면·인식/감지/감지", "화면·인식/읽는 자리/읽는 자리"))
            .Append(new("화면·인식/읽는 자리/읽는 자리", "화면 인식 설정/화면 인식 설정/화면 인식 설정")).ToDictionary();   // rail-split.plan.md   // the page's own row: its label was the section's name   // 2026-11-16: word list

    /// <summary>The id under today's names (favourites and the rail's seen rows saved before the renames); other ids unchanged.
    /// Runs until nothing changes, so an id renamed twice (화면 선택 및 출력 → 표시할 화면 → 인게임 설정) lands on today's.</summary>
    public static string MigrateSettingId(string id)
    {
        for (int step = 0; step < MaxRenames; step++)
        {
            var next = MigrateOnce(id);
            if (next == id) return id;
            id = next;
        }
        return id;
    }

    static readonly HashSet<string> RailAreas = [.. Rail.SelectMany(g => g.Areas)];

    /// <summary>The <see cref="Settings.RailSeen"/> key a seen row belongs under: its id's area when that is a rail tab, else the
    /// old key under today's name (rail-split.plan.md: 화면·인식's rows split between 인게임 설정 and 화면 인식 설정).</summary>
    public static string SeenKey(string key, string id) =>
        id.Split('/')[0] is var head && id.Contains('/') && RailAreas.Contains(head) ? head
        : key switch { "화면·인식" => "인게임 설정", "레이드 숙제" => "원정대", _ => key };

    const int MaxRenames = 8;   // a guard against a cycle in the tables; today's longest chain is 2

    static string MigrateOnce(string id)
    {
        if (MovedIds.TryGetValue(id, out var moved)) return moved;
        foreach (var (old, renamed) in RenamedPrefixes)
            if (id.StartsWith(old, StringComparison.Ordinal)) return renamed + id[old.Length..];
        return id;
    }
}
