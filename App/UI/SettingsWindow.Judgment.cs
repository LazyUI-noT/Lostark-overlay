using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>내 오버레이 → 기타 → 단심 (user, 2026-10-13): on per 직업각인, how it is read, how it is shown. 데미지 폰트 크기 is
/// also in 스킬창, beside 버프 크기, with the game's other settings.</summary>
public partial class SettingsWindow
{
    void FillJudgment()
    {
        var j = _s.Judgment;
        var p = Refill(_secOther, "단심", "단죄·심판 룬의 심판이 켜져 있는 6초와, 다시 터질 수 있을 때까지 남은 24초를 보여 줍니다. 내 화면에만 보이고 파티원에게는 보내지 않습니다.");
        if (_s.ActiveCharacter is not { } key)
        {
            p.Children.Add(Note("캐릭터를 먼저 고르세요. 단심은 직업각인마다 따로 켭니다."));
            return;
        }
        p.Children.Add(Check($"{key}에서 쓰기", j.On(key), v => j.Set(key, v), () => { Changed(); Dispatcher.BeginInvoke(FillJudgment); },
            "직업각인마다 따로 켭니다. 끄면 단심을 읽지 않습니다."));
        if (!j.On(key)) return;

        p.Children.Add(HelpRow("감지 방식",
            "아이콘이 가장 정확합니다. 버프 줄의 심판 아이콘과 그 아래 남은 시간으로 시작 시각을 맞춥니다. 캐릭터 위에 뜨는 '심판' 글자는 버프가 많아 아이콘이 버프 줄에서 밀려날 때를 위한 보조로, 아이콘보다 덜 안정적입니다.",
            EnumCombo([(JudgmentSource.Both, "아이콘 + 글자 (아이콘 우선)"), (JudgmentSource.Icon, "아이콘만"), (JudgmentSource.Text, "글자만")], j.Source, v => j.Source = v)));
        if (j.Source != JudgmentSource.Icon)
            p.Children.Add(HelpRow("게임의 데미지 폰트 크기 (%)", "게임 설정의 데미지 폰트 크기와 같게 맞춰 주세요. 스킬창 탭의 버프 크기 옆에도 있습니다.", FontSizeCombo()));

        p.Children.Add(Row("표시 방식", EnumCombo([(JudgmentDisplay.Bar, "막대 (30초)"), (JudgmentDisplay.ReadyIcon, "준비되면 아이콘만")], j.Display,
            v => { j.Display = v; Dispatcher.BeginInvoke(FillJudgment); })));
        if (j.Display == JudgmentDisplay.Bar)
        {
            p.Children.Add(ColorRow(Label("심판 색 (6초)"), j.Color, v => j.Color = v, swatch: false));
            p.Children.Add(Check("한 색으로", j.OneColor, v => j.OneColor = v, () => { Changed(); Dispatcher.BeginInvoke(FillJudgment); },
                "대기 시간(24초)을 심판 색을 어둡게 해서 그립니다."));
            if (!j.OneColor) p.Children.Add(ColorRow(Label("대기 색 (24초)"), j.CooldownColor, v => j.CooldownColor = v, swatch: false));
            p.Children.Add(Check("남은 시간 표시", j.ShowLabel, v => j.ShowLabel = v, Changed, "심판이 켜져 있는 동안은 심판의 남은 시간, 그 뒤로는 다시 터질 수 있을 때까지의 시간입니다."));
        }
        p.Children.Add(SliderRow("불투명도", JudgmentSettings.MinOpacity, 1, j.Opacity, 0.05, v => $"{v * 100:0}%", v => j.Opacity = v, Changed));
        p.Children.Add(Note("위치와 크기는 편집 모드에서 단심 창을 끌어 바꿉니다."));
        p.Children.Add(ResetButton("단심 되돌리기", "표시 설정을 되돌립니다. 켠 직업각인, 데미지 폰트 크기, 창 위치와 크기는 남습니다.",
            () => SettingsResets.Judgment(_s)));
    }

    /// <summary>게임의 데미지 폰트 크기, shown in 스킬창 beside 버프 크기 and in 기타 → 단심; either one changes the same setting.</summary>
    ComboBox FontSizeCombo()
    {
        var size = Combo();
        size.ItemsSource = JudgmentSettings.FontSizes;
        size.SelectedItem = _s.Judgment.FontSize;
        size.SelectionChanged += (_, _) =>
        {
            if (size.SelectedItem is not int v || v == _s.Judgment.FontSize) return;
            _s.Judgment.FontSize = v;
            Changed();
            Dispatcher.BeginInvoke(() => { FillJudgment(); FillFit(); });
        };
        return size;
    }
}
