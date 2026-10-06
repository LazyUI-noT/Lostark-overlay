namespace GaugeOverlay.Core.Domain;

/// <summary>레이아웃 편집기의 칸 옵션: what a clicked box is, and its colour and effects written for that box alone
/// (onlyThis, the eight 일반스킬) or for its whole group, which resets the group's boxes set one by one, as the
/// 오버레이 tab's group switches do.</summary>
public static class BoxOptions
{
    /// <summary>The slot and group of a box part; null for the gauge, 변화 and the buff bars.</summary>
    public static (SlotId Slot, SlotGroup Group)? Of(Part part, IReadOnlyCollection<SlotId> adenSlots) =>
        part < Part.변화 ? ((SlotId)part, SlotInfo.GroupOf((SlotId)part, adenSlots)) : null;

    /// <summary>Boxes of this group can each have their own colour and effects.</summary>
    public static bool PerBox(SlotGroup g) => g == SlotGroup.일반스킬;

    public static void SetShine(MineProfile p, SlotId slot, SlotGroup g, bool on, bool onlyThis, IReadOnlyCollection<SlotId> adenSlots) =>
        Set(p.SlotShine, v => { if (v) p.NoShine.Remove(g); else p.NoShine.Add(g); }, slot, g, on, onlyThis, adenSlots);

    public static void SetPulse(MineProfile p, SlotId slot, SlotGroup g, bool on, bool onlyThis, IReadOnlyCollection<SlotId> adenSlots) =>
        Set(p.SlotPulse, v => { if (v) p.Pulse.Add(g); else p.Pulse.Remove(g); }, slot, g, on, onlyThis, adenSlots);

    public static void SetEndFlash(MineProfile p, SlotId slot, SlotGroup g, bool on, bool onlyThis, IReadOnlyCollection<SlotId> adenSlots) =>
        Set(p.SlotEndFlash, v => { if (v) p.EndFlash.Add(g); else p.EndFlash.Remove(g); }, slot, g, on, onlyThis, adenSlots);

    public static void SetReadyCount(MineProfile p, SlotId slot, SlotGroup g, bool on, bool onlyThis, IReadOnlyCollection<SlotId> adenSlots) =>
        Set(p.SlotReadyCount, v => { if (v) p.ReadyCount.Add(g); else p.ReadyCount.Remove(g); }, slot, g, on, onlyThis, adenSlots);

    public static void SetHeldCount(MineProfile p, SlotId slot, SlotGroup g, bool on, bool onlyThis, IReadOnlyCollection<SlotId> adenSlots) =>
        Set(p.SlotHeldCount, v => { if (v) p.HeldCount.Add(g); else p.HeldCount.Remove(g); }, slot, g, on, onlyThis, adenSlots);

    public static void SetHideOnCooldown(MineProfile p, SlotId slot, SlotGroup g, bool on, bool onlyThis, IReadOnlyCollection<SlotId> adenSlots) =>
        Set(p.SlotHideOnCooldown, v => { if (v) p.HideOnCooldown.Add(g); else p.HideOnCooldown.Remove(g); }, slot, g, on, onlyThis, adenSlots);

    public static void SetKeyLetter(MineProfile p, SlotId slot, bool on)
    {
        if (on) p.KeyLetters.Add(slot); else p.KeyLetters.Remove(slot);
    }

    /// <summary>Makes the class's colours 개인 설정 (the others keep what they showed: learned or built in) and sets
    /// the box's, or its group's; a group colour for 일반스킬 turns 일반스킬 모두 같은 색 back on.</summary>
    public static void SetColor(ClassProfile p, SlotId slot, SlotGroup g, string hex, bool onlyThis)
    {
        p.Color = ColorSource.Personal;
        if (onlyThis) (p.SkillsUnified, p.SlotColors[slot]) = (false, hex);
        else
        {
            p.Colors[g] = hex;
            if (PerBox(g)) p.SkillsUnified = true;
        }
    }

    static void Set(Dictionary<SlotId, bool> perBox, Action<bool> setGroup, SlotId slot, SlotGroup g, bool on, bool onlyThis,
        IReadOnlyCollection<SlotId> adenSlots)
    {
        if (onlyThis) { perBox[slot] = on; return; }
        setGroup(on);
        foreach (var id in SlotInfo.All.Where(id => SlotInfo.GroupOf(id, adenSlots) == g)) perBox.Remove(id);
    }
}
