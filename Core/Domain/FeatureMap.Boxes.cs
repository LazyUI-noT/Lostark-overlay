namespace GaugeOverlay.Core.Domain;

/// <summary>A 기능 Map box: <paramref name="Column"/> 0 게임 설정, 1 읽는 것, 2 오버레이, 3 그 위 기능; <paramref name="Inside"/> the
/// large box it sits in (게임 설정, DPS 미터기, 타임라인); <paramref name="Choices"/> each switch part's choices, left to right.</summary>
public sealed record MapBox(string Id, string Title, string Color, int Column, string? Inside = null, params string[][] Choices)
{
    /// <summary>A line of small text under the title, where the box has something to say that its switches don't.</summary>
    public string? Note { get; init; }
}

/// <summary>A map arrow: data flows <paramref name="From"/> → <paramref name="To"/>; <paramref name="Party"/> lines are dotted pink
/// and can be cut (<see cref="FeatureMap.Cut"/>); <paramref name="Optional"/> lines are dashed: the target works without
/// them, better with.</summary>
public sealed record MapEdge(string From, string To, bool Party = false, string? Label = null, bool Optional = false);

/// <summary>What a box's switches add up to: off, read but not shown, shown.</summary>
public enum BoxState { Off, Hidden, On }

/// <summary>How a party line is drawn (<see cref="FeatureMap.LookOf"/>).</summary>
public enum PartyLineLook { Pink, Grey, GreySlashed }

/// <summary>The 기능 Map's boxes and arrows, as the user settled them in mockup v59 (pyosi-map.plan.md).</summary>
public static partial class FeatureMap
{
    const string PartyBox = "party";

    /// <summary>A reader marked ! sends lights back along its lines from the game settings (column 0): look there first (user, 2026-11-05).</summary>
    public static bool FlowsBack(MapEdge edge, IReadOnlyDictionary<string, Detection> board) =>
        !edge.Party && board.GetValueOrDefault(edge.To)?.Mark == DetectMark.Failed && Boxes.First(b => b.Id == edge.From).Column == 0;

    /// <summary>The window a box shows, for its hover GIF; null for a box with none.</summary>
    public static OverlayPart? WindowOf(string id) => id switch
    {
        "skillview" => OverlayPart.Skills, "aden" => OverlayPart.Gauge, "buff" => OverlayPart.Buffs, "debuff" => OverlayPart.Debuffs, "judg" => OverlayPart.Judgment,
        "amsu" => OverlayPart.Amsu, "meter" => OverlayPart.Meter, "count" => OverlayPart.BarCount, "peers" => OverlayPart.Party,
        _ => null,
    };

    /// <summary>A box's lines carry lights: its reader is ✓, or, for a box with no reader of its own (DPS 미터기, 스킬창 표시, …), a reader feeding it is.</summary>
    static bool Flows(string id, IReadOnlyDictionary<string, Detection> board) => board.ContainsKey(id) ? DetectBoard.Flowing(board, id)
        : Edges.Any(e => e.To == id && !e.Party && !e.Optional && DetectBoard.Flowing(board, e.From));

    /// <summary>The line runs lights: its source flows, or it comes from a game setting (no reader there) into a reader that is ✓.</summary>
    public static bool Lit(MapEdge edge, IReadOnlyDictionary<string, Detection> board) =>
        !edge.Party && (Flows(edge.From, board) || Boxes.First(b => b.Id == edge.From).Column == 0 && DetectBoard.Flowing(board, edge.To));

    /// <summary>Touches the 파티 block, so a click on it cuts: the one party line that doesn't (DPS 미터기 → 타임라인) follows 파티 → DPS 미터기.</summary>
    public static bool Cuttable(MapEdge e) => e.Party && (e.From == PartyBox || e.To == PartyBox);

    /// <summary>Drawn as two ends (user, 2026-11-06): a short arrow to a small square at the far box, a square and the clickable
    /// line at the 파티 block, the long line between them only in focus (<see cref="LinkShown"/>). 파티원 창 stands beside 파티: one line.</summary>
    public static bool UsesPorts(MapEdge e) => Cuttable(e) && e.To != "peers";

    /// <summary>The long line and the text show with a box at either end in focus, or while the line itself is hovered.</summary>
    public static bool LinkShown(MapEdge e, string? focus, MapEdge? hovered) => e == hovered || focus == e.From || focus == e.To;

    /// <summary>A party line shows what a click would make it: hovered, a connected one goes grey with a red slash, a cut one pink.</summary>
    public static PartyLineLook LookOf(bool cut, bool hovered) => (cut, hovered) switch
    {
        (false, true) => PartyLineLook.GreySlashed,
        (true, false) => PartyLineLook.Grey,
        _ => PartyLineLook.Pink,
    };

