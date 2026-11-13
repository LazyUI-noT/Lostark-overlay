using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>내 오버레이 → 암흑수류탄 (amsu.design.md): the switch (any boss), the look, colours, 투명도, 크기 and the warning.</summary>
public partial class SettingsWindow
{
    void FillAmsu()
    {
        var a = _s.Amsu;
        var p = Refill(_secAmsu, "암흑수류탄", "보스에 걸린 암흑수류탄(20초)과 빛나는 암흑수류탄(25초) 중 오래 남은 쪽의 시간을 보여 줍니다. 내 화면에만 보입니다.");
        p.Children.Add(CardBadge(LookGroup.암수));
        p.Children.Add(DefaultedCheck(Check("암흑수류탄 읽기", a.On, v => a.On = v, () => { Changed(); Dispatcher.BeginInvoke(FillAmsu); },
            "보스 체력바 아래 디버프 줄에서 읽습니다."), s => s.Amsu.On, (s, v) => s.Amsu.On = v));
        if (!a.On) return;
        // The in-game sizes live in 화면·인식 → 게임 설정 값 (user, 2026-11-12).
        p.Children.Add(Note("타겟 UI 버프 크기(보스)는 화면·인식 → 게임 설정 값에서 게임과 같게 맞추세요. 읽을 칸 수도 그곳에 있습니다. 100% 이상이면 남은 시간까지 읽습니다(1080p 기준, 21:9 강제처럼 화면이 낮으면 더 크게). 그보다 작으면 폭탄이 걸린 동안 아이콘만 보여 주고, 1080p에서 80%(21:9 강제는 90%)보다 작으면 읽지 않습니다. 디버프가 많아 '+N'으로 밀려나면 마지막에 읽은 시간부터 흐리게 계속 셉니다."));

        p.Children.Add(Defaulted(HelpRow("읽기 빈도", "높음: 자주 읽어서 시간이 바로 맞습니다. 낮음: 폭탄이 없을 땐 1초마다 찾고, 찾으면 1초 동안 시간을 맞춘 뒤 3초 남을 때까지 1초마다만 확인합니다. 다른 사람이 다시 던져도 1초 안에 맞춰집니다. 암흑수류탄을 켠 뒤 프레임이 떨어지면 낮음을 써 보세요. 100% 미만에서는 시간을 읽지 않아서, 낮음으로 둬도 높음처럼 읽습니다.",
            EnumCombo([(AmsuRate.높음, "높음 (정확)"), (AmsuRate.낮음, "낮음 (가벼움)")], a.Rate, v => a.Rate = v)), s => s.Amsu.Rate, (s, v) => s.Amsu.Rate = v));
        p.Children.Add(Defaulted(Row("표시 방식", EnumCombo([(AmsuLook.막대, "막대"), (AmsuLook.원, "원"), (AmsuLook.숫자, "숫자"), (AmsuLook.세로막대, "세로 막대 (버프 모양)")], a.Look,
            v => { a.Look = v; Dispatcher.BeginInvoke(FillAmsu); })), s => s.Amsu.Look, (s, v) => s.Amsu.Look = v));
        if (a.Look == AmsuLook.세로막대)
            p.Children.Add(DefaultedCheck(Check("남은 초 표시", a.ShowSeconds, v => a.ShowSeconds = v, Changed, "아이콘 없이 버프 막대처럼 위에서 아래로 줄어듭니다. 켜면 막대 위쪽에 남은 초를 씁니다."), s => s.Amsu.ShowSeconds, (s, v) => s.Amsu.ShowSeconds = v));
        if (a.Look == AmsuLook.막대)
            p.Children.Add(Defaulted(Row("줄어드는 쪽", EnumCombo([(BarDirection.오른쪽, "오른쪽"), (BarDirection.왼쪽, "왼쪽"), (BarDirection.위, "위"), (BarDirection.아래, "아래")],
                a.Direction, v => a.Direction = v)), s => s.Amsu.Direction, (s, v) => s.Amsu.Direction = v));
        if (a.Look == AmsuLook.숫자)
            p.Children.Add(DefaultedCheck(Check("아이콘 옆에", a.NumberBeside, v => a.NumberBeside = v, Changed, "끄면 아이콘 위에 숫자를 씁니다."), s => s.Amsu.NumberBeside, (s, v) => s.Amsu.NumberBeside = v));
        p.Children.Add(Defaulted(ColorRow(Label(a.Look == AmsuLook.숫자 ? "색 (옆 숫자)" : "색"), a.Color, v => a.Color = v, swatch: false),
            s => s.Amsu.Color, (s, v) => s.Amsu.Color = v));
        p.Children.Add(Defaulted(SliderRow("경고", 0, AmsuSettings.MaxWarnSeconds, a.WarnSeconds, 1, v => v < 1 ? "끔" : $"{v:0}초 미만",
            v => a.WarnSeconds = (int)Math.Round(v), Changed), s => s.Amsu.WarnSeconds, (s, v) => s.Amsu.WarnSeconds = v));
        if (a.WarnSeconds > 0) p.Children.Add(Defaulted(ColorRow(Label("경고 색"), a.WarnColor, v => a.WarnColor = v, swatch: false),
            s => s.Amsu.WarnColor, (s, v) => s.Amsu.WarnColor = v));
        p.Children.Add(Defaulted(SliderRow("불투명도", AmsuSettings.MinOpacity, 1, a.Opacity, 0.05, v => $"{v * 100:0}%", v => a.Opacity = v, Changed),
            s => s.Amsu.Opacity, (s, v) => s.Amsu.Opacity = v));
        p.Children.Add(Note("크기는 내 오버레이 화면에서 암수 창을 눌러 윤곽의 흰 점을 끌어 바꿉니다. 모서리는 비율을 지키고, 막대 모양에서는 오른쪽·아래 점이 너비·높이를 따로 바꿉니다. Ctrl+화살표로 1px씩 바꿀 수도 있습니다."));
    }
}
