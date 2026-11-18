namespace GaugeOverlay.Core.Domain;

/// <summary>The notice shown once when 시작하기 closes, by 완료, 나중에 맞추기 or ✕ (status/ui-audit-2026-11-18.md): the
/// tray fact at the moment the window goes, and the way to fit later when no fit was made.</summary>
public static class FirstRun
{
    public const string TrayFact = "GaugeOverlay는 작업 표시줄 오른쪽 알림 영역에서 계속 실행됩니다. 아이콘을 두 번 누르면 설정이 열립니다.";

    public const string FitLater = "스킬창을 아직 맞추지 않았습니다. 게임에서 모든 스킬이 준비되면 설정 → 인게임 설정 → 스킬창 맞추기를 누르세요.";

    public static string ClosingNotice(bool fitted) => fitted ? TrayFact : $"{FitLater} {TrayFact}";
}
