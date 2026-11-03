namespace GaugeOverlay.Core.Domain;

/// <summary>What tray → 표시 turns on and off.</summary>
public enum OverlayPart { Skills, Buffs, Gauge, Judgment, Debuffs, Meter, BarCount, Party, Amsu }

/// <summary>Tray → 표시 and 설정 → 표시: each part through the same setting as its own show switch in 설정 (the character's
/// class for skills and gauge, every buff bar, 단심 for the character, the gates' 디버프 창, the DPS 미터기's window, the main
/// 줄 수 switch, the party windows).</summary>
public static class TrayToggles
{
    public static bool IsShown(Settings s, OverlayPart part) => part switch
    {
        OverlayPart.Skills => Mine(s).Groups.Values.Any(on => on),
        OverlayPart.Buffs => Enum.GetValues<BuffKind>().Any(k => s.Buffs.Of(k).Visible),
        OverlayPart.Gauge => Mine(s).Gauge,
        OverlayPart.Judgment => s.Judgment.On(s.ActiveCharacter),
        OverlayPart.Meter => s.Meter.On && s.Meter.Show,
        OverlayPart.BarCount => s.Raids.CountEnabled,
        OverlayPart.Party => s.Party.ShowMembers,
        OverlayPart.Amsu => s.Amsu.On,
        _ => s.Debuffs.Raid != Raid.없음,   // the raid picked by hand; 디버프 창 자동 is 설정 → 레이드's
    };

    public static void Show(Settings s, OverlayPart part, bool on)
    {
        switch (part)
        {
            case OverlayPart.Skills:
                var groups = EditMine(s).Groups;
                string? shownKey = ClassProfiles.ActiveKey(s);
                if (!on) s.TrayHiddenGroups = (shownKey, [.. Enum.GetValues<SlotGroup>().Where(g => groups.GetValueOrDefault(g, true))]);
                var back = on && s.TrayHiddenGroups is (var k0, { Length: > 0 } hid) && k0 == shownKey ? hid : null;   // what off hid, else all
                foreach (var g in Enum.GetValues<SlotGroup>()) groups[g] = on && (back?.Contains(g) ?? true);
                if (on) s.TrayHiddenGroups = null;
                break;
            case OverlayPart.Buffs:
                if (!on) s.TrayHiddenBars = [.. Enum.GetValues<BuffKind>().Where(k => s.Buffs.Of(k).Visible)];
                var bars = on && s.TrayHiddenBars is { Length: > 0 } shown ? shown : null;
                foreach (var k in Enum.GetValues<BuffKind>()) s.Buffs.Edit(k).Visible = on && (bars?.Contains(k) ?? true);
                if (on) s.TrayHiddenBars = null;
                break;
            case OverlayPart.Gauge:
                EditMine(s).Gauge = on;
                break;
            case OverlayPart.Judgment:
                if (s.ActiveCharacter is { } key) s.Judgment.Set(key, on);
                break;
            case OverlayPart.Meter:   // off hides the window only: the meter keeps reading for 전투 기록
                if (on) s.Meter.On = true;
                s.Meter.Show = on;
                break;
            case OverlayPart.BarCount:   // the main switch: each gate's own 줄 수 창 stays as set
                s.Raids.CountEnabled = on;
                break;
            case OverlayPart.Party:
                s.Party.ShowMembers = on;
                break;
            case OverlayPart.Amsu:
                s.Amsu.On = on;
                break;
            default:
                // The raid shown by hand in any dungeon. ponytail: 모르둠 is the only raid with debuffs; a second one gets its
                // own tray entry (a raid per entry) instead of this one switch.
                s.Debuffs.Raid = on ? Raid.모르둠 : Raid.없음;
                break;
        }
    }

    static MineProfile Mine(Settings s) => ClassProfiles.Of(s, ClassProfiles.ActiveKey(s)).Mine;
    static MineProfile EditMine(Settings s) => ClassProfiles.Edit(s, ClassProfiles.ActiveKey(s)).Mine;
}
