namespace GaugeOverlay.Core.Domain;

/// <summary>Where a setting's page shows the "프로필 N · 클래스" badge.</summary>
public enum BadgeAt { None, Tab, Section, Option }

/// <summary>Badge rule (class-profiles.plan.md, Intent F): one badge per tab that is all per profile, or per section / option
/// in a mostly global tab; never one per setting, and none on a section the inspector hosts (the 내 오버레이 tab's badge covers it).</summary>
public static class ProfileBadge
{
    public const string OverlayTab = "내 오버레이";
    public const string RaidCountNote = "줄 수 창 안내";

    /// <summary>Rail pages whose look card carries the badge: (area, section; null = the area's only section).</summary>
    static readonly (string Area, string? Section)[] SectionHomes =
    [
        ("스킬창", "칸 모양"), ("단심", null), ("버프 줄", null), ("암수", null), ("DPS 미터기", null), ("아덴 게이지", null),
        ("스킬창", "창 구성"),   // the 합쳐서 / 따로 switch (per profile and class): its own chip, not a look
    ];

    /// <summary>Where <paramref name="look"/> shows its badge: a rail page's card, else the 내 오버레이 tab (inspector-hosted).</summary>
    public static (string Area, string? Section) Home(LookGroup look) => look switch
    {
        LookGroup.스킬칸 => SectionHomes[0],
        LookGroup.단심 => SectionHomes[1],
        LookGroup.버프막대 or LookGroup.디버프 => SectionHomes[2],
        LookGroup.암수 => SectionHomes[3],
        LookGroup.미터기 => SectionHomes[4],
        LookGroup.게이지 => SectionHomes[5],
        LookGroup.판 or LookGroup.줄수창 => (OverlayTab, null),
        _ => throw new ArgumentOutOfRangeException(nameof(look), look, null),
    };

    public static BadgeAt Of(string area, string? section = null, string? option = null, bool inInspector = false)
    {
        if (inInspector) return BadgeAt.None;
        if (area == "레이드" && option == RaidCountNote) return BadgeAt.Option;
        if (area == OverlayTab) return BadgeAt.Tab;
        return SectionHomes.Any(h => h.Area == area && (h.Section is null || h.Section == section)) ? BadgeAt.Section : BadgeAt.None;
    }

    /// <summary>"프로필 2 · 포격 모드" for 편집 중 = "블래스터 · 포격 모드" (the 직업각인 only, edit-follows-pick.plan.md), "프로필 2 · 기본"
    /// for 기본. <paramref name="activeProfile"/> is 0-based.</summary>
    public static string Text(int activeProfile, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return $"프로필 {activeProfile + 1} · 기본";
        int sep = key.LastIndexOf(Separator, StringComparison.Ordinal);
        return $"프로필 {activeProfile + 1} · {(sep < 0 ? key : key[(sep + Separator.Length)..])}";
    }

    const string Separator = " · ";

    /// <summary>"프로필 2 공통": the badge of a setting every 직업각인 in the profile shares (버프 막대, 파티원 창 자리).</summary>
    public static string ProfileText(int activeProfile) => $"프로필 {activeProfile + 1} 공통";
}
