using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>One 프로필's places and looks (class-profiles.plan.md, "Stored model"): every window's spot and size
/// (<see cref="Places"/>) and every look (<see cref="LookGroup"/>, <see cref="Looks"/>). The profile itself
/// (<see cref="OverlayProfile"/>) adds what is one per profile: the screenshot and the party windows.</summary>
public class ProfileEntry
{
    public WindowPlace Overlay { get; set; } = new();
    public double Opacity { get; set; } = 0.9;
    public string PlateColor { get; set; } = OverlaySettings.DefaultPlate;
    public double GaugeThickness { get; set; } = 6;

    public bool BuffsDetached { get; set; }
    public bool BuffsSide { get; set; }
    public WindowPlace Buffs { get; set; } = new();
    /// <summary>Only the bars set; the rest are 1 × 1.</summary>
    public Dictionary<BuffKind, BarSize> BuffBars { get; set; } = [];

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
    /// <summary>The gauge's own window under 따로 (spot, length and thickness scales) and whether it stands upright (null in an
    /// older file: today's). Places: <c>BuffsDetached</c>, the switch, is one too.</summary>
    public WindowPlace? GaugePlace { get; set; }
    public bool? GaugeUpright { get; set; }
    /// <summary>The DPS 미터기, 디버프 and 줄 수 창 windows' spot and size (null in an older file: today's).</summary>
    public WindowPlace? MeterPlace { get; set; }
    public WindowPlace? DebuffPlace { get; set; }
    public WindowPlace? RaidPlace { get; set; }
    /// <summary>The debuff window's boxes' sizes (null in an older file: today's).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<DebuffBox>? DebuffBoxes { get; set; }
    /// <summary>디버프 창's 비었을 때 숨기기 (null in an older file: today's).</summary>
    public bool? DebuffHideWhenEmpty { get; set; }
    /// <summary>The 줄 수 창's 배경 색 and 불투명도 (null in an older file: today's).</summary>
    public RaidLooks? RaidLook { get; set; }
    /// <summary>The 줄 수 창's 켜기, 빛날 때만 표시 and glow rules (null in an older file: today's; never read as off).</summary>
    public RaidShow? RaidShow { get; set; }

    /// <summary>The properties that are window spots and sizes, never copied as a look. 암수 and 단심 hold their window's spot
    /// inside their look (<see cref="Settings.ApplyLookToAll"/> keeps it).</summary>
    internal static readonly IReadOnlyList<string> Places =
    [
        nameof(Overlay), nameof(BuffsDetached), nameof(BuffsSide), nameof(Buffs), nameof(BuffBars),
        nameof(MeterPlace), nameof(DebuffPlace), nameof(RaidPlace), nameof(DebuffBoxes), nameof(GaugePlace), nameof(GaugeUpright),
    ];

    /// <summary>The properties each look covers: what <see cref="TakeLook"/> sets; keep the two in step.</summary>
    internal static readonly IReadOnlyDictionary<LookGroup, string[]> Looks = new Dictionary<LookGroup, string[]>
    {
        [LookGroup.판] = [nameof(Opacity), nameof(PlateColor), nameof(ShowPlate)],
        [LookGroup.스킬칸] = [nameof(Boxes)],
        [LookGroup.게이지] = [nameof(GaugeThickness), nameof(GaugeLook)],
        [LookGroup.버프막대] = [nameof(BuffLook)],
        [LookGroup.미터기] = [nameof(MeterBarOpacity), nameof(Meter)],
        [LookGroup.암수] = [nameof(Amsu)],
        [LookGroup.단심] = [nameof(Judgment)],
        [LookGroup.디버프] = [nameof(DebuffHideWhenEmpty)],
        [LookGroup.줄수창] = [nameof(RaidLook), nameof(RaidShow)],
    };

    /// <summary>Takes the places and looks now in <paramref name="s"/> (copies, so later edits leave them).</summary>
    public void CaptureFrom(Settings s)
    {
        Overlay = WindowPlace.Of(s.Overlay);
        foreach (var look in Enum.GetValues<LookGroup>()) TakeLook(look, s);
        (BuffsDetached, BuffsSide, Buffs) = (s.Buffs.Detached, s.Buffs.Side, WindowPlace.Of(s.Buffs));
        BuffBars = s.Buffs.Indicators.ToDictionary(i => i.Key, i => new BarSize { Thickness = i.Value.Thickness, Length = i.Value.Length });
        (MeterPlace, DebuffPlace, RaidPlace) = (WindowPlace.Of(s.Meter), WindowPlace.Of(s.Debuffs), WindowPlace.Of(s.Raids));
        DebuffBoxes = CopyBoxes(s.Debuffs.Boxes);
        (GaugePlace, GaugeUpright) = (WindowPlace.Of(s.Gauge), s.Gauge.Upright);
    }

    /// <summary>What is one per profile, not per 직업각인: 버프 막대, the 줄 수 창 and 디버프 숨김, as <paramref name="from"/>'s
    /// (<see cref="OverlayProfile.ShareProfileWide"/>).</summary>
    internal void TakeProfileWide(ProfileEntry from)
    {
        TakeBuffs(from);
        TakeRaid(from);
        TakeDebuffHide(from);
    }

