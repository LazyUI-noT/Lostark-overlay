using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>The box at the top left of 내 오버레이's canvas (user, 2026-11-01): the screen and every in-game size the reads
/// depend on, in one place: 해상도 and 비율 (ScreenSummary), 버프·디버프 크기 and 타겟 UI 버프 크기 (보스) (changed here), HUD 크기 (shown;
/// 맞추기 opens 화면·인식 so a refit isn't started by accident).</summary>
public partial class SettingsWindow
{
    readonly WrapPanel _screenBox = new();   // 해상도 … 타겟 UI 버프 크기, filled by FillScreenBox

    /// <summary>One thin row along the canvas top, outside its zoom (preview-images.plan.md 1): 인게임 설정's facts, then the
    /// raid-only switches. It sits above the canvas, so presses and the wheel on it never reach the canvas; it wraps only when
    /// 설정 is narrow. The pinned 되돌리기 / 다시 실행 sit at the canvas's bottom right.</summary>
    DockPanel WithScreenBox(OverlayCanvas canvas)
    {
        FillScreenBox();
        FillAutoBox();
        var row = new Border { BorderThickness = new(0, 0, 0, 1), Padding = new(Tokens.Space.Block, Tokens.Space.Tight, Tokens.Space.Block, Tokens.Space.Tight), Child = new WrapPanel { Children = { _screenBox, _autoBox } } };
        row.SetResourceReference(Border.BackgroundProperty, NavBrushKey);
        row.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        DockPanel.SetDock(row, Dock.Top);
        var bar = UndoBar(canvas);
        (bar.HorizontalAlignment, bar.VerticalAlignment, bar.Margin) = (HorizontalAlignment.Right, VerticalAlignment.Bottom, new(Tokens.Space.Block));
        return new DockPanel { Children = { row, new Grid { Children = { canvas, bar } } } };
    }

    /// <summary>The 자동 box under it (user, 2026-11-15): every raid-only switch in one place, the same values as the 버프,
    /// 게이지 and 스킬 panels.</summary>
    readonly Border _autoBox = new() { Margin = new(Tokens.Space.Block, 0, 0, 0) };

    // The 전투자원 창 only while 편집 중 is a class with one (user, 2026-11-18); refilled on a pick (RefreshPick).
    void FillAutoBox()
    {
        var row = new WrapPanel
        {
            Children =
            {
                AutoBoxLabel(),
                Check("버프 막대", _s.Buffs.RaidOnly, v => _s.Buffs.RaidOnly = v, AutoBoxChanged),
                Check("게이지", _s.Gauge.RaidOnly, v => _s.Gauge.RaidOnly = v, AutoBoxChanged),
                Check("스킬 칸", _s.Overlay.SkillsRaidOnly, v => _s.Overlay.SkillsRaidOnly = v, AutoBoxChanged),
            },
        };
        if (EditedHasRing()) row.Children.Add(Check("전투자원 창", _s.Ring.RaidOnly, v => _s.Ring.RaidOnly = v, AutoBoxChanged));
        _autoBox.Child = row;
    }

    /// <summary>Refilled with 스킬창 맞추기's page (FillFit): a fit, 직접 입력, 버프 크기 or the game window changed.</summary>
    void FillScreenBox()
    {
        var facts = ScreenSummary.Of(_s, _gameRunning);
        var refit = new Button { Content = "맞추기", Margin = new(Tokens.Space.Gap, 0, 0, 0), Padding = new(Tokens.Space.Gap, 0, Tokens.Space.Gap, 0), ToolTip = "인게임 설정을 엽니다." };
        refit.Click += (_, _) => Go(AreaFit);
        var rows = new (string Name, FrameworkElement Value)[]
        {
            ("해상도", Label(facts.Resolution)),
            ("비율", Label(facts.Ratio)),
            ("버프·디버프 크기", BuffSizeCombo()),
            ("HUD 크기", new StackPanel { Orientation = Orientation.Horizontal, Children = { Label(facts.Hud), refit } }),
            ("타겟 UI 버프 크기 (보스)", BossSizeCombo()),
        };
        _screenBox.Children.Clear();
        foreach (var (name, value) in rows)
        {
            var label = Label(name, 0.7);
            (label.Margin, label.VerticalAlignment, value.VerticalAlignment) = (new(0, 0, Tokens.Space.Tight, 0), VerticalAlignment.Center, VerticalAlignment.Center);
            _screenBox.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, Tokens.Space.Block, 0), Children = { label, value } });
        }
    }

    static TextBlock AutoBoxLabel()
    {
        var t = Label("레이드 안에서만 표시", 0.7);
        (t.Margin, t.VerticalAlignment) = (new(0, 0, 12, 0), VerticalAlignment.Center);
        return t;
    }

    /// <summary>A switch in the 자동 box: the panels that hold the same switch are rebuilt to show it.</summary>
    void AutoBoxChanged()
    {
        Changed();
        FillBuffs();
        FillGauge();
        FillBox();
        RefreshInspector();
    }

    /// <summary>A raid-only switch on a panel: the 자동 box shows it too.</summary>
    void AutoChanged()
    {
        Changed();
        FillAutoBox();
    }
}
