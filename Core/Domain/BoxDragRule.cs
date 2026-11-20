namespace GaugeOverlay.Core.Domain;

/// <summary>Who a drag, nudge or Ctrl+arrow on a skill box moves in the 레이아웃 편집기 (user, 2026-11-13): on 내 스킬창 the whole
/// window, unless 배치 is 자유 배치 (slot-layout.plan.md §5; it replaced the 칸 하나씩 tick); 파티원 windows keep moving their boxes.</summary>
public static class BoxDragRule
{
    /// <param name="mineWindow">내 스킬창 or the 따로 buff window: whole in 칸 맞춤, box by box in 자유 배치.</param>
    public static bool MovesBox(bool mineWindow, bool free) => !mineWindow || free;
}
