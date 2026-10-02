using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>Decides which key presses of a class that always spends (도화가, 바드) drop the gauge at once. A press is left
/// out when it is the cast the engine sees just after the press, or falls in the skill's own 1 s cooldown; and for a skill
/// with a free recast (바드: within <see cref="GaugeSpend.FreeRecast"/> s of a use, the next use takes nothing), that recast is
/// the one press that spends nothing, whatever the gauge holds. Not thread-safe: one per engine thread.</summary>
public sealed class ImmediateSpend
{
    /// <summary>The skills' own cooldown after a use: a press or cast seen inside it is the same use.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(1);

    readonly Dictionary<SlotId, TimeSpan> _lastBySlot = [];
    TimeSpan? _lastAny, _freeUntil;

    /// <summary>What a press of <paramref name="slot"/> at <paramref name="at"/> leaves of <paramref name="gauge"/>, or null
    /// when the gauge is left to its reads: not an at-once skill, the same use again, the free recast, or nothing full to take.</summary>
    public double? Press(GaugeRule rule, SlotId slot, double gauge, TimeSpan at)
    {
        if (!rule.SpendsAtOnce(slot)) return null;
        double? free = rule.FreeRecast(slot);
        if (_lastBySlot.TryGetValue(slot, out var last) && at - last < Cooldown) return null;
        if (free is not null && _lastAny is { } any && at - any < Cooldown) return null;
        (_lastBySlot[slot], _lastAny) = (at, at);
        if (_freeUntil is { } until && at < until) { _freeUntil = null; return null; }
        if (rule.AfterSpend(slot, gauge) is not { } left) return null;
        _freeUntil = free is { } seconds ? at + Cooldown + TimeSpan.FromSeconds(seconds) : null;   // open after the cooldown
        return left;
    }

    /// <summary>Forgets the last uses (another character).</summary>
    public void Reset()
    {
        _lastBySlot.Clear();
        (_lastAny, _freeUntil) = (null, null);
    }
}
