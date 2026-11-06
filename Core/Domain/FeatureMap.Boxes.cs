namespace GaugeOverlay.Core.Domain;

/// <summary>A 표시 map box: <paramref name="Column"/> 0 게임 설정, 1 읽는 것, 2 오버레이, 3 그 위 기능; <paramref name="Inside"/> the
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

/// <summary>The 표시 map's boxes and arrows, as the user settled them in mockup v59 (pyosi-map.plan.md).</summary>
public static partial class FeatureMap
{
    /// <summary>A reader marked ! sends lights back along its lines from the game settings (column 0): look there first (user, 2026-11-05).</summary>
    public static bool FlowsBack(MapEdge edge, IReadOnlyDictionary<string, Detection> board) =>
        !edge.Party && board.GetValueOrDefault(edge.To)?.Mark == DetectMark.Failed && Boxes.First(b => b.Id == edge.From).Column == 0;

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
        new("cls", "클래스 인식", Skill, 1, null, ["끄기", "수동", "자동"]),
        new("aden", "아덴 게이지", Skill, 1, null, OffRead, Show),
        new("judg", "단심", Skill, 1, null, OffRead, Show),
        new("buff", "버프", Buff, 1, null, OffRead, Show),
        new("g-jeonbun", "인게임 전분", Record, 1, null, OffRead),
        new("boss", "보스 체력", Raid, 1, null, OffRead, Show),
        new("dungeon", "던전 인식", Raid, 1, null, OffOn),
        new("amsu", "암수", Raid, 1, null, OffRead, Show),
        new("char", "캐릭터 인식", Homework, 1, null, OffOn),
        new("party", "파티", Party, 1),
        new("meter", "DPS 미터기", Record, 2, null, Three),
        new("cover", "버프 적용률", Buff, 2, "meter", Three),
        new("skillview", "스킬창 표시", Skill, 2, null, Show),
        new("count", "줄 수 창", Raid, 2, null, OffOn),
        new("debuff", "디버프 창 자동", Raid, 2, null, OffOn),
        new("shadow", "그림자 기믹 체력", Raid, 2, null, OffRead, Show),
        new("peers", "파티원 창", Party, 3, null, Show),
        new("rec", "타임라인", Record, 3, null, OffOn),
        new("icons", "스킬 아이콘", Skill, 3, "rec", OffOn),
        new("buffrows", "버프 줄 · 적용률", Buff, 3, "rec", OffOn),
        new("amsuline", "암수 줄", Record, 3, "rec"),
        new("zone", "대난투 구간", Record, 3, "rec"),
        new("hw", "숙제", Homework, 3, null, OffOn) { Note = "타임라인 없이도 되지만 틀릴 수 있음" },
    ];

    public static IReadOnlyList<MapEdge> Edges { get; } =
    [
        new("g-jeonbun", "meter"), new("skill", "skillview"), new("boss", "shadow"), new("g-font", "judg"), new("g-hud", "skill"),
        new("g-hud", "aden"), new("g-hud", "amsu"), new("g-buff", "buff"), new("g-debuff", "amsu"), new("cls", "icons"),
        new("skill", "rec", Label: "사용 기록"), new("skill", "icons"), new("buff", "buffrows"), new("meter", "rec", Label: "내 전분"),
        new("meter", "rec", Party: true, Label: "파티원 전분"), new("dungeon", "rec"), new("buff", "cover"), new("cover", "buffrows"),
        new("boss", "count"), new("boss", "debuff"), new("boss", "hw"), new("dungeon", "hw"), new("char", "hw"),
        new("rec", "hw", Label: "있으면 더 정확", Optional: true), new("amsu", "amsuline"), new("shadow", "zone"),
        new("skill", "party", true, "스킬쿨"), new("aden", "party", true, "아덴"), new("meter", "party", true, "내 전분"),
        new("party", "peers", true), new("party", "meter", true, "파티원 전분"),
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
    /// same ones). Reconnecting 파티원 전분 also sends 내 전분, as only someone who shares receives.</summary>
    public static void SetCut(Settings s, MapEdge e, bool cut)
    {
        switch (e.From, e.To)
        {
            case ("skill", "party"):
                foreach (var g in Enum.GetValues<SlotGroup>()) s.Party.Send[g] = !cut;
                if (!cut) s.Overlay.SkillRead = true;
                break;
            case ("aden", "party"): s.Party.SendGauge = !cut; break;
            case ("meter", "party"): s.Meter.Send = !cut; break;
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
