namespace GaugeOverlay.Core.Domain;

/// <summary>A 브레이커 skill's side: a 기력 skill fills the green 충격 half, a 충격 skill spends it.</summary>
public enum RingSide { 기력, 충격 }

/// <summary>When 수라의길's cue outline shows (user, 2026-11-17): always, only during and after Z, or never.</summary>
public enum RingGlow { 항상, Z후, 끄기 }

/// <summary>수라의길's cue on the 기력·충격 창: <see cref="Show"/> is the side opposite <see cref="Last"/>, the last 기력 or 충격
/// skill (null: none seen yet, the whole bar). <see cref="AfterZ"/>: from Z until the next skill. Immutable.</summary>
public sealed record RingCue(RingSide? Last, bool AfterZ = false)
{
    public RingSide? Show => Last switch { RingSide.기력 => RingSide.충격, RingSide.충격 => RingSide.기력, _ => null };

    /// <summary>Only 수라의길 gets the cue (<paramref name="character"/> "클래스 · 직업각인").</summary>
    /// <remarks>ponytail: one 직업각인, so a constant; data in classes.json when a second one wants a cue.</remarks>
    public static bool For(string? character) => character == "브레이커 · 수라의길";

    /// <summary>Whether the outline is drawn under <paramref name="glow"/>.</summary>
    public bool Drawn(RingGlow glow) => glow == RingGlow.항상 || glow == RingGlow.Z후 && AfterZ;
}

/// <summary>Follows 수라의길's cue from what each skill spends (user, 2026-11-18): a 기력 skill drops the yellow (기력) half, a
/// 충격 skill the green (충격) half. Rises say nothing: both halves refill slowly on their own and Z fills both. Both dropping in
/// one read is the ring covered (Z's flash). A 일반 or 초각스 skill's side is learned from the drop that follows its cast, so a
/// learned skill moves the cue on its cast. The latest drop wins, so a moved skill relearns itself. Z lights the cue until the
/// next skill: a cast, or a drop after Z's rapid phase. Mutable entity, one per engine; pure (no clock).</summary>
public sealed class RingTrack
{
    /// <summary>A change smaller than this is read noise.</summary>
    public const double Step = 0.03;
    /// <summary>A drop this soon after a 일반 cast is that skill's.</summary>
    public static readonly TimeSpan CastWindow = TimeSpan.FromSeconds(2);
    /// <summary>Z's rapid phase (~5 s): changes in it are Z's hits, not a skill.</summary>
    public static readonly TimeSpan ZPhase = TimeSpan.FromSeconds(6);
    /// <summary>Z's flash can cover the halves one read apart before Z fills both, and its cast is seen up to a second after:
    /// drops this close to each other, the refill and Z's cast, unless the skill just used explains them, were Z's and are
    /// undone. Measured 0.1–0.7 s in raids (2026-11-18).</summary>
    public static readonly TimeSpan ZLead = TimeSpan.FromSeconds(1);

    // ponytail: kept in memory, relearned each session in a few casts; save per character if the first casts matter.
    readonly Dictionary<SlotId, RingSide> _sides = [];
    (SlotId Slot, TimeSpan At)? _pending;
    double? _yellow, _green;   // where each half was; a change under Step leaves it, so a slow drain still counts
    TimeSpan? _zAt;
    // The last run of drops that may be Z's flash, with what its first replaced, and whether a refill followed, so Z's cast
    // can undo it.
    (TimeSpan At, RingCue? Cue, SlotId? Slot, RingSide? Side, bool Refilled)? _run;

    public RingCue? Cue { get; private set; }

    public RingSide? Learned(SlotId slot) => _sides.TryGetValue(slot, out var s) ? s : null;

    /// <summary>A loading screen or another class: the cue and the baselines go, learned sides stay.</summary>
    public void Clear() => (Cue, _pending, _yellow, _green, _zAt, _run) = (null, null, null, null, null, null);

