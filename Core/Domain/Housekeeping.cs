namespace GaugeOverlay.Core.Domain;

/// <summary>What the engine loop logs of an error (2026-11-04: one failing capture wrote its full trace twice a second for
/// hours, 13,000 lines): a new error in full, the same one again only as a count once a minute, and the loop waits longer
/// between tries while it repeats. Pure.</summary>
public sealed record ErrorLog(string? Last, int Repeats, TimeSpan SummaryAt, bool Repeating = false)
{
    public static readonly ErrorLog None = new(null, 0, TimeSpan.Zero);
    public static readonly TimeSpan SummaryEvery = TimeSpan.FromMinutes(1), FastPause = TimeSpan.FromMilliseconds(500), SlowPause = TimeSpan.FromSeconds(5);

    public enum Write { None, Full, Summary }

    /// <summary>What to write for this error: nothing, the error in full, or "repeated n times" (<see cref="Line.Repeats"/>).</summary>
    public sealed record Line(Write Write, int Repeats = 0);

    /// <summary>The wait before the next try: longer while the same error repeats.</summary>
    public TimeSpan Pause => Repeating ? SlowPause : FastPause;

    /// <summary>An error was caught at <paramref name="now"/>; <paramref name="key"/> tells errors apart (type and message).</summary>
    public (ErrorLog Next, Line Line) Seen(string key, TimeSpan now)
    {
        if (key != Last) return (new(key, 0, now), new(Write.Full));
        int repeats = Repeats + 1;   // since the last line written
        return now - SummaryAt >= SummaryEvery
            ? (this with { Repeats = 0, SummaryAt = now, Repeating = true }, new(Write.Summary, repeats))
            : (this with { Repeats = repeats, Repeating = true }, new(Write.None));
    }
}

/// <summary>The single-file build unpacks its native libraries to %TEMP%\.net\GaugeOverlay\&lt;hash&gt;, one folder per build
/// (23 of 8 MB on 2026-11-04) that nothing removed. Which of them to try to delete. Pure.</summary>
public static class ExtractFolders
{
    /// <summary>A folder this new may belong to a copy that is starting.</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromHours(1);

    /// <summary>The folders to try: not this run's (named in <paramref name="searchDirs"/>, the runtime's native search path)
    /// and not new. None when this run's folder isn't among them: then which one is ours is unknown. The caller still deletes a
    /// folder only after renaming it succeeds (another running copy keeps its own open).</summary>
    public static IReadOnlyList<string> Stale(IEnumerable<(string Path, DateTime Written)> folders, string? searchDirs, DateTime now)
    {
        var list = folders.ToList();
        var ours = (searchDirs ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.TrimEnd('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!list.Any(f => ours.Contains(f.Path.TrimEnd('\\', '/')))) return [];
        return [.. list.Where(f => !ours.Contains(f.Path.TrimEnd('\\', '/')) && now - f.Written >= MinAge).Select(f => f.Path)];
    }
}
