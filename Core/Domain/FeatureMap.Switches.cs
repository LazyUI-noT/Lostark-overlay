namespace GaugeOverlay.Core.Domain;

/// <summary>The map's switches: each box part (<see cref="MapBox.Choices"/>) read from and written to the setting behind it.</summary>
public static partial class FeatureMap
{
    /// <summary>The choice shown on <paramref name="part"/> of box <paramref name="id"/>; null for a part with no setting yet
    /// (던전 · 캐릭터 인식, 그림자, the timeline's inner boxes, 숙제: later phases).</summary>
    public static int? Selected(Settings s, string id, int part) => (id, part) switch
    {
        ("skill", 0) => Bit(s.Overlay.SkillRead),
        ("cls", 0) => (int)s.ClassDetect,
        ("aden", 1) => Shown(TrayToggles.IsShown(s, OverlayPart.Gauge)),
        ("judg", 0) => Bit(s.Judgment.On(s.ActiveCharacter)),
        ("buff", 0) => Bit(AnyBar(s)),
        ("buff", 1) => Shown(TrayToggles.IsShown(s, OverlayPart.Buffs)),
        ("g-jeonbun", 0) => Bit(s.Meter.On),
        ("boss", 0) => Bit(s.Raids.BossHpRead),
        ("boss", 1) => Shown(s.Raids.BossHpEasy),
        ("amsu", 0) => Bit(s.Amsu.On),
        ("meter", 0) => (int)s.Meter.Mode(),
        ("cover", 0) => (int)s.Meter.BuffShare(),
        ("skillview", 0) => Shown(TrayToggles.IsShown(s, OverlayPart.Skills)),
        ("count", 0) => Bit(s.Raids.CountEnabled),
        ("debuff", 0) => Bit(s.Raids.DebuffsAuto),
        ("peers", 0) => Shown(s.Party.ShowMembers),
        ("rec", 0) => Bit(s.Meter.On && s.Meter.Record),
        _ => null,
    };

    /// <summary>Sets <paramref name="part"/> of box <paramref name="id"/> to choice <paramref name="index"/>. Turning on what
    /// needs a source that is off turns the source on without its window and says so (the returned note, for a toast).</summary>
    public static string? Choose(Settings s, string id, int part, int index)
    {
        bool on = index == 1, shown = index == 0;
        switch (id, part)
        {
            case ("skill", 0): s.Overlay.SkillRead = on; break;
            case ("cls", 0): s.ClassDetect = (ClassDetectMode)index; break;
            case ("aden", 1): TrayToggles.Show(s, OverlayPart.Gauge, shown); break;
            case ("judg", 0): if (s.ActiveCharacter is { } key) s.Judgment.Set(key, on); break;
            case ("buff", 0): foreach (var k in Enum.GetValues<BuffKind>()) s.Buffs.Edit(k).Visible = on; break;
            case ("buff", 1): TrayToggles.Show(s, OverlayPart.Buffs, shown); break;
            case ("g-jeonbun", 0): if (!on) s.Meter.SetMode(ThreeWay.끄기); else if (!s.Meter.On) s.Meter.SetMode(ThreeWay.표시안함); break;
            case ("boss", 0): s.Raids.BossHpRead = on; break;
            case ("boss", 1): s.Raids.BossHpEasy = shown; break;
            case ("amsu", 0): s.Amsu.On = on; break;
            case ("meter", 0): s.Meter.SetMode((ThreeWay)index); break;
            case ("cover", 0): s.Meter.SetBuffWish((ThreeWay)index); break;
            case ("skillview", 0): TrayToggles.Show(s, OverlayPart.Skills, shown); break;
            case ("count", 0): s.Raids.CountEnabled = on; break;
            case ("debuff", 0): s.Raids.DebuffsAuto = on; break;
            case ("peers", 0): s.Party.ShowMembers = shown; break;
            case ("rec", 0):
                s.Meter.Record = on;
                if (on && !s.Meter.On) { s.Meter.SetMode(ThreeWay.표시안함); return "타임라인에 쓰려고 DPS 미터기를 표시 안 함으로 켰습니다."; }
                break;
        }
        return null;
    }

    /// <summary>The 설정 page a box's ⚙ opens (an area of <see cref="Rail"/>).</summary>
    public static string Page(string id) => id switch
    {
        "skill" or "skillview" => "스킬창",
        "cls" => "직업 인식",
        "aden" => "내 오버레이",
        "judg" => "단심",
        "buff" or "cover" or "buffrows" => "버프 줄",
        "g-jeonbun" or "meter" => "DPS 미터기",
        "boss" or "count" or "debuff" or "shadow" => "레이드",
        "amsu" => "암수",
        "party" or "peers" => "파티 공유",
        "hw" or "char" => "레이드 숙제",
        "rec" or "icons" or "amsuline" or "zone" or "dungeon" => "타임라인",
        _ => "화면·인식",
    };

    static int Bit(bool on) => on ? 1 : 0;
    static int Shown(bool shown) => shown ? 0 : 1;
    static bool AnyBar(Settings s) => Enum.GetValues<BuffKind>().Any(k => s.Buffs.Of(k).Visible);
}
