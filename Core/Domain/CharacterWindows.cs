namespace GaugeOverlay.Core.Domain;

/// <summary>One character's hidden windows: the gold part of their switch on the 표시 map. Read for everyone either way.</summary>
public sealed class CharacterWindows
{
    public HashSet<BuffKind> Buffs { get; set; } = [];
    public bool Meter { get; set; }
}
