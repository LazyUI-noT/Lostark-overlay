namespace GaugeOverlay.Core.Domain;

/// <summary>A gate the boss-name read (OCR) recognised, tied to the boss's max HP read with it. While each read of the max HP
/// equals it, the gate is kept and the name is read only now and then to check it; a different max HP (next boss or phase)
/// or a name read of another gate drops it. Covers bosses whose HP raids.json doesn't hold (RaidData.ByHp knows those).</summary>
public sealed record HpAnchor(string Gate, long MaxHp)
{
    /// <summary>The anchored gate when <paramref name="maxHp"/> is its max HP, else null.</summary>
    public static string? Match(HpAnchor? anchor, long? maxHp) =>
        anchor is not null && maxHp == anchor.MaxHp ? anchor.Gate : null;

    /// <summary>The anchor after a name step. A name read of another gate drops it. A name read that recognition has confirmed
    /// (<paramref name="recognised"/>) anchors the max HP once it read the same twice in a row (<paramref name="lastMaxHp"/>:
    /// one misread digit isn't kept). An unreadable HP keeps the anchor; any other max HP drops it.</summary>
    public static HpAnchor? Next(HpAnchor? anchor, string? recognised, string? ocrGate, long? maxHp, long? lastMaxHp)
    {
        var kept = ocrGate is not null && anchor is not null && ocrGate != anchor.Gate ? null : anchor;
        if (maxHp is not { } hp) return kept;
        if (ocrGate is not null && ocrGate == recognised && hp == lastMaxHp) return new(ocrGate, hp);
        return Match(kept, hp) is null ? null : kept;
    }
}
