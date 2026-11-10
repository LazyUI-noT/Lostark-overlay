namespace GaugeOverlay.Core.Domain;

/// <summary>The warnings asked before an action (user, 2026-11-10), each with 다시 보지 않기 and a switch on 설정 → 경고.</summary>
public enum WarningKind
{
    /// <summary>보관 off on a fight past the newest N: it goes to the 휴지통.</summary>
    UnkeepToBin,
    /// <summary>An edit or a lowered 최근 기록 남기기 pushes fights to the 휴지통.</summary>
    PushToBin,
    /// <summary>삭제 inside the 휴지통: for good.</summary>
    EraseFromBin,
}

/// <summary>설정 → 경고's rows: each warning's name and what it guards.</summary>
public static class Warnings
{
    public static readonly IReadOnlyList<(WarningKind Kind, string Name, string Help)> All =
    [
        (WarningKind.UnkeepToBin, "보관을 풀 때", "최근 기록 수 밖의 전투는 보관을 풀면 바로 휴지통으로 갑니다."),
        (WarningKind.PushToBin, "편집 · 최근 기록 수를 줄일 때", "캐릭터를 옮기거나 개수를 줄여 밀려나는 전투가 휴지통으로 갈 때입니다."),
        (WarningKind.EraseFromBin, "휴지통에서 삭제할 때", "휴지통에서 지운 전투는 되돌릴 수 없습니다."),
    ];
}
