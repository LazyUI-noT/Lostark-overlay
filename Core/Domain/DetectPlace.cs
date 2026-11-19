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
    public static readonly string[] Movable = ["skill", "buff", DebuffRow, "amsu", "g-jeonbun"];

    /// <summary>The 기능 맵's read chips (rail-split.plan.md 4): every box on 화면 인식 설정, in this order, with a short name.</summary>
    public static readonly (string Id, string Short)[] Chips =
    [
        ("skill", "스킬창"), ("buff", "버프"), (DebuffRow, "디버프"), ("aden", "아덴"), ("judg", "단심"), ("char", "캐릭터"),
        ("g-jeonbun", "전분"), ("boss", "보스 HP"), ("amsu", "암수"), ("dungeon", "콘텐츠"),
    ];

    /// <summary>What a 화면 인식 설정 chip's card shows around box <paramref name="area"/>: a quarter of its long side (8 px or more) on
    /// each side, cut to the game screen when one is known (user, 2026-11-19: a box not read yet still shows where it reads).</summary>
    public static PixelRect CardView(PixelRect area, Viewport view)
    {
        int pad = Math.Max(8, Math.Max(area.Width, area.Height) / 4);
        var around = new PixelRect(area.X - pad, area.Y - pad, area.Width + 2 * pad, area.Height + 2 * pad);
        return view.Width > 0 && view.Height > 0 && around.Intersect(new PixelRect(view.X, view.Y, view.Width, view.Height)) is { } cut ? cut : around;
    }

    /// <summary>The <see cref="Chips"/> a 기능 맵 box reads through: itself and every box upstream of it along the map's
    /// non-party edges (user, 2026-11-19: hovering a box lights its chips).</summary>
    public static IReadOnlySet<string> ChipsBehind(string id)
    {
        var seen = new HashSet<string>();
        var todo = new Stack<string>([id]);
        while (todo.TryPop(out var at))
        {
            if (!seen.Add(at)) continue;
            foreach (var e in FeatureMap.Edges.Where(e => e.To == at && !e.Party)) todo.Push(e.From);
        }
        return Chips.Select(c => c.Id).Where(seen.Contains).ToHashSet();
    }

    /// <summary>The table's row groups.</summary>
    public const string Slots = "슬롯", Buffs = "버프", Debuffs = "디버프", Boss = "보스";

    /// <summary>The part of 위치 조정 that moves box <paramref name="id"/>; null: nothing moves it (아덴 and the class check follow
    /// 스킬창, 보스 체력 / 던전 / 메뉴 are placed by resolution and HUD, 단심 by 데미지 폰트).</summary>
    public static AdjustPart? PartOf(string id) => id switch
    {
        "skill" => AdjustPart.스킬창,
        "buff" => AdjustPart.버프,
        DebuffRow => AdjustPart.디버프,
        "amsu" => AdjustPart.보스,   // the boss row's debuff strip follows its shift
        "g-jeonbun" => AdjustPart.전분,
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
