using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>내 오버레이 → 기타 → 단심 (user, 2026-10-13): on per 직업각인, how it is read, how it is shown. 데미지 폰트 크기 is
/// also in 스킬창, beside 버프 크기, with the game's other settings.</summary>
public partial class SettingsWindow
{
    void FillJudgment()
    {
        var j = _s.Judgment;
        var p = Refill(_secOther, "단심", "단죄·심판 룬의 심판이 켜져 있는 6초와, 다시 터질 수 있을 때까지 남은 24초를 보여 줍니다. 내 화면에만 보이고 파티원에게는 보내지 않습니다.");
        p.Children.Add(CardBadge(LookGroup.단심));
        if (_s.ActiveCharacter is not { } key)
        {
            p.Children.Add(Note("캐릭터를 먼저 고르세요. 단심은 직업각인마다 따로 켭니다."));
            return;
        }
        p.Children.Add(Check($"{key}에서 쓰기", j.On(key), v => j.Set(key, v), () => { Changed(); Dispatcher.BeginInvoke(FillJudgment); },
            "직업각인마다 따로 켭니다. 끄면 단심을 읽지 않습니다."));
        if (!j.On(key)) return;

        p.Children.Add(Defaulted(HelpRow("감지 방식",
            "아이콘이 가장 정확합니다. 버프 줄의 심판 아이콘과 그 아래 남은 시간으로 시작 시각을 맞춥니다. 캐릭터 위에 뜨는 '심판' 글자는 버프가 많아 아이콘이 밀려났을 때 대신 쓰며, 아이콘보다 덜 안정적입니다.",
            EnumCombo([(JudgmentSource.Both, "아이콘 + 글자 (아이콘 우선)"), (JudgmentSource.Icon, "아이콘만"), (JudgmentSource.Text, "글자만")], j.Source, v => j.Source = v)),
            s => s.Judgment.Source, (s, v) => s.Judgment.Source = v));
        if (j.Source != JudgmentSource.Icon)
            p.Children.Add(Note("데미지 폰트 크기는 화면·인식 → 게임 설정 값에서 맞춥니다."));

        p.Children.Add(Defaulted(Row("표시 방식", EnumCombo([(JudgmentDisplay.Bar, "막대"), (JudgmentDisplay.ReadyIcon, "아이콘")], j.Display,
            v => { j.Display = v; Dispatcher.BeginInvoke(FillJudgment); })), s => s.Judgment.Display, (s, v) => s.Judgment.Display = v));
        if (j.Display == JudgmentDisplay.ReadyIcon)
            p.Children.Add(Note("심판이 켜진 6초 동안 심판 아이콘에 남은 초, 24초 대기 동안 흑백 심판 아이콘에 다시 터질 때까지 남은 초를 보여 줍니다. 다시 터질 수 있으면 한 번 번쩍인 뒤 단죄 아이콘이 남습니다."));
        if (j.Display == JudgmentDisplay.Bar)
        {
            p.Children.Add(Defaulted(Row("막대", Check("세로", j.Side, v => j.Side = v, () => { Changed(); Dispatcher.BeginInvoke(FillJudgment); },
                "켜면 막대를 세워 위에서 아래로 줄어듭니다. 끄면 눕혀서 오른쪽에서 왼쪽으로 줄어듭니다. 심판이 켜진 6초 동안 한 번 줄어들고, 다시 가득 차서 24초 대기 동안 또 줄어듭니다."),
                Check("심판 아닐 때 숨김", j.HideWhenEmpty, v => j.HideWhenEmpty = v, Changed, "심판이 준비된 동안 막대를 숨깁니다. 끄면 준비된 동안 막대가 심판 색으로 가득 차 깜빡입니다. 편집 모드와 미리보기에서는 옮길 수 있게 흐리게 보입니다.")),
                s => (s.Judgment.Side, s.Judgment.HideWhenEmpty), (s, v) => (s.Judgment.Side, s.Judgment.HideWhenEmpty) = v));
            p.Children.Add(Defaulted(ColorRow(Label("심판 색 (6초)"), j.Color, v => j.Color = v, swatch: false),
                s => s.Judgment.Color, (s, v) => s.Judgment.Color = v));
            p.Children.Add(DefaultedCheck(Check("한 색으로", j.OneColor, v => j.OneColor = v, () => { Changed(); Dispatcher.BeginInvoke(FillJudgment); },
                "대기 시간(24초)을 심판 색을 어둡게 해서 그립니다."), s => s.Judgment.OneColor, (s, v) => s.Judgment.OneColor = v));
            if (!j.OneColor) p.Children.Add(Defaulted(ColorRow(Label("대기 색 (24초)"), j.CooldownColor, v => j.CooldownColor = v, swatch: false),
                s => s.Judgment.CooldownColor, (s, v) => s.Judgment.CooldownColor = v));
            p.Children.Add(DefaultedCheck(Check("남은 초", j.ShowLabel, v => j.ShowLabel = v, Changed,
                "심판이 켜져 있는 동안은 심판의 남은 시간, 그 뒤로는 다시 터질 수 있을 때까지의 시간입니다. 막대가 너무 얇으면 숫자가 보이지 않습니다."), s => s.Judgment.ShowLabel, (s, v) => s.Judgment.ShowLabel = v));
        }
        p.Children.Add(Defaulted(SliderRow("불투명도", JudgmentSettings.MinOpacity, 1, j.Opacity, 0.05, v => $"{v * 100:0}%", v => j.Opacity = v, Changed),
            s => s.Judgment.Opacity, (s, v) => s.Judgment.Opacity = v));
        p.Children.Add(Note("크기는 내 오버레이 화면에서 단심 창을 눌러 윤곽의 흰 점을 끌어 바꿉니다. 모서리는 비율을 지키고, 막대 모양에서는 오른쪽·아래 점이 너비·높이(길이·두께)를 따로 바꿉니다. Ctrl+화살표로 1px씩 바꿀 수도 있습니다."));
    }

    /// <summary>게임의 데미지 폰트 크기, shown in 스킬창 beside 버프 크기, in 기타 → 단심 and the 표시 map; any one changes the same setting.</summary>
    FrameworkElement FontSizeCombo() => SizeControl(GameSize.Font, () => _s.Judgment.FontSize, v => _s.Judgment.FontSize = v, () => { FillJudgment(); FillFit(); });
}