    /// <summary>Each line's offset from the middle of a box's side, in order: <paramref name="step"/> apart within a group,
    /// <paramref name="gap"/> between groups, all drawn closer when they wouldn't fit in <paramref name="room"/>.</summary>
    public static double[] SpreadPorts(IReadOnlyList<int> groups, double step, double gap, double room)
    {
        var at = new double[groups.Count];
        for (int i = 1; i < at.Length; i++) at[i] = at[i - 1] + (groups[i] == groups[i - 1] ? step : gap);
        double span = at.Length == 0 ? 0 : at[^1], scale = span > room ? room / span : 1;
        return [.. at.Select(a => (a - span / 2) * scale)];
    }

    static readonly string[] OffRead = ["끄기", "읽기"], OffOn = ["끄기", "켜기"], Show = ["켜기", "표시 안 함"], Three = ["끄기", "표시 안 함", "켜기"];

    public static IReadOnlyList<MapBox> Boxes { get; } =
    [
        new("game", "게임 설정", Game, 0),
        new("g-res", "해상도 · 비율", Game, 0, "game"),
        new("g-hud", "HUD 크기", Game, 0, "game"),
        new("g-buff", "버프 표시", Game, 0, "game"),
        new("g-debuff", "타겟 UI 버프 크기 (보스)", Game, 0, "game"),
        new("g-font", "데미지 폰트 크기", Game, 0, "game"),
        new("skill", "스킬창 읽기", Skill, 1, null, OffRead),
        new("cls", "직업 인식", Skill, 1, null, ["끄기", "수동", "자동"]),
        new("aden", "아덴 게이지", Skill, 1, null, OffRead, Show),
        new("judg", "단심", Skill, 1, null, OffRead, Show),
        new("buff", "버프", Buff, 1, null, OffRead, Show),
        new("g-jeonbun", "인게임 전분", Record, 1, null, OffRead),
        new("boss", "보스 체력", Raid, 1, null, OffRead, Show),
        new("dungeon", "콘텐츠 인식", Raid, 1, null, OffOn),
        new("amsu", "암수", Raid, 1, null, OffRead, Show),
        new("char", "캐릭터 이름 인식", Homework, 1, null, OffOn),
        // settings-access.plan.md B (user, 2026-11-11): without a key no new names, no 전투력, and no 숙제 on a new install.
        new("api", "로아 API 키", Homework, 1) { Note = "없으면 새 캐릭터 등록 · 전투력 없음" },
        new("party", "파티", Party, 1),
        new("meter", "DPS 미터기", Record, 2, null, Three),
        new("cover", "버프 적용률", Buff, 2, "meter", Three),
        new("skillview", "스킬창 표시", Skill, 2, null, Show),
        new("ring", "기력·충격 창", Skill, 2, null, OffOn) { Note = "브레이커만" },
        new("count", "줄 수 창", Raid, 2, null, OffOn),
        new("debuff", "디버프 창 자동", Raid, 2, null, OffOn),
        // Its switch hides the display only: the number is read whenever the boss HP is (user, 2026-11-09).
        new("shadow", "대난투 체력", Raid, 2, null, Show) { Note = "끄면 표시만 숨김, 기록 · 숙제는 계속 씀" },
        new("peers", "파티원 창", Party, 3, null, Show),
        new("rec", "타임라인", Record, 3, null, OffOn),
        new("icons", "스킬 아이콘", Skill, 3, "rec", OffOn),
        new("buffrows", "버프 줄 · 적용률", Buff, 3, "rec", OffOn),
        new("amsuline", "암수 줄", Record, 3, "rec"),
        new("zone", "대난투 구간", Record, 3, "rec"),
        new("hw", "레이드 숙제", Homework, 3, null, OffOn) { Note = "타임라인 없이도 되지만 틀릴 수 있음" },
    ];

    public static IReadOnlyList<MapEdge> Edges { get; } =
    [
        new("g-jeonbun", "meter"), new("skill", "skillview"), new("boss", "shadow"), new("g-font", "judg"), new("g-hud", "skill"),
        new("g-hud", "aden"), new("g-hud", "ring"), new("g-hud", "amsu"), new("g-buff", "buff"), new("g-debuff", "amsu"), new("cls", "icons"),
        new("skill", "rec", Label: "사용 기록"), new("skill", "icons"), new("buff", "buffrows"), new("meter", "rec", Label: "내 전분"),
        new("meter", "rec", Party: true, Label: "파티원 전분"), new("dungeon", "rec"), new("buff", "cover"), new("cover", "buffrows"),
        new("boss", "count"), new("boss", "debuff"), new("boss", "hw"), new("dungeon", "hw"), new("char", "hw"),
        new("rec", "hw", Label: "있으면 더 정확", Optional: true), new("amsu", "amsuline"), new("shadow", "zone"),
        new("shadow", "hw", Label: "대난투 중 보류"),
        new("api", "hw", Label: "새 설치는 키 필요", Optional: true), new("api", "rec", Label: "전투력", Optional: true),
        new("skill", "party", true, "스킬쿨"), new("aden", "party", true, "아덴"), new("meter", "party", true, "내 전분"),
        new("party", "peers", true, "파티원 창"), new("party", "meter", true, "파티원 전분"),
        new("skill", "meter", Label: "스킬 횟수", Optional: true),
    ];

