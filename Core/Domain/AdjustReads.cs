using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>What the adjust layer draws green, per part: true reads well now; false nothing seen (drawn plain: a row with no buffs
/// up is not wrong); null not read at all (that reading is off).</summary>
public sealed record AdjustReads(bool Bar, bool? Buff, bool? Debuff, bool? Boss, bool? Panel = null)
{
    /// <summary>Whether <paramref name="part"/> reads (null: not read at all).</summary>
    public bool? Of(AdjustPart part) => part switch { AdjustPart.스킬창 => Bar, AdjustPart.버프 => Buff, AdjustPart.디버프 => Debuff, AdjustPart.전분 => Panel, _ => Boss };

    /// <param name="barScore"><see cref="FitSolver.Score"/> of the bar at the fit shown.</param>
    /// <param name="buffs">The buff row's states, null when it isn't read.</param>
    /// <param name="debuffs">The debuff and boss rows' status, null when they aren't read (no raid picked).</param>
    /// <param name="panel">Whether the 전분 box holds the panel (<see cref="PictureReads.Panel"/>), null when it wasn't captured.</param>
    public static AdjustReads From(double barScore, IReadOnlyList<BuffState>? buffs, DebuffStatus? debuffs, bool? panel = null) => new(
        FitSolver.ReadsWell(barScore),
        buffs?.Any(b => b.Active && !b.Estimated),   // one carried on its estimate over missed reads isn't being read
        debuffs is null ? null : debuffs.Blue is not null || debuffs.Red is not null || debuffs.Lightning is not null || debuffs.Swap is not null || debuffs.Duelist is not null,
        debuffs is null ? null : debuffs.BossRed is not null || debuffs.BossBlue is not null,
        panel);
}
