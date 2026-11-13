using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>내 오버레이 → 줄 수 (class-profiles.plan.md E): the 줄 수 창's 켜기, glow and look, one per profile, the same for every class in it (user, 2026-11-14)
/// (<see cref="RaidShow"/>, <see cref="RaidLooks"/>). What is per raid (gates, 빛날 줄 수, ± 줄) stays in 레이드.</summary>
public partial class SettingsWindow
{
    readonly CardStack _secRaidCount = new();
    static readonly CanvasBox RaidCountBox = new("Raid", null, null);

    void FillRaidCount()
    {
        var r = _s.Raids;
        void Again() => Dispatcher.BeginInvoke(() => { FillRaidCount(); FillRaid(); });   // 레이드 greys its gates by the switch
        var p = Refill(_secRaidCount, "줄 수 창");
        p.Children.Add(DefaultedCheck(Check("줄 수 읽기 (모든 레이드)", r.CountEnabled, v => r.CountEnabled = v, () => { Changed(); Again(); },
            "끄면 모든 레이드에서 줄 수(예: X 494)를 읽지 않고 줄 수 창을 숨깁니다. 관문마다 켜 둔 줄 수 창 설정은 남아서, 다시 켜면 돌아옵니다. " +
            "디버프 창은 따르지 않습니다. 트레이 메뉴의 레이드에도 있습니다."), s => s.Raids.CountEnabled, (s, v) => s.Raids.CountEnabled = v));
        if (!r.CountEnabled) p.Children.Add(Note("줄 수 읽기가 꺼져 있습니다. 관문마다 켠 줄 수 창 설정은 남아서, 다시 켜면 돌아옵니다."));

        p.Children.Add(SubHeading("빛남", "레이드에서 관문마다 정한 줄 수가 되면 창이 빛납니다."));
        p.Children.Add(DefaultedCheck(Check("빛나기", r.Glow, v => r.Glow = v, () => { Changed(); Again(); }), s => s.Raids.Glow, (s, v) => s.Raids.Glow = v));
        var only = Check("빛날 때만 표시", r.OnlyWhenGlowing, v => r.OnlyWhenGlowing = v, Changed, "줄 수 창을 평소에는 숨기고, 빛나는 동안에만 보여 줍니다. 빛나기가 켜져 있어야 합니다.");
        only.IsEnabled = r.Glow;
        p.Children.Add(DefaultedCheck(only, s => s.Raids.OnlyWhenGlowing, (s, v) => s.Raids.OnlyWhenGlowing = v));
        if (r.Glow)
        {
            p.Children.Add(Defaulted(Row("빛나는 동안", EnumCombo([(GlowUntil.Seconds, "정한 초 동안"), (GlowUntil.Bars, "정한 줄만큼 지날 때까지")], r.GlowUntil,
                v => { r.GlowUntil = v; Again(); })), s => s.Raids.GlowUntil, (s, v) => s.Raids.GlowUntil = v));
            p.Children.Add(r.GlowUntil == GlowUntil.Seconds
                ? Defaulted(SliderRow("초", 1, 120, r.GlowSeconds, 1, v => $"{v:0}초", v => r.GlowSeconds = (int)v, Changed), s => s.Raids.GlowSeconds, (s, v) => s.Raids.GlowSeconds = v)
                : Defaulted(SliderRow("줄", 1, 50, r.GlowBars, 1, v => $"{v:0}줄", v => r.GlowBars = (int)v, Changed), s => s.Raids.GlowBars, (s, v) => s.Raids.GlowBars = v));
        }

        p.Children.Add(SubHeading("모양"));
        p.Children.Add(Defaulted(ColorRow(Label("배경 색"), r.Background, v => r.Background = v, swatch: false), s => s.Raids.Background, (s, v) => s.Raids.Background = v));
        p.Children.Add(Defaulted(SliderRow("불투명도", 0.1, 1, r.Opacity, 0.05, v => $"{v * 100:0}%", v => r.Opacity = v, Changed), s => s.Raids.Opacity, (s, v) => s.Raids.Opacity = v));
    }

    /// <summary>레이드's link to the 줄 수 panel: 내 오버레이 with the 줄 수 창 picked.</summary>
    void ShowRaidCount() => GoTo(new(AreaOverlay, "줄 수 창", RaidCountBox));
}
