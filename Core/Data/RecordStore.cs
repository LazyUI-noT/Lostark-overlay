using System.IO.Compression;
using System.Text.Json;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>Saved fights (spec § D Keeping): one gzip JSON file per record in <paramref name="dir"/>, named by its start, gate and
/// class ("yyyyMMdd-HHmmss_gate_class subclass", "-n" when taken; the date first keeps the names in time order). Saving prunes
/// to the last N automatic records (the user's choice, <see cref="MinKeep"/>–<see cref="MaxKeep"/>); 보관 ones stay until
/// deleted. Files that can't be read (cut short, hand-edited, too big) are left alone and skipped.</summary>
public sealed class RecordStore(string dir)
{
    public const int Keep = 50, MinKeep = 1, MaxKeep = 50;
    const string Ext = ".json.gz";
    const long MaxBytes = 64L << 20;   // a 10-minute 8-player fight is ~300 KB unpacked
    const double MaxSeconds = 24 * 3600;
    readonly object _gate = new();   // a background save and the UI's 보관 / 삭제 take turns
    // Parsed records by file, kept while the file's length and write time don't change: the 전투 기록 page lists every
    // character's fights (hundreds of files) on each refresh.
    readonly Dictionary<string, (long Length, DateTime Written, FightRecord Record)> _parsed = [];

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GaugeOverlay", "records");

    /// <summary>Writes the record and prunes to the last <paramref name="keep"/> automatic ones; returns its file name.</summary>
    public string Save(FightRecord r, int keep = Keep)
    {
        lock (_gate) return SaveLocked(r, Math.Clamp(keep, MinKeep, MaxKeep));
    }

    /// <summary>자동 저장 off: the last fight, held in one file the next fight overwrites (listed first: "last" sorts after the
    /// dates, newest first). It is not counted or pruned; 보관 saves it under its own name.</summary>
    public const string Held = "last" + Ext;

    public static bool IsHeld(string file) => file == Held;

    public void Hold(FightRecord r)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(dir);
            Write(Held, r with { Kept = false });
        }
    }

    /// <summary>The start, then the gate, the difficulty and the class when known, as a plain file name.</summary>
    static string Stem(FightRecord r)
    {
        string cls = string.Join(' ', new[] { r.MyClassName, r.MySubclass }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var parts = new[] { r.StartedAt.ToString("yyyyMMdd-HHmmss"), r.Gate, r.Difficulty, cls }.Where(x => !string.IsNullOrWhiteSpace(x));
        var bad = Path.GetInvalidFileNameChars();
        return new string(string.Join('_', parts).Select(c => bad.Contains(c) ? '-' : c).ToArray()).Replace("..", "-");
    }

    string SaveLocked(FightRecord r, int keep)
    {
        Directory.CreateDirectory(dir);
        foreach (var tmp in Directory.EnumerateFiles(dir, "*" + Ext + ".tmp")) File.Delete(tmp);   // left by a crash mid-write
        string stem = Stem(r), name = stem + Ext;
        for (int n = 2; File.Exists(Path.Combine(dir, name)); n++) name = $"{stem}-{n}{Ext}";
        Write(name, r);
        string mine = RecordCharacter.Of(r);   // per character: another character's fights never push these out
        foreach (var (file, _) in List().Where(x => !x.Record.Kept && !IsHeld(x.File) && RecordCharacter.Of(x.Record) == mine).Skip(keep))
            File.Delete(Path.Combine(dir, file));
        return name;
    }

    /// <summary>Every readable record, newest first.</summary>
    public IReadOnlyList<(string File, FightRecord Record)> List()
    {
        if (!Directory.Exists(dir)) return [];
        lock (_gate)
        {
            var files = Directory.EnumerateFiles(dir, "*" + Ext).Select(Path.GetFileName).OfType<string>()
                .OrderByDescending(f => f, StringComparer.Ordinal).ToList();
            foreach (var gone in _parsed.Keys.Except(files).ToList()) _parsed.Remove(gone);
            return [.. files.Select(f => (File: f, Record: Cached(f))).Where(x => x.Record is not null).Select(x => (x.File, x.Record!))];
        }
    }

    public void SetKept(string file, bool kept)
    {
        lock (_gate)
        {
            if (Read(Checked(file)) is not { } r) return;
            if (!IsHeld(file)) Write(file, r with { Kept = kept });
            else if (kept)
            {
                SaveLocked(r with { Kept = true }, MaxKeep);   // the held fight becomes a saved one
                File.Delete(Path.Combine(dir, Held));
            }
        }
    }

    /// <summary>A saved fight's 클리어 / 실패, settled after it was written (FightVerdict: the next 전분 record's boss).</summary>
    public void SetCleared(string file, bool cleared)
    {
        lock (_gate)
            if (File.Exists(Path.Combine(dir, Checked(file))) && Read(file) is { } r) Write(file, r with { Cleared = cleared });
    }

    public void Delete(string file)
    {
        lock (_gate) File.Delete(Path.Combine(dir, Checked(file)));
    }

    /// <summary>Only plain names from <see cref="List"/>: never a path out of the folder.</summary>
    static string Checked(string file) =>
        file == Path.GetFileName(file) && file.EndsWith(Ext, StringComparison.Ordinal) && !file.Contains("..")
            ? file : throw new ArgumentException($"not a record file: {file}", nameof(file));

    void Write(string file, FightRecord r)
    {
        string path = Path.Combine(dir, file), tmp = path + ".tmp";
        using (var gz = new GZipStream(File.Create(tmp), CompressionLevel.Optimal))
            JsonSerializer.Serialize(gz, r, SettingsStore.Json);
        File.Move(tmp, path, overwrite: true);
    }

    FightRecord? Cached(string file)
    {
        var info = new FileInfo(Path.Combine(dir, file));
        if (!info.Exists) { _parsed.Remove(file); return null; }   // deleted between the listing and here
        if (_parsed.TryGetValue(file, out var c) && c.Length == info.Length && c.Written == info.LastWriteTimeUtc) return c.Record;
        var r = Read(file);
        if (r is null) _parsed.Remove(file);
        else _parsed[file] = (info.Length, info.LastWriteTimeUtc, r);
        return r;
    }

    FightRecord? Read(string file)
    {
        try
        {
            using var gz = new GZipStream(File.OpenRead(Path.Combine(dir, file)), CompressionMode.Decompress);
            using var json = new MemoryStream();
            var buffer = new byte[81920];
            for (int n; (n = gz.Read(buffer)) > 0;)
            {
                json.Write(buffer, 0, n);
                if (json.Length > MaxBytes) return null;
            }
            json.Position = 0;
            var r = JsonSerializer.Deserialize<FightRecord>(json, SettingsStore.Json);
            return r is { Me: not null, Phases: not null, Members: not null, Samples: not null, Boss: not null, Events: not null }
                && double.IsFinite(r.Duration) && r.Duration is >= 0 and <= MaxSeconds
                && r.Phases.All(p => double.IsFinite(p) && p >= 0 && p <= r.Duration)
                && r.Members.All(m => m?.Name is not null) && r.Samples.All(s => s?.Name is not null)
                && r.Events.All(e => e?.Name is not null) && !r.Boss.Contains(null!)
                ? r : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
