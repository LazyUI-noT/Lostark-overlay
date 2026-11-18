namespace GaugeOverlay.Core.Domain;

/// <summary>The tray icon's tooltip: the profile, then in words what the icon shows by look alone (dimmed while the game
/// isn't running, a green dot during 파티 공유) (ui-audit-2026-11-18, P2).</summary>
public static class TrayTip
{
    /// <param name="profile">1-based, as the icon's number.</param>
    public static string Text(int profile, bool gameRunning, bool partyActive) =>
        $"GaugeOverlay · 프로필 {profile}" + (gameRunning ? "" : " · 게임 꺼짐") + (partyActive ? " · 파티 공유 중" : "");
}
