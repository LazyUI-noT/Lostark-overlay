namespace GaugeOverlay.Core.Domain;

/// <summary>The tray's Windows 알림 in three kinds (user, 2026-11-11), each with a switch on 설정 → 경고.</summary>
public enum NoticeKind
{
    /// <summary>What the app did or is about to do (a fit result, a mode switched, a tip).</summary>
    Guide,
    /// <summary>파티 공유: joined, left, refused, kicked.</summary>
    Party,
    /// <summary>Something failed or needs fixing (an error, a missing fit, a hotkey that can't be used).</summary>
    Problem,
}

/// <summary>설정 → 경고's Windows 알림 rows, and whether a notice of a kind shows (<see cref="Settings.MutedNotices"/>).</summary>
public static class Notices
{
    public static readonly IReadOnlyList<(NoticeKind Kind, string Name, string Help)> All =
    [
        (NoticeKind.Guide, "안내", "스킬창 맞추기 결과, 테스트 모드나 위치 프로필 전환처럼 앱이 한 일을 알려 줍니다."),
        (NoticeKind.Party, "파티", "공유 방 참가, 나가기, 거절, 내보내기를 알려 줍니다."),
        (NoticeKind.Problem, "문제", "오류, 맞춘 스킬창이 없을 때, 쓸 수 없는 단축키처럼 고쳐야 할 일을 알려 줍니다. 끄면 이런 문제는 로그 파일에만 남습니다."),
    ];

    public static bool Shows(Settings settings, NoticeKind kind) => !settings.MutedNotices.Contains(kind);
}
