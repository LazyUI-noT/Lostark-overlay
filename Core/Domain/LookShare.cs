namespace GaugeOverlay.Core.Domain;

/// <summary>What 복사 says about a look (user, 2026-11-13): its name and which settings it covers (a card can mix them with
/// ones that aren't looks).</summary>
public static class LookShare
{
    /// <summary>The settings <paramref name="look"/> covers, as the 설정 rows call them.</summary>
    public static string Covers(LookGroup look) => look switch
    {
        LookGroup.판 => "배경판, 배경판 불투명도, 배경판 색",
        LookGroup.스킬칸 => "칸 불투명도, 초각스 색, 금테, 경과 초 색 (스킬 칸 설정)",
        LookGroup.게이지 => "게이지 색, 두께, 불투명도",
        LookGroup.버프막대 => "움직임, 비었을 때, 막대마다 색 · 남은 초 · 깜빡임",
        LookGroup.미터기 => "표시 방식, DPS 그래프, 색, 배경, 불투명도, 버프 적용률 막대 불투명도",
        // Not 크기 / 막대 길이 / 두께 (the window's size, the profile's own) or 암수's 타겟 UI rows (global).
        LookGroup.암수 => "암흑수류탄 읽기, 읽기 빈도, 표시 방식, 색, 경고, 불투명도",
        LookGroup.단심 => "단심의 크기를 뺀 모든 설정",
        LookGroup.디버프 => "비었을 때 숨기기",
        LookGroup.줄수창 => "줄 수 읽기, 빛나기, 빛날 때만 표시, 빛나는 동안, 줄 수 창 배경 색, 불투명도",
        _ => throw new ArgumentOutOfRangeException(nameof(look), look, null),
    };

    /// <summary>The look's name as the 설정 pages call it (복사할 것).</summary>
    public static string Name(LookGroup look) => look switch
    {
        LookGroup.판 => "배경판",
        LookGroup.스킬칸 => "스킬 칸",
        LookGroup.게이지 => "게이지",
        LookGroup.버프막대 => "버프 막대",
        LookGroup.미터기 => "DPS 미터기",
        LookGroup.암수 => "암수",
        LookGroup.단심 => "단심",
        LookGroup.디버프 => "디버프",
        LookGroup.줄수창 => "줄 수 창",
        _ => throw new ArgumentOutOfRangeException(nameof(look), look, null),
    };
}
