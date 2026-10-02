using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>내 오버레이's preview: your overlay as it is now (the active class) with ▶ 실행해 보기. A click opens the section
/// of what was clicked (a box: 스킬 칸 with that box picked; the gauge: 아덴 게이지; a buff bar or the debuff window: 버프/디버프; else 전체).
/// While the 레이아웃 편집기 is open, the preview steps aside for it.</summary>
public partial class SettingsWindow
{
    OverlayRenderer? _quick, _quickBuffs, _quickDebuffs;   // the buff and debuff windows under yours, while they are shown
    PreviewPlayer? _quickPlay;
    StackPanel? _quickBlock;   // placed by ShowBody: pinned on the right, or above the form
    readonly StackPanel _quickShown = new();
    readonly ToggleButton _quickRun = new() { Content = "▶ 실행해 보기", Padding = new(10, 3, 10, 3), Margin = new(0, 0, 8, 0) };
    readonly TextBlock _editorOpen = new()
    {
        Text = "레이아웃 편집기가 열려 있습니다. 칸의 색과 효과도 편집기에서 칸을 눌러 바꿀 수 있습니다.",
        TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new(0, 8, 0, 8), Visibility = Visibility.Collapsed,
    };

    void BuildQuick()
    {
        var quick = _quick = new OverlayRenderer { Settings = _s, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(8) };
        var buffs = _quickBuffs = new OverlayRenderer { Settings = _s, BuffsOnly = true, Margin = new(8, 0, 8, 8), VerticalAlignment = VerticalAlignment.Top };
        var debuffs = _quickDebuffs = new OverlayRenderer { Settings = _s, DebuffsOnly = true, Margin = new(8, 0, 8, 8), VerticalAlignment = VerticalAlignment.Top };
        var play = _quickPlay = new PreviewPlayer(quick, buffs, debuffs);
        buffs.MouseLeftButtonDown += (_, e) =>
        {
            if (buffs.PartAt(e.GetPosition(buffs))?.Part is { } used) play.Click(used, 1);
            Go(AreaOverlay, SecBuffs);
        };
        debuffs.MouseLeftButtonDown += (_, _) => Go(AreaOverlay, SecBuffs);
        quick.MouseLeftButtonDown += (_, e) =>
        {
            var part = quick.PartAt(e.GetPosition(quick))?.Part;
            if (part is { } used) play.Click(used, PreviewPlayer.Sections(_s, _chars));
            if (part is { } box && box < Part.변화)
            {
                ShowBox(box, plate: false);
                Go(AreaOverlay, SecSkills);
            }
            else Go(AreaOverlay, part switch { Part.게이지 => "아덴 게이지", >= FreeLayout.FirstBuff => SecBuffs, _ => "전체" });
        };

        var run = _quickRun;
        var reset = new Button { Content = "초기화", Padding = new(10, 3, 10, 3), IsEnabled = false };
        run.Checked += (_, _) => { play.Start(); reset.IsEnabled = true; run.Content = "■ 멈추기"; };
        run.Unchecked += (_, _) => { play.Stop(); reset.IsEnabled = false; run.Content = "▶ 실행해 보기"; };
        reset.Click += (_, _) => play.Reset();
        IsVisibleChanged += (_, _) => { if (!IsVisible) run.IsChecked = false; };

        var p = _quickShown;
        p.Children.Add(new TextBlock { Text = "오버레이 미리보기 · 눌러서 편집", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) });
        // Shrinks to fit the pinned column; clicks still land on the right part.
        p.Children.Add(new Border
        {
            Child = new Viewbox { Child = Stack(quick, new StackPanel { Orientation = Orientation.Horizontal, Children = { buffs, debuffs } }), StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left },
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x33)), CornerRadius = new(4), MinHeight = 120,
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 0) };
        buttons.Children.Add(run);
        buttons.Children.Add(reset);
        p.Children.Add(buttons);
        p.Children.Add(Note("▶ 실행해 보기를 켜면 누른 칸이 쓴 것처럼 움직이고 (쿨타임 중이면 1초 전으로), 바꾼 설정이 바로 보입니다. 크기와 위치는 배치의 레이아웃 편집기에서 바꿉니다."));

        _quickBlock = new StackPanel { Margin = new(0, 0, 16, 12), Children = { _editorOpen, p } };
        QuickRefresh();
    }

    /// <summary>The 레이아웃 편집기 is open: it shows the overlay, so this preview steps aside (its ▶ stops) until it closes.</summary>
    public bool EditorOpen
    {
        set
        {
            if (value) _quickRun.IsChecked = false;
            _quickShown.Visibility = Shown(!value);
            _editorOpen.Visibility = Shown(value);
        }
    }

    /// <summary>Redraws the preview after a change, the buff window shown with 분리된 창 and the debuff window with a raid, as in game.</summary>
    void QuickRefresh()
    {
        if (_quick is null || _quickBuffs is null || _quickDebuffs is null) return;
        _quickBuffs.Visibility = Shown(_s.Buffs.Detached);
        _quickDebuffs.Visibility = Shown(_s.Debuffs.Raid != Raid.없음);
        foreach (var r in new[] { _quick, _quickBuffs, _quickDebuffs }) r.Refresh();
    }

    /// <summary>The preview's sample, for the class active now.</summary>
    void QuickSample() => _quickPlay?.Show(PreviewPlayer.SampleFor(_chars));
}