    /// <summary>버프 막대 is one per profile (user, 2026-11-13): <paramref name="from"/>'s 합쳐서 / 따로, 세로, window, bar sizes and
    /// look, as copies (<see cref="OverlayProfile.ShareProfileWide"/>).</summary>
    internal void TakeBuffs(ProfileEntry from)
    {
        // Nulls only come from a hand-edited file; this runs at every load, so they read as defaults instead of throwing.
        (BuffsDetached, BuffsSide, Buffs) = (from.BuffsDetached, from.BuffsSide, from.Buffs is { } place ? WindowPlace.Of(place) : new());
        BuffBars = (from.BuffBars ?? []).ToDictionary(b => b.Key, b => b.Value is { } size ? new BarSize { Thickness = size.Thickness, Length = size.Length } : new BarSize());
        BuffLook = from.BuffLook is { } look ? look with { Bars = new(look.Bars) } : null;
    }

    /// <summary>The 줄 수 창 is one per profile (user, 2026-11-14): <paramref name="from"/>'s 켜기, 빛날 때만 표시, glow rules, 배경 색
    /// and 불투명도 (records: shared safely; <see cref="OverlayProfile.ShareProfileWide"/>).</summary>
    internal void TakeRaid(ProfileEntry from) => (RaidLook, RaidShow) = (from.RaidLook, from.RaidShow);

    /// <summary>디버프 없을 때 숨김 is one per profile (user, 2026-11-14); the debuff window's spot and 칸 크기 stay per 직업각인.</summary>
    internal void TakeDebuffHide(ProfileEntry from) => DebuffHideWhenEmpty = from.DebuffHideWhenEmpty;

    protected static List<DebuffBox> CopyBoxes(List<DebuffBox> boxes) => [.. boxes.Select(b => new DebuffBox { Width = b.Width, Height = b.Height })];

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
            case LookGroup.디버프: DebuffHideWhenEmpty = s.Debuffs.HideWhenEmpty; break;
            case LookGroup.줄수창: (RaidLook, RaidShow) = (RaidLooks.Of(s.Raids), RaidShow.Of(s.Raids)); break;
        }
    }

    /// <summary>Puts these places and looks into <paramref name="s"/>.</summary>
    public void ApplyTo(Settings s)
    {
        ApplyPlaces(s);
        ApplyLooks(s);
    }

    /// <summary>Puts the window spots and sizes (<see cref="Places"/>) into <paramref name="s"/>.</summary>
    internal void ApplyPlaces(Settings s)
    {
        Overlay.To(s.Overlay);
        Buffs.To(s.Buffs);
        (s.Buffs.Detached, s.Buffs.Side) = (BuffsDetached, BuffsSide);
        foreach (var kind in Enum.GetValues<BuffKind>())
        {
            if (!BuffBars.ContainsKey(kind) && !s.Buffs.Indicators.ContainsKey(kind)) continue;   // stays default
            var size = BuffBars.GetValueOrDefault(kind) ?? new BarSize();
            var bar = s.Buffs.Edit(kind);
            (bar.Thickness, bar.Length) = (size.Thickness, size.Length);
        }
        MeterPlace?.To(s.Meter);
        DebuffPlace?.To(s.Debuffs);
        RaidPlace?.To(s.Raids);
        GaugePlace?.To(s.Gauge);
        if (GaugeUpright is { } upright) s.Gauge.Upright = upright;
        if (DebuffBoxes is not null) s.Debuffs.Boxes = CopyBoxes(DebuffBoxes);
    }

    /// <summary>Puts the looks (<see cref="Looks"/>) into <paramref name="s"/>. 암수 and 단심 bring their window's spot.</summary>
    internal void ApplyLooks(Settings s)
    {
        (s.Overlay.Opacity, s.Overlay.PlateColor, s.Overlay.ShowPlate) = (Opacity, PlateColor, ShowPlate ?? s.Overlay.ShowPlate);
        Boxes?.To(s.Overlay);
        s.Gauge.Thickness = GaugeThickness;
        GaugeLook?.To(s.Gauge);
        // Copies, so editing the live ones leaves this entry as stored until the next switch captures them.
        if (Amsu is not null) s.Amsu = Amsu.Copy();
        if (Judgment is not null) s.Judgment = Judgment.Copy();
        if (MeterBarOpacity is not null) s.Meter.BarOpacity = MeterBarOpacity;
        Meter?.To(s.Meter);
        BuffLook?.To(s.Buffs);
        if (DebuffHideWhenEmpty is { } hide) s.Debuffs.HideWhenEmpty = hide;
        RaidLook?.To(s.Raids);
        RaidShow?.To(s.Raids);
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
        MeterPlace ??= WindowPlace.Of(s.Meter);
        DebuffPlace ??= WindowPlace.Of(s.Debuffs);
        RaidPlace ??= WindowPlace.Of(s.Raids);
        GaugePlace ??= WindowPlace.Of(s.Gauge);
        GaugeUpright ??= s.Gauge.Upright;
        DebuffBoxes = CopyBoxes([.. (DebuffBoxes ?? s.Debuffs.Boxes).OfType<DebuffBox>()]);   // a hand edit: a null box
        DebuffHideWhenEmpty ??= s.Debuffs.HideWhenEmpty;
        RaidShow = (RaidShow ?? RaidShow.Of(s.Raids)).Clamped();
        RaidLook ??= RaidLooks.Of(s.Raids);
        RaidLook = RaidLook with { Opacity = Math.Clamp(double.IsFinite(RaidLook.Opacity) ? RaidLook.Opacity : 1, 0.1, 1), Background = RaidLook.Background ?? s.Raids.Background };
    }
}
