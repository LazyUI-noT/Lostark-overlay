using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Cooldown;

/// <summary>Learns which slots are stack skills during play: the count beside a timer on <see cref="Casts"/> separate casts
/// (one misread count can't mark an ordinary skill). Per 직업각인: <see cref="Reset"/> when the character changes.</summary>
public sealed class StackLearner
{
    public const int Casts = 3;

    /// <summary>The tracker restarts one cast's state when its recharge timer follows an unknown slot's "1s" (and on a
    /// resume), so states starting this close together are one cast.</summary>
    public static readonly TimeSpan SameCast = TimeSpan.FromSeconds(3);

    readonly Dictionary<SlotId, List<TimeSpan>> _seen = [];

    /// <summary>Notes reading <paramref name="r"/> of the slot now in <paramref name="state"/> (after the update). True once,
    /// on the read that makes the slot a learned stack skill.</summary>
    public bool Add(Reading r, SlotState state)
    {
        if (!r.Stacked || state.Phase != SlotPhase.Cooldown) return false;
        var casts = _seen.TryGetValue(r.Slot, out var c) ? c : _seen[r.Slot] = [];
        if (casts.Count >= Casts || casts.Any(t => (state.ChangedAt - t).Duration() < SameCast)) return false;
        casts.Add(state.ChangedAt);
        return casts.Count == Casts;
    }

    public void Reset() => _seen.Clear();
}
