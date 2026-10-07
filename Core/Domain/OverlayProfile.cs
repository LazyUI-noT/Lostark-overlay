using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>A window's spot (Left/Top in DIP, NaN = the default spot) and box scale.</summary>
public sealed class WindowPlace : IWindowPlace
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;

    internal static WindowPlace Of(IWindowPlace p) => new() { Left = p.Left, Top = p.Top, Width = p.Width, Height = p.Height };
    internal void To(IWindowPlace p) => (p.Left, p.Top, p.Width, p.Height) = (Left, Top, Width, Height);
}

/// <summary>One buff bar's 두께 and 길이 (<see cref="BuffIndicator"/>).</summary>
public sealed class BarSize
{
    public double Thickness { get; set; } = 1;
    public double Length { get; set; } = 1;
}

/// <summary>오버레이 프로필: the screenshot it was set up on (<see cref="Shot"/>) and where the windows go, their sizes and
/// thicknesses, split or together, and plate colours, so a different in-game situation (another HUD spot, a bright or
/// dark map) is one switch away. Colours of the boxes, effects and class layouts are not in it. A new profile holds the
/// defaults.</summary>
public sealed class OverlayProfile
{
    public const int Count = 5;

    public WindowPlace Overlay { get; set; } = new();
    /// <summary>Your window's spot and size for each 개별 직업각인 ("클래스 · 직업각인") placed in this profile;
    /// <see cref="Overlay"/> is the one every other class uses.</summary>
    public Dictionary<string, WindowPlace> SubclassPlaces { get; set; } = [];
    public double Opacity { get; set; } = 0.9;
    public string PlateColor { get; set; } = OverlaySettings.DefaultPlate;
    public double GaugeThickness { get; set; } = 6;

    public bool BuffsDetached { get; set; }
    public bool BuffsSide { get; set; }
    public WindowPlace Buffs { get; set; } = new();
    /// <summary>Only the bars set; the rest are 1 × 1.</summary>
    public Dictionary<BuffKind, BarSize> BuffBars { get; set; } = [];

    public bool PartyTogether { get; set; }
    public double BoundPartyLeft { get; set; } = double.NaN;
    public double BoundPartyTop { get; set; } = double.NaN;
    public string PartyPlateColor { get; set; } = OverlaySettings.DefaultPlate;
    /// <summary>Each party slot window's spot (only Left/Top are used).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<WindowPlace> PartySlots { get; set; } = [];

    /// <summary>The screen area (physical pixels) the profile's screenshot shows, so it maps onto window spots; null =
    /// no screenshot, or one not of the game's screen (a backdrop only).</summary>
    public PixelRect? Shot { get; set; }

    /// <summary>Takes the places now in <paramref name="s"/> (the screenshot is kept).</summary>
    public void CaptureFrom(Settings s)
    {
        KeepPlace(s.PlaceKey, s);
        (Opacity, PlateColor, GaugeThickness) = (s.Overlay.Opacity, s.Overlay.PlateColor, s.Gauge.Thickness);
        (BuffsDetached, BuffsSide, Buffs) = (s.Buffs.Detached, s.Buffs.Side, WindowPlace.Of(s.Buffs));
        BuffBars = s.Buffs.Indicators.ToDictionary(i => i.Key, i => new BarSize { Thickness = i.Value.Thickness, Length = i.Value.Length });
        (PartyTogether, BoundPartyLeft, BoundPartyTop, PartyPlateColor) = (s.Party.Together, s.Party.Left, s.Party.Top, s.Party.PlateColor);
        PartySlots = [.. s.Party.ShowOthersBySlot.Select(d => new WindowPlace { Left = d.Left, Top = d.Top })];
    }

    /// <summary>Stores your window's spot and size (in <paramref name="s"/>) as <paramref name="key"/>'s (null = the classes').</summary>
    internal void KeepPlace(string? key, Settings s)
    {
        if (key is null) Overlay = WindowPlace.Of(s.Overlay);
        else SubclassPlaces[key] = WindowPlace.Of(s.Overlay);
    }

    /// <summary><paramref name="key"/>'s spot and size, the classes' one where it has none yet.</summary>
    internal WindowPlace PlaceOf(string? key) => key is not null && SubclassPlaces.TryGetValue(key, out var p) ? p : Overlay;

    /// <summary>Puts this profile's places into <paramref name="s"/>.</summary>
    public void ApplyTo(Settings s)
    {
        PlaceOf(s.PlaceKey).To(s.Overlay);
        (s.Overlay.Opacity, s.Overlay.PlateColor, s.Gauge.Thickness) = (Opacity, PlateColor, GaugeThickness);
        Buffs.To(s.Buffs);
        (s.Buffs.Detached, s.Buffs.Side) = (BuffsDetached, BuffsSide);
        foreach (var kind in Enum.GetValues<BuffKind>())
        {
            if (!BuffBars.ContainsKey(kind) && !s.Buffs.Indicators.ContainsKey(kind)) continue;   // stays default
            var size = BuffBars.GetValueOrDefault(kind) ?? new BarSize();
            var bar = s.Buffs.Edit(kind);
            (bar.Thickness, bar.Length) = (size.Thickness, size.Length);
        }
        (s.Party.Together, s.Party.Left, s.Party.Top, s.Party.PlateColor) = (PartyTogether, BoundPartyLeft, BoundPartyTop, PartyPlateColor);
        for (int i = 0; i < s.Party.ShowOthersBySlot.Count; i++)
        {
            var at = i < PartySlots.Count ? PartySlots[i] : new WindowPlace();
            (s.Party.ShowOthersBySlot[i].Left, s.Party.ShowOthersBySlot[i].Top) = (at.Left, at.Top);
        }
    }
}
