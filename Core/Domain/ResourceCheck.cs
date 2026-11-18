namespace GaugeOverlay.Core.Domain;

/// <summary>A row of 리소스 검사: a map box's id, or one of <see cref="ResourceCheck.SharedRows"/>. <paramref name="Percent"/> is of one
/// core; <paramref name="Approx"/>: only wall-clock time was measured (a wait counts), so it is an upper bound; <paramref name="Ran"/>
/// false: a box that never ran while the check was on (its Percent is 0).</summary>
public sealed record ResourceRow(string Id, string Title, double Percent, bool Approx = false, bool Ran = true);

public sealed record ResourceReport(IReadOnlyList<ResourceRow> Rows, double Total, int Cores);

/// <summary>리소스 검사 (Phase 6, pyosi-map.plan.md): the app's CPU by map box, from Perf's parts. Measured on this PC and
/// shown there, never sent or written to a record.</summary>
public static class ResourceCheck
{
    /// <summary>Perf adds this to a part's name for the thread's CPU time over it (the same time as the part without it).</summary>
    public const string CpuTag = " [CPU]";
    public const string Capture = "capture", Shared = "app", Live = "live", Map = "map", Other = "other", Render = "render";

    /// <summary>The rows that are not boxes, in the order they follow the boxes.</summary>
    public static readonly IReadOnlyDictionary<string, string> SharedRows = new Dictionary<string, string>
    {
        [Capture] = "화면 캡처 (모든 읽기가 같이 씀)", [Shared] = "앱 처리 (미터 · 기록 · 파티 보내기)", [Live] = "읽는 자리 Live",
        [Map] = "표시 지도 (선 강조)", [Other] = "그 밖의 일", [Render] = "렌더링 · 런타임",
    };

    /// <summary>Perf part → the row it counts in. Parts are named where they are timed (Engine, DuplicationCapture, OverlayRenderer).</summary>
    public static readonly IReadOnlyDictionary<string, string> PartRows = new Dictionary<string, string>
    {
        ["스킬바 확인"] = "skill", ["스킬 쿨다운"] = "skill", ["각성기 표시"] = "skill", ["스킬 인식"] = "cls", ["아덴 게이지"] = "aden",
        ["버프"] = "buff", ["디버프"] = "debuff", ["디버프 (시간)"] = "debuff", ["단심"] = "judg", ["암수"] = "amsu",
        ["DPS 미터기 (보스 HP · 전분)"] = "meter", ["전분 위치 찾기"] = "g-jeonbun",
        ["레이드 (보스 이름 · 줄 수)"] = "boss", ["보스 바 확인"] = "boss", ["보스 이름 (Windows OCR)"] = "boss",
        ["던전 이름 (Windows OCR)"] = "dungeon",
        ["게임 메뉴 (제목 비교)"] = "char", ["게임 메뉴 (칸 비교)"] = "char", ["게임 메뉴 (Windows OCR)"] = "char",
        ["그리기: 스킬 창"] = "skillview", ["그리기: 파티 창"] = "peers", ["그리기: 전투자원 창"] = "ring",
        ["그리기: 버프 창"] = "buff", ["그리기: 게이지 창"] = "aden", ["그리기: 디버프 창"] = "debuff", ["그리기: 단심 창"] = "judg",
        ["그리기: 암수 창"] = "amsu", ["그리기: 줄 수 창"] = "count", ["그리기: 미터기 창"] = "meter",
        ["그리기: 보스 체력 창"] = "boss", ["그리기: 대난투 체력 창"] = "shadow",   // OverlayTextWindow
        ["화면 캡처 (새 프레임 대기 포함)"] = Capture, ["앱 처리 (미터 · 기록 · 파티 보내기)"] = Shared,
        ["Live"] = Live, ["지도 선 강조"] = Map,
    };

    /// <summary>Parts timed inside another part on the same thread: counted once, in the outer one.</summary>
    static readonly HashSet<string> Inside = ["화면 캡처: 복사·읽기", "캡처 텍스처 새로 만들기"];

    /// <summary>Parts that are a wait for the game's next frame: never "what ran" during a gap.</summary>
    public static bool IsWait(string part) => part == "화면 캡처 (새 프레임 대기 포함)";

    /// <summary>The row a part counts in (<see cref="Other"/> for one nobody mapped).</summary>
    public static string RowOf(string part) => PartRows.GetValueOrDefault(part, Other);

    /// <summary>A box stays active this long after each run of one of its parts (<see cref="ActiveTime"/>).</summary>
    public const double ActiveGrace = 3;

    static readonly HashSet<string> MeasuredBoxes = [.. PartRows.Values.Where(id => !SharedRows.ContainsKey(id))];

    /// <param name="seconds">Perf's time per part over the window, in seconds; a part's CPU time under the same name plus <see cref="CpuTag"/>.</param>
    /// <param name="processCpu">The process's CPU seconds over the window (all threads).</param>
    /// <param name="window">Seconds the figures cover.</param>
    /// <param name="active">Seconds each row was active (<see cref="ActiveTime"/>), by row id; null: every row over the window.
    /// Only map boxes use it: the shared rows and the total run whenever the app does, so they stay over the window. With it,
    /// a measured box that never ran is listed with <see cref="ResourceRow.Ran"/> false.</param>
    public static ResourceReport Build(IReadOnlyDictionary<string, double> seconds, double processCpu, double window, int cores,
        IReadOnlyDictionary<string, double>? active = null)
    {
        var bare = seconds.Keys.Select(k => k.EndsWith(CpuTag) ? k[..^CpuTag.Length] : k).Where(k => !Inside.Contains(k)).Distinct().ToList();
        var rows = new Dictionary<string, (double Seconds, bool Approx)>();
        double measured = 0;   // CPU seconds that the process total already holds as these parts
        foreach (var part in bare)
        {
            bool cpu = seconds.TryGetValue(part + CpuTag, out var c);
            double s = cpu ? c : seconds.GetValueOrDefault(part);
            if (cpu) measured += s;
            var (was, approx) = rows.GetValueOrDefault(RowOf(part));
            rows[RowOf(part)] = (was + s, approx || !cpu);
        }
        double Pct(double s, double over) => over > 0 ? s * 100 / over : 0;
        // A box's own time; a row with no record (or none longer than zero) keeps the window, and never more than the window.
        double Over(string id) => active is not null && !SharedRows.ContainsKey(id) && active.GetValueOrDefault(id) is > 0 and var a
            ? Math.Min(a, window) : window;
        rows[Render] = (Math.Max(0, processCpu - measured), false);
        var ordered = FeatureMap.Boxes.Select(b => (b.Id, b.Title)).Concat(SharedRows.Select(r => (r.Key, r.Value)))
            .Where(r => rows.ContainsKey(r.Item1) ? r.Item1 != Other || rows[r.Item1].Seconds > 0 : active is not null && MeasuredBoxes.Contains(r.Item1))
            .Select(r => rows.TryGetValue(r.Item1, out var row)
                ? new ResourceRow(r.Item1, r.Item2, Pct(row.Seconds, Over(r.Item1)), row.Approx)
                : new ResourceRow(r.Item1, r.Item2, 0, Ran: false)).ToList();
        return new ResourceReport(ordered, Pct(processCpu, window), cores);
    }

    /// <summary>"2.4% (0.20%)": of one core, then of all cores (one core / logical cores).</summary>
    public static string Label(double percent, int cores) => $"{percent:0.0}% ({percent / Math.Max(1, cores):0.00}%)";
}
