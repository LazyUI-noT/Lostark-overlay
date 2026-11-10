namespace GaugeOverlay.Core.Data;

/// <summary>HP 변경 감지's pictures (user, 2026-11-09): the boss HP text row a found value was read from, one PNG per
/// (gate, difficulty, HP) in <c>hp-crops\</c> beside settings.json. Never sent anywhere; deleted once its row leaves the list.</summary>
public static class HpCrops
{
    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GaugeOverlay", "hp-crops");

    const string Ext = ".png";

    public static string FileName(string gateId, string difficulty, long hp) => $"{Safe(gateId)}_{Safe(difficulty)}_{hp}{Ext}";

    static string Safe(string part) => string.Concat(part.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '_' ? '-' : c));

    public static string PathOf(string gateId, string difficulty, long hp) => Path.Combine(Dir, FileName(gateId, difficulty, hp));

    /// <summary>Drops the crop of a row that left the list (확정, 삭제).</summary>
    public static void Delete(string gateId, string difficulty, long hp) => Try(() => File.Delete(PathOf(gateId, difficulty, hp)));

    /// <summary>Drops every crop whose value isn't listed. At start-up only: the engine may be saving one while a raid runs.</summary>
    public static void Clean(IEnumerable<(string Gate, string Difficulty, long Hp)> listed, string? dir = null)
    {
        dir ??= Dir;
        if (!Directory.Exists(dir)) return;
        foreach (var f in Stale(Directory.EnumerateFiles(dir).Select(Path.GetFileName).OfType<string>(), listed))
            Try(() => File.Delete(Path.Combine(dir, f)));
    }

    static void Try(Action io)
    {
        try { io(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine($"[raid] HP crop: {e.Message}"); }
    }

    /// <summary>The crop file names among <paramref name="files"/> whose value isn't in <paramref name="listed"/>.</summary>
    public static IReadOnlyList<string> Stale(IEnumerable<string> files, IEnumerable<(string Gate, string Difficulty, long Hp)> listed)
    {
        var keep = listed.Select(l => FileName(l.Gate, l.Difficulty, l.Hp)).ToHashSet();
        return [.. files.Where(f => f.EndsWith(Ext, StringComparison.OrdinalIgnoreCase) && !keep.Contains(f))];
    }
}
