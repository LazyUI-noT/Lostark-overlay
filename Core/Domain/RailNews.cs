namespace GaugeOverlay.Core.Domain;

/// <summary>The rail's yellow orbs (settings-reorg.plan.md, order 14). Per tab, the keys that are there now (setting rows, the
/// shipped raid data, learned HP values, characters); a tab lights while one of its keys was never seen. A tab's keys are seen
/// when it is opened, and all of them at the first recording (so a first install lights nothing).</summary>
public static class RailNews
{
    /// <summary>The tabs with a key that was not seen; none before the first recording (<paramref name="seen"/> null).</summary>
    public static IReadOnlySet<string> Lit(IReadOnlyDictionary<string, List<string>> now, IReadOnlyDictionary<string, List<string>>? seen)
    {
        if (seen is null) return new HashSet<string>();
        return now.Where(tab => { var known = SeenKeys(seen, tab.Key); return tab.Value.Any(key => !known.Contains(key)); })
            .Select(tab => tab.Key).ToHashSet();
    }

    /// <summary>Everything there now counts as seen: the first recording.</summary>
    public static Dictionary<string, List<string>> Baseline(IReadOnlyDictionary<string, List<string>> now) =>
        now.ToDictionary(tab => tab.Key, tab => tab.Value.ToList());

    /// <summary><paramref name="tab"/> was opened: its keys are seen; every other tab keeps what it had.</summary>
    public static Dictionary<string, List<string>> Opened(IReadOnlyDictionary<string, List<string>> now, IReadOnlyDictionary<string, List<string>>? seen, string tab)
    {
        Dictionary<string, List<string>> result = seen?.ToDictionary(p => p.Key, p => p.Value.ToList()) ?? new();
        if (now.TryGetValue(tab, out var keys)) result[tab] = [.. SeenKeys(seen, tab).Union(keys)];
        return result;
    }

    /// <summary>Where news came from (user, 2026-11-10): an update (green: setting rows, raids.json, class support) or what the app
    /// found while playing (yellow: HP values, characters, their order).</summary>
    public enum NewsKind { Patch, App }

    const string HpPrefix = "hp:", CharPrefix = "char:", PlacePrefix = "place:";

    static bool Starts(string key, string prefix) => key.StartsWith(prefix, StringComparison.Ordinal);

    public static NewsKind KindOf(string key) =>
        Starts(key, HpPrefix) || Starts(key, CharPrefix) || Starts(key, PlacePrefix) ? NewsKind.App : NewsKind.Patch;

    /// <summary><see cref="Lit"/> by kind: each tab with an unseen key, once per kind it has.</summary>
    public static IReadOnlySet<(string Tab, NewsKind Kind)> LitKinds(IReadOnlyDictionary<string, List<string>> now, IReadOnlyDictionary<string, List<string>>? seen) =>
        now.Keys.SelectMany(tab => Unseen(now, seen, tab).Select(key => (tab, KindOf(key)))).ToHashSet();

    /// <summary>What lit <paramref name="tab"/>: its keys not seen yet, read before <see cref="Opened"/> records them, so the page
    /// can mark them while it shows (user, 2026-11-10: "I do not know what is new").</summary>
    public static IReadOnlyList<string> Unseen(IReadOnlyDictionary<string, List<string>> now, IReadOnlyDictionary<string, List<string>>? seen, string tab)
    {
        if (seen is null || !now.TryGetValue(tab, out var keys)) return [];
        var known = SeenKeys(seen, tab);
        return keys.Where(key => !known.Contains(key)).Distinct().ToList();
    }

    /// <summary>A setting row's key ("area/section/label"), as opposed to the other news: HP values, characters, places and the
    /// raids.json fingerprint (hex, no '/').</summary>
    public static bool IsRow(string key) => key.Contains('/') && KindOf(key) == NewsKind.Patch;

    /// <summary>The news that has no row to mark, said in words for the top of the page. Rows are marked in place, so left out.</summary>
    public static IReadOnlyList<(NewsKind Kind, string Line)> Describe(IEnumerable<string> keys)
    {
        var list = keys.Where(key => !IsRow(key)).ToList();
        var lines = new List<(NewsKind, string)>();
        if (list.Any(key => KindOf(key) == NewsKind.Patch)) lines.Add((NewsKind.Patch, "레이드 정보가 업데이트로 바뀌었습니다."));
        int hp = list.Count(key => Starts(key, HpPrefix));
        if (hp > 0) lines.Add((NewsKind.App, $"보스 체력 표에 새로 읽은 HP가 {hp}개 있습니다."));
        var names = list.Where(key => Starts(key, CharPrefix)).Select(key => key[CharPrefix.Length..]).ToList();
        if (names.Count > 0) lines.Add((NewsKind.App, $"새 캐릭터: {string.Join(", ", names)}"));
        if (list.Any(key => Starts(key, PlacePrefix))) lines.Add((NewsKind.App, "캐릭터 순서가 바뀌었습니다."));
        return lines;
    }

    /// <summary>The sections whose rows depend on the picked class, character, party member or canvas box, by the section names
    /// SettingsWindow.Search.cs gives them. Their rows change with the picking, not with an update, so they are no news.</summary>
    public static readonly IReadOnlySet<string> PickedSections = new HashSet<string> { "스킬 칸", "아덴 게이지", "파티원 모양", "단심", "버프 막대" };

    /// <summary>A row's key: its id without the " #n" that <c>SettingsRegistry</c> adds to a repeated row, so a row keeps its key when
    /// an earlier row of the same label appears or goes.</summary>
    public static string RowKey(string id)
    {
        int at = id.LastIndexOf('#');
        return at > 0 && at < id.Length - 1 && id[(at + 1)..].All(char.IsAsciiDigit) ? id[..at] : id;
    }

    /// <summary>The setting rows that count as news: their keys, each once, without the rows of <see cref="PickedSections"/>.</summary>
    public static IReadOnlyList<string> RowKeys(IEnumerable<string> registryIds) =>
        registryIds.Select(RowKey).Distinct().Where(key => !PickedSections.Contains(SectionOf(key))).ToList();

    static string SectionOf(string key) => key.Split('/') is [_, var section, ..] ? section : "";

    static HashSet<string> SeenKeys(IReadOnlyDictionary<string, List<string>>? seen, string tab) =>
        seen is not null && seen.TryGetValue(tab, out var keys) ? [.. keys] : [];
}
