namespace GaugeOverlay.Core.Domain;

/// <summary>What tray → 표시 turns on and off.</summary>
public enum OverlayPart { Skills, Buffs, Gauge, Judgment, Debuffs }

/// <summary>Tray → 표시: each part through the same setting as its own show switch in 설정 (the character's class for
/// skills and gauge, every buff bar, 단심 for the character, the debuff raid).</summary>
public static class TrayToggles
{
    public static bool IsShown(Settings s, OverlayPart part) => part switch
    {
        OverlayPart.Skills => Mine(s).Groups.Values.Any(on => on),
        OverlayPart.Buffs => Enum.GetValues<BuffKind>().Any(k => s.Buffs.Of(k).Visible),
        OverlayPart.Gauge => Mine(s).Gauge,
        OverlayPart.Judgment => s.Judgment.On(s.ActiveCharacter),
        _ => s.Debuffs.Raid != Raid.없음,
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
            default:
                // ponytail: G3 모르둠 is the only raid; remember the last one when project B adds more.
                s.Debuffs.Raid = on ? Raid.모르둠 : Raid.없음;
                break;
        }
    }

    static MineProfile Mine(Settings s) => ClassProfiles.Of(s, ClassProfiles.ActiveKey(s)).Mine;
    static MineProfile EditMine(Settings s) => ClassProfiles.Edit(s, ClassProfiles.ActiveKey(s)).Mine;
}
