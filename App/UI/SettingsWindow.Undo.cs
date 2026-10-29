using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>Ctrl+Z / Ctrl+Y: whole-settings snapshots (<see cref="SettingsHistory"/>) with 편집 중's pick. Each change
/// records the state before it; changes closer than <see cref="MergeSeconds"/> are one step (a slider's drag, typing a
/// colour), a canvas drag is one step, and a profile switch or a 편집 중 pick is a step of its own.</summary>
public partial class SettingsWindow
{
    const double MergeSeconds = 0.6;
    const char Separator = '\u0001';   // between 편집 중's pick and the settings JSON in a snapshot

    readonly SettingsHistory _history = new();
    string? _baseline;   // the state after the last change: what the next change records
    DateTime _lastChange;

    string Snapshot() => (_classShown ?? "") + Separator + JsonSerializer.Serialize(_s, SettingsStore.Json);

    void BuildUndo()
    {
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || Keyboard.FocusedElement is TextBox || UiBuild.KeyFieldCapturing()) return;   // a text box keeps its own undo
            if (e.Key == Key.Z) { Step(undo: true); e.Handled = true; }
            else if (e.Key == Key.Y) { Step(undo: false); e.Handled = true; }
        };
    }

    /// <summary>After a change of a row: records the state before it, unless the last change was a moment ago.</summary>
    void RecordChange()
    {
        var now = DateTime.UtcNow;
        if (_baseline is not null && (now - _lastChange).TotalSeconds >= MergeSeconds) _history.Record(_baseline);
        _lastChange = now;
        _baseline = Snapshot();
    }

    /// <summary>Before a gesture (a canvas drag, a profile switch, a 편집 중 pick): the state now is a step.</summary>
    void RecordStep()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastChange).TotalSeconds >= MergeSeconds) _history.Record(_baseline ?? Snapshot());   // a held arrow key: one step
        _lastChange = now;
    }

    /// <summary>The state now becomes what the next change records (after a rebuild, a gesture's moves, a pick).</summary>
    void ResetBaseline()
    {
        _baseline = Snapshot();
    }

    void Step(bool undo)
    {
        var current = Snapshot();
        if ((undo ? _history.Undo(current) : _history.Redo(current)) is not { } target) return;
        int cut = target.IndexOf(Separator);
        if (JsonSerializer.Deserialize<Settings>(target[(cut + 1)..], SettingsStore.Json) is not { } snapshot) return;
        _s.CopyFrom(snapshot);
        _lastChange = default;   // the next edit is a step of its own
        string pick = target[..cut];
        (_classShown, _lastActive) = (pick == "" ? null : pick, ClassProfiles.ActiveKey(_s));   // BuildAll keeps the pick
        BuildAll();
        SettingsChanged?.Invoke();
        LayoutChanged?.Invoke();   // the profile, spots and sizes as they were
    }
}
