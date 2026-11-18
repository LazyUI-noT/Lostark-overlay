namespace GaugeOverlay.Core.Domain;

/// <summary>One searchable setting. <c>Path</c> is where it lives ("버프 › 모양"); <c>Box</c> is the canvas box kind to
/// select when it is picked, null for none.</summary>
public sealed record SettingEntry(string Id, string Label, string Path, string? Help = null, string? Box = null);

/// <summary>설정's search box: spaces and case don't count, and a label match outranks a path or help match.</summary>
public static class SettingsSearch
{
    public static IReadOnlyList<SettingEntry> Find(IEnumerable<SettingEntry> all, string query, int max = 20)
    {
        string q = Squash(query);
        if (q.Length == 0) return [];
        var qs = Queries(q);
        return all.Select((e, i) => (e, i, rank: qs.Select(x => Rank(e, x)).Where(r => r >= 0).DefaultIfEmpty(-1).Min()))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank).ThenBy(x => x.i)
            .Take(max).Select(x => x.e).ToList();
    }

    /// <summary>The words the word list (2026-11-16) replaced, so a player who types the old one still finds the row.</summary>
    static readonly (string Old, string New)[] OldWords =
    [
        ("감지", "읽는"), ("공유방", "파티공유"), ("편집중", "편집할직업각인"), ("초기화", "기본값으로"), ("되돌리기", "복원"), ("test", "테스트"),
        ("보스디버프크기", "타겟ui버프크기"), ("스킬창맞추기", "인게임설정"), ("표시할화면", "인게임설정"),
        ("레이드숙제", "원정대"), ("읽는자리", "화면인식설정"), ("화면·인식", "인게임설정"),   // rail-split.plan.md   // reading-place.plan.md A: both are 화면·인식 → 인게임 설정 now
    ];

    /// <summary>The query as typed, and with each old word swapped for its new one.</summary>
    static List<string> Queries(string q) =>
        [q, .. OldWords.Where(w => q.Contains(w.Old, StringComparison.Ordinal)).Select(w => q.Replace(w.Old, w.New, StringComparison.Ordinal))];

    /// <summary>0 label starts with it, 1 label contains it, 2 path, 3 help; -1 no match.</summary>
    static int Rank(SettingEntry e, string q)
    {
        string label = Squash(e.Label);
        if (label.StartsWith(q, StringComparison.Ordinal)) return 0;
        if (label.Contains(q, StringComparison.Ordinal)) return 1;
        if (Squash(e.Path).Contains(q, StringComparison.Ordinal)) return 2;
        if (e.Help is { } h && Squash(h).Contains(q, StringComparison.Ordinal)) return 3;
        return -1;
    }

    static string Squash(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
}
