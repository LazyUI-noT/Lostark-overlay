using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The tint the game draws over the skill bar while the character is dead or disabled (침묵, held), and in a town: grey
/// plus one fixed red offset on every pixel of every icon (dead-disabled.plan.md). Read only to leave that time out of 버프 적용률
/// and to save a death as 실패; cooldown detection never uses it (.claude/rules/detection.md). The colours are the game's own,
/// not one character's, and the crops are the 일반 slots' (Geometry), so resolution, 21:9 and HUD 크기 are theirs.</summary>
public static class SkillTint
{
    // A tinted pixel: R − G ≈ 30 and R − B ≈ 25 whatever the icon (measured means 24–30 on greyed out.png, dead.PNG, dead2.PNG
    // and the long 종탑 video).
    const int MinRedOverGreen = 14, MaxRedOverGreen = 40, MinRedOverBlue = 8, MaxRedOverBlue = 36;
    // Pixels that tell nothing are not counted: the cooldown sweep and the slot's dark ground (luma under Dark), and the digits,
    // key letter and arrows (every channel over White).
    const float Dark = 40;
    const int White = 200;
    // Measured (SkillTintTests): a tinted slot 0.58 and up, a cooling tinted one 0.62; ready, cooling, 마나 부족 and cast
    // flashes 0.19 at most. Six slots: two may be unread or covered.
    const double MinShare = 0.5;
    const int MinSlots = 6;

    /// <summary>The share of a slot crop's telling pixels that carry the tint (0…1); NaN when it has none (unsure).</summary>
    public static double Share(Image slot)
    {
        int counted = 0, tinted = 0;
        for (int i = 0; i < slot.Bgra.Length; i += 4)
        {
            int b = slot.Bgra[i], g = slot.Bgra[i + 1], r = slot.Bgra[i + 2];
            if (ImageOps.LumaPerceptual(slot.Bgra, i) < Dark || Math.Min(b, Math.Min(g, r)) > White) continue;
            counted++;
            if (r - g is >= MinRedOverGreen and <= MaxRedOverGreen && r - b is >= MinRedOverBlue and <= MaxRedOverBlue) tinted++;
        }
        return counted == 0 ? double.NaN : (double)tinted / counted;
    }

    /// <summary>The bar is tinted: enough of the 일반 slots' last <see cref="Share"/>s say so. NaN (not read, unsure) counts as
    /// not. The caller adds what the tint alone can't tell: the skill bar on screen, and no town (a 전분 record running).</summary>
    public static bool On(IReadOnlyList<double> shares) => shares.Count(s => s >= MinShare) >= MinSlots;
}
