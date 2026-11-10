namespace GaugeOverlay.Core.Domain;

public sealed class HotkeySettings
{
    public string EditMode { get; set; } = "Ctrl+Shift+E";
    public string ToggleOverlay { get; set; } = "Ctrl+Shift+H";
    /// <summary>스킬창 다시 맞추기: fits the skill bar again (the 아덴 keys may have moved), then detects the character
    /// (class emblem and 초각스 icon). Named for what it did first, so a key set before still works.</summary>
    public string DetectCharacter { get; set; } = "Ctrl+Shift+D";
    /// <summary>테스트 모드: the window last in front (a video player) stands in for the game, which isn't looked for.</summary>
    public string TestMode { get; set; } = "Ctrl+Shift+T";
    /// <summary>직업각인 다시 읽기: reads the 직업각인 again, past the history; unset ("") by default.</summary>
    public string DetectSubclass { get; set; } = "";
    /// <summary>아이콘 다시 읽기: copies every slot's icon again (아이콘으로 표시); unset ("") by default.</summary>
    public string RereadIcons { get; set; } = "";
    /// <summary>오버레이 프로필 1–5; unset ("") by default.</summary>
    public string Profile1 { get; set; } = "";
    public string Profile2 { get; set; } = "";
    public string Profile3 { get; set; } = "";
    public string Profile4 { get; set; } = "";
    public string Profile5 { get; set; } = "";

    static readonly string[] ProfileActions = [nameof(Profile1), nameof(Profile2), nameof(Profile3), nameof(Profile4), nameof(Profile5)];

    /// <summary>Every action with its combo, by action name (nameof the property).</summary>
    public IReadOnlyList<(string Action, string Combo)> All() =>
    [
        (nameof(EditMode), EditMode), (nameof(ToggleOverlay), ToggleOverlay), (nameof(DetectCharacter), DetectCharacter),
        (nameof(DetectSubclass), DetectSubclass), (nameof(RereadIcons), RereadIcons),
        (nameof(TestMode), TestMode), (nameof(Profile1), Profile1), (nameof(Profile2), Profile2), (nameof(Profile3), Profile3),
        (nameof(Profile4), Profile4), (nameof(Profile5), Profile5),
    ];

    /// <summary>The profile (0-based) a ProfileN action switches to; null for the other actions.</summary>
    public static int? ProfileOf(string action) => Array.IndexOf(ProfileActions, action) is var i and >= 0 ? i : null;

    /// <summary>Actions whose combo another action or a skill key (<paramref name="skillKeys"/>) also uses: one of them
    /// wouldn't register.</summary>
    public IReadOnlyList<string> Clashes(IEnumerable<string> skillKeys)
    {
        var used = All().Select(a => a.Combo).Concat(skillKeys).Where(c => c != "").GroupBy(c => c)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return [.. All().Where(a => used.Contains(a.Combo)).Select(a => a.Action)];
    }
}
