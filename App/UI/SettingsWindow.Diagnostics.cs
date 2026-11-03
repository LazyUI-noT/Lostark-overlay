using static GaugeOverlay.App.UI.UiBuild;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>화면·인식 → 진단: the read-out table that was the debug window's. It fills only while it is on screen
/// (<see cref="DiagnosticsShown"/>, which App passes to the engine as Config.Debug).</summary>
public partial class SettingsWindow
{
    const string SecDiag = "진단";
    readonly CardStack _secDiag = new();
    readonly DiagnosticsPanel _diag = new();

    /// <summary><see cref="DiagnosticsShown"/> may have changed.</summary>
    public event Action? DiagnosticsShownChanged;

    /// <summary>설정 is open on 화면·인식 → 진단: the engine sends the table's rows.</summary>
    public bool DiagnosticsShown => IsVisible && _area.Name == AreaFit && _area.Shown == SecDiag;

    /// <summary>Tray → 인식 도구 → 진단: 설정 opened on it.</summary>
    public void ShowDiagnostics()
    {
        Show();
        Activate();
        Go(AreaFit, SecDiag);
    }

    public void UpdateDiagnostics(SlotId slot, Image? crop, Reading? reading, SlotState? state) => _diag.Update(slot, crop, reading, state);

    public void UpdateDiagnostics(string key, Image crop, string reading, string state) => _diag.UpdateExtra(key, crop, reading, state);

    void FillDiag()
    {
        const string lead = "읽는 칸마다 화면에서 잘라 낸 그림, 읽은 값과 상태입니다. 인식이 이상할 때 어느 칸이 어떻게 읽히는지 봅니다. 이 화면을 보는 동안만 채웁니다.";
        var p = Refill(_secDiag, SecDiag, lead);
        Detach(_diag);
        p.Children.Add(_diag);
        UiBuild.Report?.Invoke(SecDiag, lead, _diag, null);   // the table has no labelled rows: search finds the section by its name
    }
}
