namespace GaugeOverlay.Core.Domain;

/// <summary>Who a drag, nudge or Ctrl+arrow on a skill box moves in the 레이아웃 편집기 (user, 2026-11-13): on 내 스킬창 the whole
/// window, unless 칸 하나씩 is ticked (not saved, off each time 설정 opens); 파티원 windows keep moving their boxes.</summary>
public static class BoxDragRule
{
    public static bool MovesBox(bool mineWindow, bool boxesApart) => !mineWindow || boxesApart;
}
