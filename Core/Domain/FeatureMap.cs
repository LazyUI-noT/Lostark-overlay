namespace GaugeOverlay.Core.Domain;

/// <summary>A group of 설정's rail: its name, the 표시 map's colour for it ("": neutral), and its areas in order.</summary>
public sealed record RailGroup(string Name, string Color, IReadOnlyList<string> Areas);

/// <summary>The 표시 map's groups, in one place so the rail and the map can't drift apart.</summary>
public static partial class FeatureMap
{
    const string Game = "#868E96", Skill = "#4C6EF5", Buff = "#2F9E44", Record = "#E8590C", Raid = "#AE3EC9", Homework = "#0C8599", Party = "#D6336C";

    public static IReadOnlyList<RailGroup> Rail { get; } =
    [
        new("", "", ["표시"]),
        new("게임 설정", Game, ["화면·인식", "표시 화면·창 밖", "내 오버레이"]),
        new("전투기록", Record, ["타임라인"]),
        new("캐릭터 · 스킬", Skill, ["캐릭터", "스킬창", "단심"]),
        new("버프", Buff, ["버프 줄"]),
        new("DPS 미터기", Record, ["DPS 미터기"]),
        new("레이드 · 보스", Raid, ["레이드", "암수"]),
        new("숙제", Homework, ["숙제"]),
        new("파티", Party, ["파티"]),
        new("앱", "", ["즐겨찾기", "키·단축키", "테마·업데이트"]),
    ];
}
