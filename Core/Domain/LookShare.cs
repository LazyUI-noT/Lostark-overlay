namespace GaugeOverlay.Core.Domain;

/// <summary>What a 모든 배치에 적용 mark says under it (display-split.plan.md, step 4): whether the look is one value for
/// 배치 1–5 or the active 배치's own, and which settings it covers, since a card can mix them with ones that aren't looks.</summary>
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
        // Not 크기 / 막대 길이 / 두께 (the window's size, the 배치's own) or 암수's 타겟 UI rows (global).
        LookGroup.암수 => "암흑수류탄 읽기, 읽기 빈도, 표시 방식, 색, 경고, 불투명도",
        LookGroup.단심 => "단심의 크기를 뺀 모든 설정",
        _ => throw new ArgumentOutOfRangeException(nameof(look), look, null),
    };

    /// <summary>The line under the mark; <paramref name="activeProfile"/> is 0-based.</summary>
    public static string Status(LookGroup look, bool shared, int activeProfile) => shared
        ? $"배치 1–5가 같은 값을 씁니다: {Covers(look)}."
        : $"배치 {activeProfile + 1}의 값입니다. 배치를 바꾸면 함께 바뀝니다: {Covers(look)}.";
}
