namespace GaugeOverlay.Core.Domain;

/// <summary>오버레이 표시: 끄기 (hidden), 자동 (with the game or 설정 in front, <see cref="OverlayAuto"/>), 항상 (over any app).</summary>
public enum OverlayShow { 끄기, 자동, 항상 }

/// <summary>자동, with the game in front: 스킬창 while the skill bar is seen, 던전 while in a dungeon (the in-game 전투 분석기 or a boss
/// bar seen). 게임앞 is always: the old 켜기, kept for who had it and not offered once they pick another (user, 2026-11-05).</summary>
public enum OverlayAuto { 게임앞, 스킬창, 던전 }

/// <summary>When the overlay windows are on screen.</summary>
public static class OverlayVisibility
{
    /// <summary>Edit mode always shows them. Otherwise <paramref name="mode"/> decides: 자동 shows them while the game or one of
    /// the app's own windows (설정) is in front, whether or not the game runs; 항상 over any app. With the game in front they wait
    /// for the first run; 자동 follows <paramref name="auto"/>, 항상 doesn't.</summary>
    public static bool Show(bool editMode, OverlayShow mode, bool ownForeground, bool gameForeground, bool hudVisible,
        OverlayAuto auto, bool inDungeon, bool firstRunDone)
    {
        if (editMode) return true;
        if (mode == OverlayShow.끄기) return false;
        if (gameForeground) return firstRunDone && (mode == OverlayShow.항상 || auto switch
        {
            OverlayAuto.스킬창 => hudVisible,
            OverlayAuto.던전 => inDungeon,
            _ => true,
        });
        return ownForeground || (mode == OverlayShow.항상 && firstRunDone);
    }
}
