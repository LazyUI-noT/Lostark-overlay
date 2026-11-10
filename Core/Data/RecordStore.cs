using System.IO.Compression;
using System.Text.Json;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>Saved fights (spec § D Keeping): one gzip JSON file per record in <paramref name="dir"/>, named by its start, gate and
/// class ("yyyyMMdd-HHmmss_gate_class subclass", "-n" when taken; the date first keeps the names in time order). Saving prunes
/// each character to its last N records (the user's choice, <see cref="MinKeep"/>–<see cref="MaxKeep"/>), 보관 ones counted;
/// a 보관 one past them stays until deleted. Files that can't be read (cut short, hand-edited, too big) are left alone and skipped.
/// 휴지통 (user, 2026-11-10): nothing is deleted outright. A pruned, un-kept past N or deleted fight moves to <see cref="Bin"/>
/// (the <see cref="BinDir"/> folder, which older versions don't list), whose own 삭제 and weekly emptying erase it.</summary>
public sealed class RecordStore(string dir, bool isBin = false)
{
    public const string BinDir = "bin";
    readonly RecordStore? _bin = isBin ? null : new(Path.Combine(dir, BinDir), isBin: true);   // made up front: one gate however threads first reach it

    /// <summary>The fights moved out of this store, each file written at the time it entered (<see cref="EmptyBin"/>).</summary>
    public RecordStore Bin => _bin ?? throw new InvalidOperationException("no bin in the bin");

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

    long _edits;   // bumped by every write and delete here: the folder's write time can stay put across two quick edits

