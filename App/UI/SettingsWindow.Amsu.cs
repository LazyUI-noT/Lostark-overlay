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
        p.Children.Add(Check("암흑수류탄 읽기", a.On, v => a.On = v, () => { Changed(); Dispatcher.BeginInvoke(FillAmsu); },
            "보스 체력바 아래 디버프 줄에서 읽습니다. 모든 보스에서 읽습니다."));
        if (!a.On) return;
        // The same setting as 버프/디버프 → 디버프's, which shows only while a gate's 디버프 창 is on (user, 2026-11-01: none here).
        p.Children.Add(HelpRow("게임의 보스 디버프 크기 (%)", "게임 설정의 보스 디버프 크기와 같게 맞춰 주세요. 보스 체력바 아래 디버프 줄을 이 크기로 읽습니다.",
            BossSizeCombo()));
        p.Children.Add(Note("보스 디버프 크기를 게임과 같게 맞추세요. 남은 시간은 100% 이상에서만 읽습니다 (게임 화면 높이 1080 기준: 21:9 강제처럼 화면이 낮으면 그만큼 더 커야 합니다). 그보다 작으면 글자가 작아 시간을 읽지 않고, 폭탄이 걸려 있는 동안 아이콘만 보여줍니다(막대·원·숫자 없음). 디버프가 많아 '+N'으로 밀려나면 마지막에 읽은 시간부터 흐리게 계속 셉니다. 화면의 아이콘이 약 21px보다 작으면(1080p의 80%, 21:9 강제 화면의 90% 등) 테두리가 흐려 폭탄을 놓칠 수 있어 읽지 않습니다."));

        p.Children.Add(HelpRow("읽기 빈도", "높음: 지금처럼 자주 읽어 바로 맞춥니다. 낮음: 폭탄이 없을 땐 1초마다 찾고, 찾은 폭탄은 1초 동안 시간을 맞춘 뒤 3초 남을 때까지 1초마다만 확인합니다 (다른 사람이 다시 던지면 1초 안에 맞춰집니다). 암흑수류탄을 켜면 프레임이 떨어질 때 낮음을 써 보세요. 시간을 읽지 않는 크기(100% 미만)에서는 낮음이어도 높음으로 읽습니다.",
            EnumCombo([(AmsuRate.높음, "높음 (정확)"), (AmsuRate.낮음, "낮음 (가벼움)")], a.Rate, v => a.Rate = v)));
        p.Children.Add(Row("표시 방식", EnumCombo([(AmsuLook.막대, "막대"), (AmsuLook.원, "원"), (AmsuLook.숫자, "숫자")], a.Look,
            v => { a.Look = v; Dispatcher.BeginInvoke(FillAmsu); })));
        if (a.Look == AmsuLook.막대)
            p.Children.Add(Row("줄어드는 쪽", EnumCombo([(BarDirection.오른쪽, "오른쪽"), (BarDirection.왼쪽, "왼쪽"), (BarDirection.위, "위"), (BarDirection.아래, "아래")],
                a.Direction, v => a.Direction = v)));
        if (a.Look == AmsuLook.숫자)
            p.Children.Add(Check("아이콘 옆에", a.NumberBeside, v => a.NumberBeside = v, Changed, "끄면 아이콘 위에 숫자를 씁니다."));
        p.Children.Add(ColorRow(Label(a.Look == AmsuLook.숫자 ? "색 (옆 숫자)" : "색"), a.Color, v => a.Color = v, swatch: false));
        p.Children.Add(SliderRow("경고", 0, AmsuSettings.MaxWarnSeconds, a.WarnSeconds, 1, v => v < 1 ? "끔" : $"{v:0}초 미만",
            v => a.WarnSeconds = (int)Math.Round(v), Changed));
        if (a.WarnSeconds > 0) p.Children.Add(ColorRow(Label("경고 색"), a.WarnColor, v => a.WarnColor = v, swatch: false));
        p.Children.Add(SliderRow("크기", ClassProfiles.MinScale, ClassProfiles.MaxScale, a.Height, 0.05, v => $"{v * 100:0}%", v => a.Height = v, Changed));
        if (a.Look == AmsuLook.막대)
            p.Children.Add(SliderRow("막대 길이", ClassProfiles.MinScale, ClassProfiles.MaxScale, a.Width, 0.05, v => $"{v * 100:0}%", v => a.Width = v, Changed));
        p.Children.Add(SliderRow("불투명도", AmsuSettings.MinOpacity, 1, a.Opacity, 0.05, v => $"{v * 100:0}%", v => a.Opacity = v, Changed));
        p.Children.Add(Note("편집 모드에서 암수 창을 끌어 옮기고, 모서리를 끌어 크기를 바꿀 수도 있습니다."));
    }
}
