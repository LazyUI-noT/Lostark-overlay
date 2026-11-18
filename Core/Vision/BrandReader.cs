using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The supports whose 낙인 (a debuff on the boss: the party deals more to it) has its own icon; 홀리나이트 and 발키리 share one.</summary>
public enum BrandIcon { 바드, 도화가, 홀나발키리 }

/// <summary>낙인 in the boss debuff row (Geometry.BossDebuffStrip), read as the bombs are (AmsuReader: the walk over the red-framed
/// debuffs, then one match per filled slot) against the three 낙인 icons. Icon only: no "N초" is read (user, 2026-11-15). Pure.</summary>
public static class BrandReader
{
    // Measured on nakin_dohwa / nakin_holy / nakin_bard and the 빛암 rows (BrandReaderTests): the 낙인 score 0.83–0.92 on
    // their slots, every other icon 0.41 at most (the gold shield beside them included). Near the middle, for blurrier frames.
    internal const double MinBrand = 0.65;

    static readonly AmsuReader.IconSet Icons = new(BrandTemplates.Icons, MinBrand);

    /// <summary>The icon as matched (the settings and demo may draw it).</summary>
    public static Image? IconOf(BrandIcon b) => Icons.Templates[(int)b];

    /// <summary>The 낙인 icon on line <paramref name="rowY"/> (strip px) of <paramref name="r"/> (the strip at reference size,
    /// <see cref="DebuffReader.ToReference"/>), null when none of the filled slots shows one.</summary>
    public static BrandIcon? Find(Image r, double scale, BossSlots slots, int rowY) =>
        FindIn(r, scale, [.. AmsuReader.WalkIn(r, scale, slots, rowY).Filled.Take(slots.Reads)], rowY);

    /// <summary><see cref="Find"/> over a walk already made (<see cref="BrandWatch"/> also counts its filled slots), through
    /// <paramref name="gate"/> when given (<see cref="Gate"/>: a slot is searched only when its icon changed). Stops at the
    /// first 낙인: the slots after it are not asked (one 낙인 per row, multiple 낙인 out: HANDOFF 3b).</summary>
    internal static BrandIcon? FindIn(Image r, double scale, IReadOnlyList<AmsuReader.SlotAt> filled, int rowY, IconGate<BrandIcon?>? gate = null) =>
        FindSlot(r, scale, filled, rowY, gate)?.Icon;

    /// <summary><see cref="FindIn"/> with the slot the icon was found in (its box is where the "N초" under it is read).</summary>
    internal static (BrandIcon Icon, AmsuReader.SlotAt Slot)? FindSlot(Image r, double scale, IReadOnlyList<AmsuReader.SlotAt> filled, int rowY,
        IconGate<BrandIcon?>? gate = null)
    {
        int y = (int)Math.Round(rowY / scale);
        for (int i = 0; i < filled.Count; i++)
        {
            var box = new PixelRect((int)Math.Round(filled[i].Icon.X / scale), y, DebuffReader.BossIconSize, DebuffReader.BossIconSize);
            if ((gate is null ? At(r, box.X, box.Y) : gate.Classify(r, i, box)) is { } hit) return (hit, filled[i]);
        }
        return null;
    }

    /// <summary>One gate per filled slot, keyed by the slot's icon box in reference px (HANDOFF 3d): the 3 shifts and the ±2
    /// search run only for a slot whose thumbnail moved past <see cref="IconGate.Threshold"/>. Its owner resets it when the
    /// row shifts (another filled count), its line moves or the layout changes.</summary>
    internal static IconGate<BrandIcon?> Gate() => new((r, box) => At(r, box.X, box.Y));

    /// <summary>The 낙인 icon whose box starts at (<paramref name="x"/>, <paramref name="y"/>) in reference px, if any.</summary>
    internal static BrandIcon? At(Image r, int x, int y)
    {
        (int Kind, double Score, int X)? best = null;
        foreach (int d in Shifts)
            if (AmsuReader.Around(Icons, r, x + d, y) is { } a && (best is null || a.Score > best.Value.Score)) best = a;
        return best is { } b ? (BrandIcon)b.Kind : null;
    }

    // The walk's slot can sit up to 6 px off the frame (nakin_holy's 4th: 5 at 보스 디버프 100); Around looks ±2, so three looks
    // cover ±6. Kept here, not in the walk, so 암수 reads as before.
    static readonly int[] Shifts = [-4, 0, 4];

    internal static string Debug(Image r, int x, int y)
    {
        var parts = new List<string>();
        for (int k = 0; k < 3; k++)
        {
            var one = new AmsuReader.IconSet([.. BrandTemplates.Icons.Select((t, i) => i == k ? t : null)], -1);
            var best = (Score: -2.0, Hue: 0.0);
            for (int dx = -2; dx <= 2; dx++)
            {
                var box = BuffReader.Interior(new PixelRect(x + dx, y, DebuffReader.BossIconSize, DebuffReader.BossIconSize));
                var (sc, h, _) = BuffReader.Match(r, box, one.Thumbs, one.Hues, []);
                if (sc[0] > best.Score) best = (sc[0], h[0]);
            }
            parts.Add($"{(BrandIcon)k} {best.Score:0.00}/h{best.Hue:0}");
        }
        return string.Join("  ", parts);
    }
}