    /// <summary>The box's state from the settings behind its switches; null for a box with none yet (a frame, or a part
    /// whose switch later phases add: 클래스 인식, 던전, 캐릭터 인식, 그림자, the timeline's inner boxes, 숙제).</summary>
    public static BoxState? State(Settings s, string id) => id switch
    {
        "skill" => On(s.Overlay.SkillRead),
        "skillview" => !s.Overlay.SkillRead ? BoxState.Off : TrayToggles.IsShown(s, OverlayPart.Skills) ? BoxState.On : BoxState.Hidden,
        "aden" => TrayToggles.IsShown(s, OverlayPart.Gauge) ? BoxState.On : BoxState.Hidden,
        "judg" => On(TrayToggles.IsShown(s, OverlayPart.Judgment)),
        "buff" => !Enum.GetValues<BuffKind>().Any(k => s.Buffs.Of(k).Visible) ? BoxState.Off
            : TrayToggles.IsShown(s, OverlayPart.Buffs) ? BoxState.On : BoxState.Hidden,
        "g-jeonbun" => On(s.Meter.On),
        "boss" => !s.Raids.BossHpRead ? BoxState.Off : s.Raids.BossHpEasy ? BoxState.On : BoxState.Hidden,
        "amsu" => On(s.Amsu.On),
        "ring" => On(s.Ring.On),
        "meter" => s.Meter.Mode() == ThreeWay.켜기 && !ClassProfiles.MeterShown(s) ? BoxState.Hidden : Of(s.Meter.Mode()),
        "cover" => Of(s.Meter.BuffShare()),
        "count" => On(s.Raids.CountEnabled),
        "debuff" => On(s.Raids.DebuffsAuto),
        "peers" => s.Party.ShowMembers ? BoxState.On : BoxState.Hidden,
        "rec" => On(s.Meter.On && s.Meter.Record),
        _ => null,
    };

    /// <summary>A party line stopped by its switch (끊기): 스킬쿨 with no group sent, 아덴, 내 전분, 파티원 전분 (also stopped by not
    /// sending 내 전분), 파티원 창.</summary>
    public static bool Cut(Settings s, MapEdge e) => e.Party && (e.From, e.To) switch
    {
        ("skill", "party") => ClassProfiles.SentGroups(s).Count == 0,
        ("aden", "party") => !s.Party.SendGauge,
        ("meter", "party") => !s.Meter.Send,
        ("party", "meter") or ("meter", "rec") => !s.Meter.TakesParty,
        ("party", "peers") => !s.Party.ShowMembers,
        _ => false,
    };

    /// <summary>끊기 on the map: a click on a party line cuts or reconnects it through the switch behind it (the 파티 menu holds the
    /// same ones). 내 전분 is the one that binds: cut, 파티원 전분 goes with it (<see cref="MeterSettings.TakesParty"/>), reconnected, it comes
    /// back. Reconnecting 파티원 전분 also sends 내 전분, as only someone who shares receives; cutting it leaves mine going out.</summary>
    public static void SetCut(Settings s, MapEdge e, bool cut)
    {
        switch (e.From, e.To)
        {
            case ("skill", "party"):
                foreach (var g in Enum.GetValues<SlotGroup>()) s.Party.Send[g] = !cut;
                if (!cut) s.Overlay.SkillRead = true;
                break;
            case ("aden", "party"): s.Party.SendGauge = !cut; break;
            case ("meter", "party"):
                s.Meter.Send = !cut;
                if (!cut) s.Meter.Receive = true;
                break;
            case ("party", "meter") or ("meter", "rec"):
                s.Meter.Receive = !cut;
                if (!cut) s.Meter.Send = true;
                break;
            case ("party", "peers"): s.Party.ShowMembers = !cut; break;
        }
    }

    static BoxState On(bool on) => on ? BoxState.On : BoxState.Off;
    static BoxState Of(ThreeWay w) => w switch { ThreeWay.켜기 => BoxState.On, ThreeWay.표시안함 => BoxState.Hidden, _ => BoxState.Off };
}
