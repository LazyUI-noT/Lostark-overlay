namespace GaugeOverlay.Core.Domain;

/// <summary>설정's undo and redo: whole-settings snapshots (the settings file's JSON). The window records the snapshot
/// before each change, once per gesture (a drag's start, a slider press, a click), never per mouse move.</summary>
/// <remarks>Also capped by size (2026-11-04): a snapshot is the whole file, ~0.8 MB with 15 characters' emblems, so 100 of
/// them held ~160 MB. <paramref name="maxChars"/> counts the undo steps' characters; the newest step is always kept.</remarks>
public sealed class SettingsHistory(int capacity = 100, long maxChars = SettingsHistory.DefaultMaxChars)
{
    public const long DefaultMaxChars = 10_000_000;   // ~20 MB of UTF-16
    readonly LinkedList<string> _undo = new();
    long _chars;
    readonly Stack<string> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The settings as they are about to change. A new change drops what could be redone; the oldest step goes
    /// past <c>capacity</c>; the same snapshot twice in a row is one step.</summary>
    public void Record(string before)
    {
        if (_undo.Last?.Value == before) return;
        Add(before);
        _redo.Clear();
    }

    /// <summary>The snapshot to go back to, keeping <paramref name="current"/> for redo; null when there is none.</summary>
    public string? Undo(string current)
    {
        if (_undo.Last is not { } last) return null;
        _undo.RemoveLast();
        _chars -= last.Value.Length;
        _redo.Push(current);
        return last.Value;
    }

    /// <summary>The snapshot undone last, keeping <paramref name="current"/> for undo; null when there is none.</summary>
    public string? Redo(string current)
    {
        if (!_redo.TryPop(out var next)) return null;
        Add(current);
        return next;
    }

    void Add(string snapshot)
    {
        _undo.AddLast(snapshot);
        _chars += snapshot.Length;
        while (_undo.Count > 1 && (_undo.Count > capacity || _chars > maxChars))
        {
            _chars -= _undo.First!.Value.Length;
            _undo.RemoveFirst();
        }
    }
}