    /// <summary>Moves on every save, hold, 보관, edit and delete, so a reader that keys on it sees the fights change without
    /// being told: the folder's write time (also catches files changed from outside) plus this store's own edit count; both
    /// only grow. 0 before the folder exists and nothing was written.</summary>
    public long Changed => (Directory.Exists(dir) ? Directory.GetLastWriteTimeUtc(dir).Ticks : 0) + Interlocked.Read(ref _edits);

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
        var parts = new[] { r.StartedAt.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture), r.Gate, r.Difficulty, cls }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var bad = Path.GetInvalidFileNameChars();
        return new string(string.Join('_', parts).Select(c => bad.Contains(c) ? '-' : c).ToArray()).Replace("..", "-");
    }

    string SaveLocked(FightRecord r, int keep, string? replacing = null)
    {
        string name = WriteLocked(r, replacing);
        Prune(RecordCharacter.Of(r), keep);
        return name;
    }

    string WriteLocked(FightRecord r, string? replacing)
    {
        Directory.CreateDirectory(dir);
        foreach (var tmp in Directory.EnumerateFiles(dir, "*" + Ext + ".tmp")) File.Delete(tmp);   // left by a crash mid-write
        string stem = Stem(r), name = stem + Ext;
        for (int n = 2; name != replacing && File.Exists(Path.Combine(dir, name)); n++) name = $"{stem}-{n}{Ext}";
        Write(name, r);
        if (replacing is not null && replacing != name) File.Delete(Path.Combine(dir, replacing));   // only once the new one is written
        return name;
    }

    /// <summary>The character's newest <paramref name="keep"/> saved fights stay, 보관 ones among them taking a place (user,
    /// 2026-11-10); past them only 보관 ones stay, the rest go to the bin, an edited fight moved there too. Per character:
    /// another character's fights never push these out.</summary>
    void Prune(string character, int keep)
    {
        var mine = List().Where(x => !IsHeld(x.File) && RecordCharacter.Of(x.Record) == character).ToList();
        foreach (var (file, _) in Overflow(mine, _ => false, x => x.Record.Kept, keep)) ToBin(file);
    }

    /// <summary>최근 기록 남기기 lowered: every character's fights past the new number go to the bin at once.</summary>
    public void PruneAll(int keep)
    {
        lock (_gate)
            foreach (var character in List().Select(x => RecordCharacter.Of(x.Record)).Distinct().ToList()) Prune(character, Clamped(keep));
    }

    /// <summary>Once, after the update from 0.19 (N counted only unkept fights, 보관 ones extra): every fight the new count would
    /// bin turns 보관, so the update moves nothing (user, 2026-11-10). Returns how many.</summary>
    public int KeepOverflow(int keep)
    {
        lock (_gate)
        {
            var past = List().Where(x => !IsHeld(x.File)).GroupBy(x => RecordCharacter.Of(x.Record))
                .SelectMany(g => Overflow(g.ToList(), _ => false, x => x.Record.Kept, keep)).ToList();
            foreach (var (file, r) in past) Write(file, r with { Kept = true });
            return past.Count;
        }
    }

    /// <summary>How many fights <see cref="PruneAll"/> with <paramref name="keep"/> would move: asked before, to warn.</summary>
    public int OverflowCount(int keep) =>
        List().Where(x => !IsHeld(x.File)).GroupBy(x => RecordCharacter.Of(x.Record))
            .Sum(g => Overflow(g.ToList(), _ => false, x => x.Record.Kept, keep).Count);

    /// <summary>Whether 보관 off on <paramref name="file"/> sends it to the bin (past the newest <paramref name="keep"/>).</summary>
    public bool UnkeepWouldBin(string file, int keep)
    {
        var all = List();
        if (IsHeld(file) || all.FirstOrDefault(x => x.File == file).Record is not { } me) return false;
        var mine = all.Where(x => !IsHeld(x.File) && RecordCharacter.Of(x.Record) == RecordCharacter.Of(me)).ToList();
        return Overflow(mine, _ => false, x => x.File != file && x.Record.Kept, keep).Any(x => x.File == file);
    }

    /// <summary>Whether <see cref="Edit"/> with these would push a fight (the edited one too) to the bin: asked before, to warn.</summary>
    public bool WouldBin(string file, FightRecord edited, int keep)
    {
        if (IsHeld(file)) return false;
        string character = RecordCharacter.Of(edited), name = Stem(edited) + Ext;
        var after = List().Where(x => !IsHeld(x.File) && x.File != file && RecordCharacter.Of(x.Record) == character)
            .Select(x => (x.File, x.Record.Kept)).Append((File: name, edited.Kept)).OrderByDescending(x => x.File, StringComparer.Ordinal).ToList();
        return Overflow(after, _ => false, x => x.Kept, keep).Count > 0;
    }

    /// <summary>Moves a file to the bin under a free name, written now (when it entered); in the bin itself, erases it.</summary>
    void ToBin(string file)
    {
        Interlocked.Increment(ref _edits);
        if (isBin) { File.Delete(Path.Combine(dir, file)); return; }
        string binDir = Path.Combine(dir, BinDir), stem = file[..^Ext.Length], name = file, from = Path.Combine(dir, file);
        lock (Bin._gate)   // store gate, then bin gate: the order everywhere. An emptying never sees it half moved.
        {
            Directory.CreateDirectory(binDir);
            for (int n = 2; File.Exists(Path.Combine(binDir, name)); n++) name = $"{stem}-{n}{Ext}";
            File.SetLastWriteTimeUtc(from, DateTime.UtcNow);   // stamped before the move: never in the bin with its old time
            File.Move(from, Path.Combine(binDir, name));
            Interlocked.Increment(ref Bin._edits);
        }
    }

    /// <summary>휴지통's 보관: the fight comes back to this store as 보관, its start time as recorded; returns its file name.</summary>
    public string Restore(string binFile, int keep = Keep)
    {
        lock (_gate)
        {
            var bin = Bin;
            if (bin.Read(Checked(binFile)) is not { } r) throw new FileNotFoundException("the record is gone", binFile);
            var restored = r with { Kept = true };
            string name = WriteLocked(restored, null);
            bin.Erase(binFile);   // before the prune: a prune that fails never leaves the fight in both places
            Prune(RecordCharacter.Of(restored), Clamped(keep));
            return name;
        }
    }

    /// <summary>Erases every bin file that entered before <paramref name="cutoffUtc"/> (the last emptying time); returns how many.</summary>
    public int EmptyBin(DateTime cutoffUtc)
    {
        var bin = Bin;
        string binDir = Path.Combine(dir, BinDir);
        if (!Directory.Exists(binDir)) return 0;
        lock (bin._gate)
        {
            var old = Directory.EnumerateFiles(binDir, "*" + Ext).Where(f => File.GetLastWriteTimeUtc(f) < cutoffUtc).ToList();
            foreach (var f in old) File.Delete(f);
            if (old.Count > 0) Interlocked.Increment(ref bin._edits);
            return old.Count;
        }
    }

    // The tabs and the ! mark (user, 2026-11-10). newestFirst: one character's fights as List gives them; held: the 자동 저장 off
    // fight (never counted or pruned); keep: the user's N.

    /// <summary>최근: the held fight, then the newest <paramref name="keep"/> saved ones, 보관 or not.</summary>
    public static IReadOnlyList<T> Recent<T>(IReadOnlyList<T> newestFirst, Func<T, bool> held, int keep) =>
        [.. newestFirst.Where(held), .. Saved(newestFirst, held).Take(Clamped(keep))];

    /// <summary>The unkept fights past the newest <paramref name="keep"/>: what <see cref="Prune"/> moves to the bin.</summary>
    public static IReadOnlyList<T> Overflow<T>(IReadOnlyList<T> newestFirst, Func<T, bool> held, Func<T, bool> kept, int keep) =>
        [.. Saved(newestFirst, held).Where((x, i) => !kept(x) && i >= Clamped(keep))];

    /// <summary>The fights the next save moves to the bin (the new one takes a place), marked ! so none goes by surprise: an
    /// unkept <paramref name="keep"/>th.</summary>
    public static IReadOnlyList<T> GoesNext<T>(IReadOnlyList<T> newestFirst, Func<T, bool> held, Func<T, bool> kept, int keep) =>
        [.. Saved(newestFirst, held).Where((x, i) => !kept(x) && i >= Clamped(keep) - 1)];

    static IEnumerable<T> Saved<T>(IEnumerable<T> newestFirst, Func<T, bool> held) => newestFirst.Where(x => !held(x));
    static int Clamped(int keep) => Math.Clamp(keep, MinKeep, MaxKeep);

    /// <summary>전투 기록 편집: the record rewritten as edited, under the name its new gate, difficulty and class give (the held
    /// fight stays held). Moved to another character, that one's oldest automatic records make room (<paramref name="keep"/>).
    /// Returns its file name.</summary>
    public string Edit(string file, FightRecord edited, int keep = Keep)
    {
        lock (_gate)
        {
            if (!File.Exists(Path.Combine(dir, Checked(file)))) throw new FileNotFoundException("the record is gone", file);
            if (IsHeld(file)) { Write(Held, edited with { Kept = false }); return Held; }
            return SaveLocked(edited, Math.Clamp(keep, MinKeep, MaxKeep), replacing: file);
        }
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

    /// <summary>보관 on or off. Off on a fight past the newest <paramref name="keep"/>, it goes to the bin at once.</summary>
    public void SetKept(string file, bool kept, int keep = Keep)
    {
        lock (_gate)
        {
            if (Read(Checked(file)) is not { } r) return;
            if (!IsHeld(file))
            {
                Write(file, r with { Kept = kept });
                if (!kept) Prune(RecordCharacter.Of(r), Clamped(keep));
            }
            else if (kept)
            {
                SaveLocked(r with { Kept = true }, Clamped(keep));   // the held fight becomes a saved one
                File.Delete(Path.Combine(dir, Held));
            }
        }
    }

    /// <summary>A saved fight's 클리어 / 실패, settled after it was written (FightVerdict: the next 전분 record's boss). Returns the
    /// record as written (null: gone or unreadable).</summary>
    public FightRecord? SetCleared(string file, bool cleared)
    {
        lock (_gate)
        {
            if (!File.Exists(Path.Combine(dir, Checked(file))) || Read(file) is not { } r) return null;
            var settled = r with { Cleared = cleared };
            Write(file, settled);
            return settled;
        }
    }

    /// <summary>삭제: to the bin (in the bin: for good). Gone already: nothing happens.</summary>
    public void Delete(string file)
    {
        lock (_gate)
            if (File.Exists(Path.Combine(dir, Checked(file)))) ToBin(file);
    }

    /// <summary>For good, past the bin: a fight that moved to another store, or one written by mistake.</summary>
    public void Erase(string file)
    {
        lock (_gate)
        {
            File.Delete(Path.Combine(dir, Checked(file)));
            Interlocked.Increment(ref _edits);
        }
    }

    /// <summary>A record read from a file (ours, or an imported one) has all its parts, finite times and events of things the
    /// timeline knows (an unknown buff or bomb id would throw where it is looked up): the pages can draw it.</summary>
    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] FightRecord? r) =>
        r is { Me: not null, Phases: not null, Members: not null, Samples: not null, Boss: not null, Events: not null }
        && double.IsFinite(r.Duration) && r.Duration is >= 0 and <= MaxSeconds
        && r.Phases.All(p => double.IsFinite(p) && p >= 0 && p <= r.Duration)
        && r.Members.All(m => m?.Name is not null) && r.Samples.All(s => s?.Name is not null && double.IsFinite(s.T))
        && r.Events.All(e => e?.Name is not null && double.IsFinite(e.T) && Known(e)) && r.Boss.All(b => b is not null && double.IsFinite(b.T))
        && (r.Skills ?? new Dictionary<SlotId, RecordSkill>()).All(s => Enum.IsDefined(s.Key) && Skill(s.Value))
        && (r.Unsure ?? new Dictionary<SlotId, IReadOnlyList<RecordSkill>>()).All(s => Enum.IsDefined(s.Key) && s.Value is not null && s.Value.All(Skill));

    /// <summary>A slot's skill (0.19): a name, a kind it can be and an icon path that stays a Smilegate CDN file.</summary>
    static bool Skill(RecordSkill? s) => s is { Name: not null } && Enum.IsDefined(s.Kind) && SkillIconSource.IsValid(s.Icon);

    static bool Known(RecordEvent e) => e.Kind switch
    {
        RecordEventKind.사용 => Enum.IsDefined((SlotId)e.What),
        RecordEventKind.버프시작 or RecordEventKind.버프끝 => Enum.IsDefined((BuffKind)e.What),
        RecordEventKind.암수시작 or RecordEventKind.암수끝 => Enum.IsDefined((Vision.Bomb)e.What),
        RecordEventKind.대난투입장 or RecordEventKind.대난투종료 => true,
        _ => false,
    };

    /// <summary>Gives a saved fight its character (불명 fights once the character is known), read and written under the lock so a
    /// result settled meanwhile (<see cref="SetCleared"/>) stays. Returns the record as written (null: gone or unreadable).</summary>
    public FightRecord? SetCharacter(string file, string name)
    {
        lock (_gate)
        {
            if (!File.Exists(Path.Combine(dir, Checked(file))) || Read(file) is not { } r) return null;
            var named = r with { Character = name };
            Write(file, named);
            return named;
        }
    }

    /// <summary>The most a record or an export may unpack to (a 10-minute 8-player fight is ~300 KB).</summary>
    public const long MaxUnpacked = MaxBytes;

    /// <summary>Only plain names from <see cref="List"/>: never a path out of the folder.</summary>
    static string Checked(string file) =>
        file == Path.GetFileName(file) && file.EndsWith(Ext, StringComparison.Ordinal) && !file.Contains("..")
            ? file : throw new ArgumentException($"not a record file: {file}", nameof(file));

    void Write(string file, FightRecord r)
    {
        Interlocked.Increment(ref _edits);
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
            return IsValid(r) ? r : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
