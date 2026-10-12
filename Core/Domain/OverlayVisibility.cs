namespace GaugeOverlay.Core.Domain;

/// <summary>When the overlay windows are on screen.</summary>
public static class OverlayVisibility
{
    /// <summary>Edit mode always shows them. Otherwise 오버레이 표시/숨기기 (<paramref name="visible"/>) decides, also while 설정 is open
    /// (<paramref name="preview"/>): then they show whatever is in front, with sample data when nothing is live. Without 설정 they
    /// show with the game in front, the HUD up and the first run done.</summary>
    public static bool Show(bool editMode, bool preview, bool visible, bool gameForeground, bool hudVisible, bool firstRunDone) =>
        editMode || (visible && (preview || (gameForeground && hudVisible && firstRunDone)));
}
