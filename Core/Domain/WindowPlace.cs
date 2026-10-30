namespace GaugeOverlay.Core.Domain;

/// <summary>Where a window sits and the size of its boxes: yours (<see cref="OverlaySettings"/>), and a party member's
/// (the slot's spot, <see cref="PeerDisplay"/>, with the size for their class, <see cref="MemberProfile"/>). Left/Top NaN =
/// the default spot; Width/Height are box scales, 0.3 – 2.0.</summary>
public interface IWindowPlace
{
    double Left { get; set; }
    double Top { get; set; }
    double Width { get; set; }
    double Height { get; set; }
}
