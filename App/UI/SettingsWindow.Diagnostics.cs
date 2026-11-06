using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The read-out table that was the debug window's, now under the screen on 화면·인식 → 감지: the picked box's rows
/// (<see cref="DetectPlace.TableOf"/>). It fills only while shown (<see cref="DiagnosticsShown"/>, which App passes to the engine as
/// Config.Debug), so it costs nothing otherwise.</summary>
public partial class SettingsWindow
{
    const string SecDiag = "진단";   // the table's name in 설정 검색 (and the tray): it finds 감지
    readonly DiagnosticsPanel _diag = new();
    bool _diagShown;

    /// <summary><see cref="DiagnosticsShown"/> may have changed.</summary>
    public event Action? DiagnosticsShownChanged;

    /// <summary>설정 is open on 화면·인식 → 감지 with a box picked that has table rows: the engine sends the rows.</summary>
    public bool DiagnosticsShown => IsVisible && _area.Name == AreaFit && _area.Shown == SecDetect && _detectPicked is { } id && DetectPlace.TableOf(id) is not null;

    void DiagnosticsMaybeChanged()
    {
        if (DiagnosticsShown == _diagShown) return;
        _diagShown = !_diagShown;
        DiagnosticsShownChanged?.Invoke();
    }

    /// <summary>Tray → 인식 도구 → 진단: 설정 opened on 감지 with a box that has rows picked (the skill bar's unless one is).</summary>
    public void ShowDiagnostics()
    {
        Show();
        Activate();
        if (_detectPicked is not { } id || DetectPlace.TableOf(id) is null) _detectPicked = "skill";
        Go(AreaFit, SecDetect);
        DrawDetect();
    }

    public void UpdateDiagnostics(SlotId slot, Image? crop, Reading? reading, SlotState? state) => _diag.Update(slot, crop, reading, state);

    public void UpdateDiagnostics(string key, Image crop, string reading, string state) => _diag.UpdateExtra(key, crop, reading, state);
}
