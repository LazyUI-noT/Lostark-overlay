using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>What the 표시 map's 게임 설정 boxes hold when opened (pyosi-map.plan.md): 해상도 · 비율 (값 출처, 직접 입력, 스킬창
/// 맞추기), the sizes (HUD, 버프, 타겟 UI 버프 (보스), 데미지 폰트) with 감지 개수, and the 값 직접 조정 switch. The same
/// settings and setters as 화면·인식 and 버프/디버프; the boxes are narrow, so the fields stack.</summary>
public partial class SettingsWindow
{
    /// <summary>값 직접 조정 is on: its switch, or 테스트 모드 (a video may show any size).</summary>
    bool FineOn => _s.FineSizes || GameWindow.TestMode;

    /// <summary>The map's folded text under a 게임 설정 box's title (FeatureMapView.Value).</summary>
    string? GameValue(string id) => ScreenSummary.Value(_s, _gameRunning, id);

    /// <summary>The body of a 게임 설정 box when opened (FeatureMapView.Body); null for any other box.</summary>
    UIElement? GameBody(string id)
    {
        var p = new StackPanel { Margin = new(0, 4, 0, 2) };
        switch (id)
        {
            case "game": FineSwitch(p); break;
            case "g-res": ResolutionBody(p); break;
            case "g-hud": HudBody(p); break;
            case "g-buff":
                p.Children.Add(Field("버프 크기 (%)", BuffSizeCombo()));
                p.Children.Add(Field("읽을 칸 수", CompactSlider(Core.Vision.Geometry.MinBuffBoxCount, Core.Vision.Geometry.MaxBuffBoxCount, _s.Buffs.Count, v => $"{v:0}칸", v => _s.Buffs.Count = v)));
                p.Children.Add(Small("버프 줄 오른쪽 끝에서부터 읽을 칸 수입니다. 칸이 많을수록 컴퓨터를 조금 더 씁니다. 버프가 많이 떠서 놓칠 때만 늘리세요."));
                break;
            case "g-debuff":
                p.Children.Add(Field("타겟 UI 버프 크기 (%)", BossSizeCombo()));
                p.Children.Add(Field("읽을 칸 수", CompactSlider(1, Core.Vision.Geometry.MaxBossSlots, _s.Debuffs.BossCount, BossCountText, v => _s.Debuffs.BossCount = v)));
                p.Children.Add(Small(BossCountNote));
                break;
            case "g-font":
                p.Children.Add(Field("데미지 폰트 크기 (%)", FontSizeCombo()));
                p.Children.Add(Small("게임 설정의 데미지 폰트 크기와 같게 맞추세요. 단심이 캐릭터 위의 '심판' 글자를 읽을 때 씁니다."));
                break;
            default: return null;
        }
        return new Border { Tag = "map-seg", Child = p };   // a click inside doesn't fold the box
    }

    const string BossCountNote = "보스 체력바 아래 줄에서 왼쪽부터 읽을 칸 수입니다. 게임이 보여 주는 칸은 크기에 따라 최대 14 · 12 · 10개입니다. "
        + "줄이면 컴퓨터를 덜 쓰지만, 그 뒤 칸에 걸린 암흑수류탄은 찾지 못합니다. 모르둠 문양과 보스 숫자는 이 개수와 상관없이 읽습니다.";

    static string BossCountText(int count) => count >= Core.Vision.Geometry.MaxBossSlots ? "모두" : $"{count}칸";

    /// <summary>값 직접 조정's switch at the top of the large box.</summary>
    void FineSwitch(StackPanel p)
    {
        bool test = GameWindow.TestMode;
        var check = Check("값 직접 조정", FineOn, v => _s.SetFineSizes(v), () => { Changed(); Dispatcher.BeginInvoke(BuildAll); },
            "켜면 HUD, 버프, 타겟 UI 버프 크기를 − +로 1%씩 올리고 내립니다. 게임에 없는 크기로 찍은 영상을 읽을 때 씁니다. 끄면 가장 가까운 게임 값으로 맞춥니다.");
        check.IsEnabled = !test;
        p.Children.Add(check);
        if (test) p.Children.Add(Small("테스트 모드에서는 항상 켜져 있습니다."));
    }

