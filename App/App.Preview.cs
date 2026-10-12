namespace GaugeOverlay.App;

/// <summary>미리보기: while 설정 is open, the windows show sample data, and my window shows the 직업각인 picked in 편집 중 when it
/// is not the character's (<see cref="UI.SettingsWindow.PreviewTarget"/>, with its saved icons). The character's own icons stay
/// in the renderer untouched, so closing 설정 is only clearing the target.</summary>
public partial class App
{
    /// <summary>미리보기: sample data while 설정 is open and 오버레이 표시 is on (it takes the foreground, so the overlay must show regardless),
    /// but not while the 레이아웃 편집기 is open, which draws the overlays itself.</summary>
    void UpdatePreview()
    {
        bool on = PreviewOn;
        ApplyPreviewTarget();
        _overlay.Preview = on;
        foreach (var w in Others) w.Preview = on;
        UpdateVisibility();
    }

    bool PreviewOn => _settingsWindow.IsVisible && _editor is null;

    /// <summary>My window shows the picked 직업각인's sample while previewing, else the live data. Never in edit mode: dragging
    /// there moves the character's own spot, which must be what is drawn.</summary>
    void ApplyPreviewTarget() =>
        _overlay.SetPreviewTarget(PreviewOn && !_overlay.EditMode ? _settingsWindow.PreviewTarget : null, _settingsWindow.PreviewIcons);
}
