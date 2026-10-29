namespace GaugeOverlay.Core.Domain;

/// <summary>설정's undo and redo: whole-settings snapshots (the settings file's JSON). The window records the snapshot
/// before each change, once per gesture (a drag's start, a slider press, a click), never per mouse move.</summary>
public sealed class SettingsHistory(int capacity = 100)
{
    readonly LinkedList<string> _undo = new();
    readonly Stack<string> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The settings as they are about to change. A new change drops what could be redone; the oldest step goes
    /// past <c>capacity</c>; the same snapshot twice in a row is one step.</summary>
    public void Record(string before)
    {
        if (_undo.Last?.Value == before) return;
        _undo.AddLast(before);
        if (_undo.Count > capacity) _undo.RemoveFirst();
        _redo.Clear();
    }

    /// <summary>The snapshot to go back to, keeping <paramref name="current"/> for redo; null when there is none.</summary>
    public string? Undo(string current)
    {
        if (_undo.Last is not { } last) return null;
        _undo.RemoveLast();
        _redo.Push(current);
        return last.Value;
    }

    /// <summary>The snapshot undone last, keeping <paramref name="current"/> for undo; null when there is none.</summary>
    public string? Redo(string current)
    {
        if (!_redo.TryPop(out var next)) return null;
        _undo.AddLast(current);
        return next;
    }
}