    void ResolutionBody(StackPanel p)
    {
        var mode = _s.FitMode;
        p.Children.Add(Field("값 출처", FitModeToggles("mapFitMode", () => Dispatcher.BeginInvoke(FillShow))));
        p.Children.Add(Small(FitModeNote(mode)));
        if (mode == FitMode.직접입력 && _s.ManualHud is not null) ScreenRows(p, () => Dispatcher.BeginInvoke(FillShow), compact: true);
        var refit = new Button { Content = "스킬창 맞추기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 6, 0, 2), Padding = new(10, 3, 10, 3) };
        refit.Click += (_, _) => RefitRequested?.Invoke();
        p.Children.Add(refit);
        p.Children.Add(Small("누를 때만 맞춥니다. 스킬을 쓸 수 있는 곳에서 쿨타임이 끝난 채로 서서 누르세요."));
    }

    void HudBody(StackPanel p)
    {
        if (_s.FitMode == FitMode.직접입력 && _s.ManualHud is not null) p.Children.Add(Field("HUD 크기 (%)", HudControl()));
        p.Children.Add(Small(_s.FitMode switch
        {
            FitMode.직접입력 => "스킬창 맞추기가 찾지 않고 이 값을 씁니다.",
            FitMode.수동조정 => "위치 조정에서 옮기고 적용한 값입니다.",
            _ => "스킬창 맞추기가 찾은 값입니다. 직접 정하려면 해상도 · 비율에서 직접 입력을 고르세요.",
        }));
    }

    /// <summary>A narrow box's field: its name over its control.</summary>
    static StackPanel Field(string name, UIElement control)
    {
        var f = new StackPanel { Margin = new(0, 2, 0, 4) };
        f.Children.Add(Label(name, 0.7));
        if (control is FrameworkElement e) e.HorizontalAlignment = HorizontalAlignment.Left;
        f.Children.Add(control);
        return f;
    }

    static TextBlock Small(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12, Margin = new(0, 0, 0, 4) };

    /// <summary>A whole-number slider for a narrow box: the value beside it, saved as it moves.</summary>
    UIElement CompactSlider(int min, int max, int value, Func<int, string> format, Action<int> set)
    {
        var text = new TextBlock { Text = format(value), MinWidth = 40, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 120, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) => { int v = (int)Math.Round(e.NewValue); set(v); text.Text = format(v); Changed(); };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { slider, text } };
    }

    /// <summary>A size: the game's presets in a combo box, or with 값 직접 조정 − + by 1%. A change refills the pages that show it.</summary>
    FrameworkElement SizeControl(GameSize kind, Func<int> get, Action<int> set, Action again)
    {
        void Done() { Changed(); Dispatcher.BeginInvoke(() => { again(); if (_secShow.IsVisible) FillShow(); }); }
        if (FineOn && GameSizes.Range(kind) is not null)
        {
            var value = new TextBlock { Text = $"{get()}%", MinWidth = 44, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Button Step(string text, int direction)
            {
                var b = new Button { Content = text, MinWidth = 28, Padding = new(6, 0, 6, 0) };
                b.Click += (_, _) =>
                {
                    int v = GameSizes.Step(kind, get(), direction, fine: true);
                    if (v == get()) return;
                    set(v);
                    value.Text = $"{v}%";
                    Done();
                };
                return b;
            }
            return new StackPanel { Orientation = Orientation.Horizontal, Children = { Step("−", -1), value, Step("+", 1) } };
        }
        var size = Combo();
        size.MinWidth = 120;
        size.ItemsSource = GameSizes.Offered(kind, get());   // a size set with 위치 조정's ± or 값 직접 조정 shows too
        size.SelectedItem = get();
        size.SelectionChanged += (_, _) => { if (size.SelectedItem is int v && v != get()) { set(v); Done(); } };
        return size;
    }
}
