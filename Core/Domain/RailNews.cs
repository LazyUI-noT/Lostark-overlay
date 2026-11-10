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
