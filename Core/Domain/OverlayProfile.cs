using System.Text.Json;
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

/// <summary>A look a 모든 위치 프로필에 적용 mark covers (<see cref="Settings.SharedLooks"/>): window spots and sizes are always per
/// 배치. What each covers, as the 설정 page says it: <see cref="LookShare.Covers"/>; keep it in step with <c>TakeLook</c>.</summary>
public enum LookGroup { 판, 스킬칸, 게이지, 버프막대, 미터기, 암수, 단심 }

/// <summary>오버레이 프로필: the screenshot it was set up on (<see cref="Shot"/>) and where the windows go, their sizes and
/// thicknesses, split or together, and plate colours, so a different in-game situation (another HUD spot, a bright or
/// dark map) is one switch away. Colours of the boxes, effects and class layouts are not in it. A new profile holds the
/// defaults.</summary>
public sealed class OverlayProfile
{
    public const int Count = 5;

    public WindowPlace Overlay { get; set; } = new();
    /// <summary>Your window's spot and size for each 직업각인 ("클래스 · 직업각인") placed in this profile;
    /// <see cref="Overlay"/> is the one every other character uses (no listed 직업각인).</summary>
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

    /// <summary>암수, 단심 (all their settings) and the meter's 버프 적용률 막대 투명도 (profile-looks.plan.md). Null in a file
    /// from before them: <c>SettingsStore.Normalize</c> fills today's.</summary>
    public AmsuSettings? Amsu { get; set; }
    public JudgmentSettings? Judgment { get; set; }
    public double? MeterBarOpacity { get; set; }
    /// <summary>The meter window's look (null in an older file: filled with today's).</summary>
    public MeterLooks? Meter { get; set; }
    /// <summary>The buff bars' look, not their 표시 (null in an older file: filled with today's).</summary>
    public BuffLooks? BuffLook { get; set; }
    /// <summary>The skill boxes' look that isn't the class's (null in an older file: filled with today's).</summary>
    public BoxLooks? Boxes { get; set; }
    /// <summary>판 표시 (null in an older file: today's).</summary>
    public bool? ShowPlate { get; set; }
    /// <summary>The gauge's colours and 투명도; its 두께 is <see cref="GaugeThickness"/> (null in an older file: today's).</summary>
    public GaugeLooks? GaugeLook { get; set; }

    /// <summary>Takes the places now in <paramref name="s"/> (the screenshot is kept).</summary>
    public void CaptureFrom(Settings s)
    {
        KeepPlace(s.PlaceKey, s);
        foreach (var look in Enum.GetValues<LookGroup>()) TakeLook(look, s);
        (BuffsDetached, BuffsSide, Buffs) = (s.Buffs.Detached, s.Buffs.Side, WindowPlace.Of(s.Buffs));
        BuffBars = s.Buffs.Indicators.ToDictionary(i => i.Key, i => new BarSize { Thickness = i.Value.Thickness, Length = i.Value.Length });
        (PartyTogether, BoundPartyLeft, BoundPartyTop, PartyPlateColor) = (s.Party.Together, s.Party.Left, s.Party.Top, s.Party.PlateColor);
        PartySlots = [.. s.Party.ShowOthersBySlot.Select(d => new WindowPlace { Left = d.Left, Top = d.Top })];
    }

    /// <summary><paramref name="look"/>'s stored values as text, to tell two 배치 apart.</summary>
    internal string LookKey(LookGroup look) => look switch
    {
        LookGroup.판 => $"{Opacity}|{PlateColor}|{ShowPlate}",
        LookGroup.스킬칸 => $"{Boxes}",
        LookGroup.게이지 => $"{GaugeThickness}|{GaugeLook}",
        LookGroup.암수 => Unplaced(Amsu?.Copy()),
        LookGroup.단심 => Unplaced(Judgment?.Copy()),
        LookGroup.미터기 => $"{MeterBarOpacity}|{Meter}",
        LookGroup.버프막대 => JsonSerializer.Serialize(BuffLook, Data.SettingsStore.Json),
        _ => "",
    };

    /// <summary><paramref name="window"/> (a copy) as text without its spot and size, which are always the 배치's own.</summary>
    static string Unplaced(IWindowPlace? window)
    {
        if (window is null) return "";
        new WindowPlace().To(window);
        return JsonSerializer.Serialize(window, window.GetType(), Data.SettingsStore.Json);
    }

