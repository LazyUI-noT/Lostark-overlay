using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 테마·업데이트's last rows, 버프 잠금 (buff-lock.design.md): the state, this PC's code to send for a key, and the key
/// box while locked. A key that fails says why and changes nothing.</summary>
public partial class SettingsWindow
{
    readonly StackPanel _buffLockRows = new();
    string _buffLockStatus = "";

    void BuildBuffLock()
    {
        _secApp.Children.Add(_buffLockRows);
        FillBuffLock();
    }

    void FillBuffLock()
    {
        var p = _buffLockRows;
        p.Children.Clear();
        p.Children.Add(Heading("버프 잠금"));
        p.Children.Add(Note("잠겨 있으면 DPS 미터기와 타임라인의 버프 적용률은 공증만 보이고, 85% 이상은 85+%로 보입니다. 타임라인의 아덴 피증은 "
            + "전투가 끝나고 10분 뒤에 % 없이 보이고, 초각스 피증은 보이지 않습니다. 버프 막대, 저장된 전투와 파티원에게 보내는 값은 그대로입니다."));
        bool locked = BuffUnlock.Locked;
        var state = !locked ? BuffKeyResult.열림 : BuffUnlock.Result == BuffKeyResult.열림 ? BuffKeyResult.없음 : BuffUnlock.Result;
        p.Children.Add(Row("상태", Label(state == BuffKeyResult.없음 || state == BuffKeyResult.열림 ? BuffKey.Message(state) : "저장된 키: " + BuffKey.Message(state))));
        if (!locked) return;
        if (BuffUnlock.PcCode is not { } pcCode)
        {
            p.Children.Add(Note("이 PC의 Windows 기기 ID를 읽지 못해 PC 코드를 만들 수 없습니다. 그래서 키도 넣을 수 없습니다."));
            return;
        }
        var copy = ApiButton("복사");
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(pcCode);_buffLockStatus = "PC 코드를 복사했습니다."; }
            catch (System.Runtime.InteropServices.COMException) { _buffLockStatus = "다른 프로그램이 클립보드를 쓰고 있습니다. 잠시 뒤 다시 눌러 주세요."; }
            FillBuffLock();
        };
        p.Children.Add(Row("PC 코드", Label(pcCode), copy));
        var box = new TextBox { Width = 320, MaxLength = 200, VerticalContentAlignment = VerticalAlignment.Center };
        var apply = ApiButton("적용");
        apply.Click += (_, _) => EnterBuffKey(box.Text);
        p.Children.Add(Row("키", box, apply));
        p.Children.Add(Note("PC 코드를 보내 키를 받으세요. 키는 만든 뒤 10시간 안에 넣어야 하고, 넣은 키는 이 PC에서 계속 쓰입니다(업데이트로 초기화될 때까지)."));
        if (_buffLockStatus.Length > 0) p.Children.Add(Note(_buffLockStatus));
    }

    void EnterBuffKey(string key)
    {
        var r = BuffUnlock.Enter(_s, key);
        _buffLockStatus = r == BuffKeyResult.열림 ? "" : BuffKey.Message(r);
        if (r == BuffKeyResult.열림)
        {
            Changed();
            _timeline.BuffLocked = BuffUnlock.Locked;
            _timeline.InvalidateMeasure();
            _timeline.InvalidateVisual();
        }
        FillBuffLock();
    }
}
