using System.Windows;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The gauge under 합쳐서 / 따로: its own window (<see cref="GaugeOnly"/>, yours, only the bar, flat or upright) and the
/// plate's account of where the gauge would be drawn (<see cref="GaugeHome"/>).</summary>
public sealed partial class OverlayRenderer
{
    /// <summary>The gauge's own window under 따로: yours, only the bar, at the size <see cref="Settings.Gauge"/> gives it.</summary>
    public bool GaugeOnly { get; set; }

    /// <summary>The gauge bar stands upright and fills from the bottom: only in its own window, and only when set.</summary>
    bool GaugeUpright => GaugeOnly && Settings.Gauge.Upright;

    /// <summary>Your skill window with the gauge taken out of it (따로): <see cref="LayoutPlayer"/> leaves the strip out.</summary>
    bool GaugeApart => Peer < 0 && !BuffsOnly && !GaugeOnly && Settings.Apart;

    /// <summary>Where the gauge was drawn on the plate before it left it (this element's coordinates), or null when the plate
    /// draws none (no gauge for this class, or nothing to place by): where the gauge window first stands.</summary>
    public Rect? GaugeHome => _layout?.GaugeHome;

    /// <summary>That spot's size as the Width/Height scales of the gauge window, so it first has the size it was drawn at.</summary>
    public (double W, double H)? GaugeHomeScales => _layout is { GaugeHome: { } h } l ? SplitLayout.HomeScales(h.Width, l.Unit) : null;

    /// <summary>The gauge window: the bar (length × thickness, swapped when upright) inside the usual bleed, nothing else.</summary>
    PlayerLayout? LayoutGauge(Player p)
    {
        var mine = Target is { } t ? ClassProfiles.Mine(Settings, t.Key) : ClassProfiles.Mine(Settings);
        if (!mine.Gauge || p.Snapshot.Gauge is null) return null;
        var (w, h) = SplitLayout.GaugeSize(Settings.Gauge);
        var bar = new Rect(Bleed, Bleed, w, h);
        return new PlayerLayout(p, "gauge", BoxSize(Settings.Gauge.Height))
        {
            Plate = bar, Gauge = bar, Anchor = new Point(Bleed, Bleed), CellOrigin = new Point(Bleed, Bleed), Scale = (1, 1), Bar = ResolveBar(p),
        };
    }

    /// <summary>Your gauge bar's splits, colours and glow for the 직업각인 drawn (the picked one in 설정's canvas, else the active character's).</summary>
    GaugeBar? ResolveBar(Player p)
    {
        var (barClass, barSub) = Target is { } picked ? (picked.Class, picked.Subclass)
            : Settings.ActiveCharacter is { } k && Settings.Characters.TryGetValue(k, out var c) ? (c.Class, c.Subclass) : default;
        return barClass is null ? null
            : GaugeBar.Resolve(Settings, barClass, barSub!, Emblems.Class(p.Snapshot.ClassId)?.Subclasses.GetValueOrDefault(barSub!));
    }
}
