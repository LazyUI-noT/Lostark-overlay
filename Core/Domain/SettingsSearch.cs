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
        return all.Select((e, i) => (e, i, rank: Rank(e, q)))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank).ThenBy(x => x.i)
            .Take(max).Select(x => x.e).ToList();
    }

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