    /// <summary>Takes <paramref name="look"/>'s values now in <paramref name="s"/> (copies, so later edits leave them).</summary>
    internal void TakeLook(LookGroup look, Settings s)
    {
        switch (look)
        {
            case LookGroup.판: (Opacity, PlateColor, ShowPlate) = (s.Overlay.Opacity, s.Overlay.PlateColor, s.Overlay.ShowPlate); break;
            case LookGroup.스킬칸: Boxes = BoxLooks.Of(s.Overlay); break;
            case LookGroup.게이지: (GaugeThickness, GaugeLook) = (s.Gauge.Thickness, GaugeLooks.Of(s.Gauge)); break;
            case LookGroup.암수: Amsu = s.Amsu.Copy(); break;
            case LookGroup.단심: Judgment = s.Judgment.Copy(); break;
            // A null 막대 투명도 (↺) means the meter's: stored as that, or ApplyTo would skip it and keep the other 배치's.
            case LookGroup.미터기: (MeterBarOpacity, Meter) = (s.Meter.BarOpacity ?? s.Meter.Opacity, MeterLooks.Of(s.Meter)); break;
            case LookGroup.버프막대: BuffLook = BuffLooks.Of(s.Buffs); break;
        }
    }

    /// <summary>Stores your window's spot and size (in <paramref name="s"/>) as <paramref name="key"/>'s (null = the classes').</summary>
    internal void KeepPlace(string? key, Settings s)
    {
        if (key is null) Overlay = WindowPlace.Of(s.Overlay);
        else SubclassPlaces[key] = WindowPlace.Of(s.Overlay);
    }

    /// <summary><paramref name="key"/>'s spot and size, the classes' one where it has none yet.</summary>
    internal WindowPlace PlaceOf(string? key) => key is not null && SubclassPlaces.TryGetValue(key, out var p) ? p : Overlay;

    /// <summary>Puts this profile's places into <paramref name="s"/>, and its looks but the shared ones
    /// (<see cref="Settings.SharedLooks"/>), which keep the live value.</summary>
    public void ApplyTo(Settings s)
    {
        bool Own(LookGroup g) => s.SharedLooks?.Contains(g) != true;
        PlaceOf(s.PlaceKey).To(s.Overlay);
        if (Own(LookGroup.판)) (s.Overlay.Opacity, s.Overlay.PlateColor, s.Overlay.ShowPlate) = (Opacity, PlateColor, ShowPlate ?? s.Overlay.ShowPlate);
        if (Own(LookGroup.스킬칸)) Boxes?.To(s.Overlay);
        if (Own(LookGroup.게이지))
        {
            s.Gauge.Thickness = GaugeThickness;
            GaugeLook?.To(s.Gauge);
        }
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
        // Copies, so editing the live ones leaves this profile as stored until the next switch captures them.
        // Shared or not, the 암수 and 단심 windows' spot and size are this 배치's.
        if (Amsu is not null)
        {
            if (Own(LookGroup.암수)) s.Amsu = Amsu.Copy();
            else WindowPlace.Of(Amsu).To(s.Amsu);
        }
        if (Judgment is not null)
        {
            if (Own(LookGroup.단심)) s.Judgment = Judgment.Copy();
            else WindowPlace.Of(Judgment).To(s.Judgment);
        }
        if (MeterBarOpacity is not null && Own(LookGroup.미터기)) s.Meter.BarOpacity = MeterBarOpacity;
        if (Meter is not null && Own(LookGroup.미터기)) Meter.To(s.Meter);
        if (BuffLook is not null && Own(LookGroup.버프막대)) BuffLook.To(s.Buffs);
    }

    /// <summary>Hand edits back into range, and today's settings where the file has none (<c>SettingsStore.Normalize</c>).</summary>
    internal void Fill(Settings s)
    {
        Amsu ??= s.Amsu.Copy();
        Amsu.Clamp();
        Judgment ??= s.Judgment.Copy();
        Judgment.Clamp();
        MeterBarOpacity = MeterSettings.ClampOpacity(MeterBarOpacity ?? s.Meter.BarOpacity ?? s.Meter.Opacity);
        ShowPlate ??= s.Overlay.ShowPlate;
        Boxes ??= BoxLooks.Of(s.Overlay);
        Boxes = Boxes with { BoxOpacity = Math.Clamp(double.IsFinite(Boxes.BoxOpacity) ? Boxes.BoxOpacity : 1, 0, 1) };   // a hand edit
        BuffLook ??= BuffLooks.Of(s.Buffs);
        BuffLook = BuffLook with { Bars = BuffLook.Bars ?? [] };   // a hand edit
        GaugeLook ??= GaugeLooks.Of(s.Gauge);
        GaugeLook = GaugeLook with { Opacity = Math.Clamp(double.IsFinite(GaugeLook.Opacity) ? GaugeLook.Opacity : 1, 0, 1) };   // a hand edit
        Meter ??= MeterLooks.Of(s.Meter);
        Meter = Meter with { Opacity = MeterSettings.ClampOpacity(Meter.Opacity), Background = Meter.Background ?? s.Meter.Background };
    }
}

