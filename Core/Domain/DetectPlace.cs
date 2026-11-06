using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>The 감지 page's boxes seen as 위치 조정's parts: which box moves with which <see cref="AdjustPart"/> (only these can
/// be placed there), and which rows of the read-out table a picked box shows.</summary>
public static class DetectPlace
{
    /// <summary>Your debuff row: it has no 표시 map box (the map's 디버프 창 is a window), but 위치 조정 moves it and the table reads it.</summary>
    public const string DebuffRow = "debuff-row";
    public const string DebuffRowTitle = "디버프 줄";

    /// <summary>The boxes 위치 조정 moves, in <see cref="AdjustPart"/> order.</summary>
    public static readonly string[] Movable = ["skill", "buff", DebuffRow, "amsu"];

    /// <summary>The table's row groups.</summary>
    public const string Slots = "슬롯", Buffs = "버프", Debuffs = "디버프", Boss = "보스";

    /// <summary>The part of 위치 조정 that moves box <paramref name="id"/>; null: nothing moves it (아덴 and the class check follow
    /// 스킬창, 인게임 전분 finds its own place, 보스 체력 / 던전 / 메뉴 are placed by resolution and HUD, 단심 by 데미지 폰트).</summary>
    public static AdjustPart? PartOf(string id) => id switch
    {
        "skill" => AdjustPart.스킬창,
        "buff" => AdjustPart.버프,
        DebuffRow => AdjustPart.디버프,
        "amsu" => AdjustPart.보스,   // the boss row's debuff strip follows its shift
        _ => null,
    };

    /// <summary>The group of table rows box <paramref name="id"/> shows under the screen; null: it has none.</summary>
    public static string? TableOf(string id) => id switch
    {
        "skill" => Slots,
        "buff" => Buffs,
        DebuffRow => Debuffs,
        "amsu" => Boss,
        _ => null,
    };
}
