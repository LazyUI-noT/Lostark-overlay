namespace GaugeOverlay.Core.Domain;

/// <summary>오버레이 표시: 끄기 (hidden), 켜기 (with the game or 설정 in front), 항상 (over any app).</summary>
public enum OverlayShow { 끄기, 켜기, 항상 }

/// <summary>When the overlay windows are on screen.</summary>
public static class OverlayVisibility
{
    /// <summary>Edit mode always shows them. Otherwise <paramref name="mode"/> decides: 켜기 shows them while the game or one of
    /// the app's own windows (설정) is in front, whether or not the game runs; 항상 over any app. With the game in front they wait
    /// for the first run, and hide with the skill bar unseen only when <paramref name="hideWithoutHud"/>.</summary>
    public static bool Show(bool editMode, OverlayShow mode, bool ownForeground, bool gameForeground, bool hudVisible,
        bool hideWithoutHud, bool firstRunDone)
    {
        if (editMode) return true;
        if (mode == OverlayShow.끄기) return false;
        if (gameForeground) return firstRunDone && (hudVisible || !hideWithoutHud);
        return ownForeground || (mode == OverlayShow.항상 && firstRunDone);
    }
}