    /// <summary>A slot went on cooldown; <paramref name="z"/> for 아덴 (Z). Returns whether the cue changed.</summary>
    public bool Cast(SlotId slot, bool z, TimeSpan t)
    {
        var was = Cue;
        if (z)
        {
            if (_zAt is not { } z0 || t - z0 >= ZPhase) _zAt = t;   // one Z is signalled twice (drain and slot): the phase starts at the first
            // Z ends with its refill: drops nothing refilled were a skill's.
            if (_run is { Refilled: true } r && t - r.At <= ZLead) Undo(r);
            _pending = null;
            Cue = (Cue ?? new RingCue(null)) with { AfterZ = true };
        }
        else
        {
            (_zAt, _run) = (null, null);
            // 초각스 has a side (user, 2026-11-18: 천왕지무 기력, the other 충격), learned as a 일반 skill's; 각성기 none, it only
            // ends Z's glow. Any cast ends the wait of the one before, so a green change teaches only the skill that made it.
            bool counts = slot is SlotId.초각스 or >= SlotId.일반1 and <= SlotId.일반8;
            _pending = counts ? (slot, t) : null;
            Cue = counts && Learned(slot) is { } side ? new RingCue(side) : Cue is null ? null : Cue with { AfterZ = false };
        }
        return Cue != was;
    }

    /// <summary>A read of both halves, yellow (기력) and green (충격), 0–1. Returns whether the cue changed.</summary>
    public bool Read(double yellow, double green, TimeSpan t)
    {
        if (_yellow is not { } y0 || _green is not { } g0)
        {
            (_yellow, _green) = (yellow, green);
            return false;
        }
        bool refill = yellow - y0 >= Step && green - g0 >= Step;   // Z fills both; each half alone refills slowly
        if (_zAt is { } z && t - z < ZPhase)
        {
            (_yellow, _green) = (yellow, green);
            return refill && _run is { } zr && t - zr.At <= ZLead && UndoForZ(zr);   // Z's cast seen between its flash and its refill
        }
        bool yMoved = Math.Abs(yellow - y0) >= Step, gMoved = Math.Abs(green - g0) >= Step;
        if (yMoved) _yellow = yellow;
        if (gMoved) _green = green;
        bool yDrop = yMoved && yellow < y0, gDrop = gMoved && green < g0;
        if (yDrop == gDrop)   // nothing spent: a refill, noise, or both covered at once
        {
            if (refill && _run is { } r && t - r.At <= ZLead) _run = r with { At = t, Refilled = true };
            return false;
        }
        var side = yDrop ? RingSide.기력 : RingSide.충격;
        var was = Cue;
        var learns = _pending is { } q && t - q.At <= CastWindow ? q.Slot : (SlotId?)null;
        var before = learns is { } l ? Learned(l) : null;
        // Only a drop no skill explains, or one against the skill's known side, may be Z's flash (user, 2026-11-18: a 기력
        // skill then Z right away lost its 기력): the run of such drops keeps the state from before its first.
        bool maybeZ = learns is null || before is { } known && known != side;
        _run = !maybeZ ? null
            : _run is { } r0 && t - r0.At <= ZLead ? r0 with { At = t, Refilled = false }
            : (t, was, learns, before, false);
        if (learns is { } slot) (_sides[slot], _pending) = (side, null);
        (Cue, _zAt) = (new RingCue(side), null);
        return Cue != was;
    }

    bool UndoForZ((TimeSpan At, RingCue? Cue, SlotId? Slot, RingSide? Side, bool Refilled) r)
    {
        var was = Cue;
        Undo(r);
        Cue = (Cue ?? new RingCue(null)) with { AfterZ = true };
        return Cue != was;
    }

    void Undo((TimeSpan At, RingCue? Cue, SlotId? Slot, RingSide? Side, bool Refilled) r)
    {
        Cue = r.Cue;
        if (r.Slot is { } slot)
        {
            if (r.Side is { } s) _sides[slot] = s;
            else _sides.Remove(slot);
        }
        _run = null;
    }
}
