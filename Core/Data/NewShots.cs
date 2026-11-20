using System.Text.Json;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>One 새 캡처: its PNG (a file name inside the folder), where it lay on screen (null: a backdrop) and whether the
/// list has been opened since it came.</summary>
public sealed record NewShot(string File, PixelRect? Area, bool Seen);

/// <summary>새 캡처 (preview-images.plan.md §4): the latest <see cref="Cap"/> instant captures, <c>screens\new\*.png</c> plus
/// <c>index.json</c> for their areas. Not part of <see cref="Settings"/> (no undo, not in snapshots). Newest first. Safe to
/// call from any thread (the capture key's file write runs off the UI thread).</summary>
public sealed class NewShots
{
    public const int Cap = 20;

    public static readonly string DefaultDir = Path.Combine(ScreenShots.Dir, "new");

    const string IndexName = "index.json";
    readonly string _dir;
    readonly object _gate = new();
    List<NewShot> _list = [];   // newest first

    public NewShots(string dir)
    {
        _dir = dir;
        Load();
    }

    /// <summary>Raised after the list changed (on the calling thread).</summary>
    public event Action? Changed;

    public IReadOnlyList<NewShot> Entries { get { lock (_gate) return [.. _list]; } }

    public int Unseen { get { lock (_gate) return _list.Count(e => !e.Seen); } }

    public string PathOf(NewShot shot) => Path.Combine(_dir, shot.File);

    /// <summary>Keeps <paramref name="png"/> as the newest shot (through a temp file); the oldest past <see cref="Cap"/> go.</summary>
    public void Add(byte[] png, PixelRect? area)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_dir);
            var name = $"{DateTime.UtcNow.Ticks}-{Guid.NewGuid():N}.png";
            Write(Path.Combine(_dir, name), png);
            _list.Insert(0, new NewShot(name, area, false));
            foreach (var old in _list.Skip(Cap)) TryDelete(PathOf(old));
            _list = [.. _list.Take(Cap)];
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>The list was opened: nothing is unseen any more.</summary>
    public void MarkSeen()
    {
        lock (_gate)
        {
            if (_list.All(e => e.Seen)) return;
            _list = [.. _list.Select(e => e with { Seen = true })];
            Save();
        }
        Changed?.Invoke();
    }

    public void DiscardAll()
    {
        lock (_gate)
        {
            foreach (var e in _list) TryDelete(PathOf(e));
            _list = [];
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>바꾸기: the shot's file becomes image <paramref name="slot"/> in <paramref name="slotsDir"/> (through a temp
    /// file), its area goes to <see cref="Settings.SlotAreas"/>, and the shot leaves the list. False when its file is gone
    /// (the shot is dropped) or the file can't be written (the shot stays).</summary>
    public bool PutInSlot(NewShot shot, int slot, string slotsDir, Settings settings)
    {
        bool put = false;
        lock (_gate)
        {
            var from = PathOf(shot);
            if (!File.Exists(from))
            {
                _list = [.. _list.Where(e => e.File != shot.File)];
                Save();
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(slotsDir);
                    Write(ScreenShots.PathOf(slotsDir, slot), File.ReadAllBytes(from));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
                settings.SlotAreas[slot] = shot.Area;
                TryDelete(from);
                _list = [.. _list.Where(e => e.File != shot.File)];
                Save();
                put = true;
            }
        }
        Changed?.Invoke();   // outside the lock, like the others
        return put;
    }

    void Load()
    {
        List<NewShot> indexed = [];
        try { indexed = JsonSerializer.Deserialize<List<NewShot>>(File.ReadAllText(Path.Combine(_dir, IndexName))) ?? []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }   // lost: areas go, pictures stay
        if (!Directory.Exists(_dir)) return;
        var onDisk = Directory.GetFiles(_dir, "*.png").Select(Path.GetFileName).OfType<string>().ToHashSet();
        var known = indexed.Where(e => e.File is { Length: > 0 } && onDisk.Contains(e.File)).DistinctBy(e => e.File).ToList();
        // A picture the index doesn't know (its write came after the last save): oldest by name, a backdrop, seen.
        var rest = onDisk.Except(known.Select(e => e.File)).Order(StringComparer.Ordinal).Select(f => new NewShot(f, null, true));
        _list = [.. known.Concat(rest).Take(Cap)];
    }

    /// <summary>Best effort: a lost index is rebuilt from the pictures on disk by <see cref="Load"/>.</summary>
    void Save()
    {
        try { Write(Path.Combine(_dir, IndexName), JsonSerializer.SerializeToUtf8Bytes(_list)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static void Write(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally { TryDelete(temp); }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