/// <summary>The skill boxes' look kept per 배치: 칸 투명도, the gold edge, the 각성기 changed colour and the ready count's
/// colour. The boxes' colours, glow and cooldown style are the class's.</summary>
public sealed record BoxLooks(double BoxOpacity, string GoldColor, double GoldThickness, string AwakeningChangedColor, string ReadyCountColor)
{
    public static BoxLooks Of(OverlaySettings o) => new(o.BoxOpacity, o.GoldColor, o.GoldThickness, o.AwakeningChangedColor, o.ReadyCountColor);

    public void To(OverlaySettings o) =>
        (o.BoxOpacity, o.GoldColor, o.GoldThickness, o.AwakeningChangedColor, o.ReadyCountColor) = (BoxOpacity, GoldColor, GoldThickness, AwakeningChangedColor, ReadyCountColor);
}

/// <summary>The gauge's look kept per 배치: fill and empty colours, 투명도. Splits, section colours and glow are the
/// class's (<see cref="Settings.GaugeBars"/>); <see cref="GaugeStyle.ReadMode"/> is a read setting.</summary>
public sealed record GaugeLooks(string Fill, string Empty, double Opacity)
{
    public static GaugeLooks Of(GaugeStyle g) => new(g.Fill, g.Empty, g.Opacity);

    public void To(GaugeStyle g) => (g.Fill, g.Empty, g.Opacity) = (Fill, Empty, Opacity);
}

/// <summary>The buff bars' look kept per 배치: 줄어듦, 비었을 때 숨기기, and each bar's colour, 시간 표시 and 깜빡임. Their
/// 표시 (on / 자동 / off) isn't in it: that stays per character.</summary>
public sealed record BuffLooks(bool Drain, bool HideWhenEmpty, Dictionary<BuffKind, BuffBarLook> Bars)
{
    public static BuffLooks Of(BuffSettings b) => new(b.Drain, b.HideWhenEmpty,
        b.Indicators.ToDictionary(i => i.Key, i => new BuffBarLook(i.Value.Color, i.Value.ShowLabel, i.Value.EndFlash)));

    public void To(BuffSettings b)
    {
        (b.Drain, b.HideWhenEmpty) = (Drain, HideWhenEmpty);
        var defaults = new BuffSettings();
        foreach (var kind in Enum.GetValues<BuffKind>())
        {
            if (!Bars.ContainsKey(kind) && !b.Indicators.ContainsKey(kind)) continue;   // stays default
            var look = Bars.GetValueOrDefault(kind) ?? BuffBarLook.Of(defaults.Of(kind));
            var bar = b.Edit(kind);
            (bar.Color, bar.ShowLabel, bar.EndFlash) = (look.Color, look.ShowLabel, look.EndFlash);
        }
    }
}

public sealed record BuffBarLook(string Color, bool ShowLabel, bool? EndFlash)
{
    public static BuffBarLook Of(BuffIndicator i) => new(i.Color, i.ShowLabel, i.EndFlash);
}

/// <summary>The meter window's look kept per 배치: 표시 방식, the graph, row colours, background, 투명도.</summary>
public sealed record MeterLooks(MeterLook Look, bool Graph, string? My, string? Dealer, string? Support, string? Other, string Background, double Opacity)
{
    public static MeterLooks Of(MeterSettings m) => new(m.Look, m.Graph, m.MyColor, m.DealerColor, m.SupportColor, m.OtherColor, m.Background, m.Opacity);

    public void To(MeterSettings m) =>
        (m.Look, m.Graph, m.MyColor, m.DealerColor, m.SupportColor, m.OtherColor, m.Background, m.Opacity) = (Look, Graph, My, Dealer, Support, Other, Background, Opacity);
}
