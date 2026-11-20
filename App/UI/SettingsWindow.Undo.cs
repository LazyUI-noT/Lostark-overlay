using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>Ctrl+Z / Ctrl+Y: whole-settings snapshots (<see cref="SettingsHistory"/>) with 편집 중's pick. Each change
/// records the state before it; changes closer than <see cref="ChangeRecorder.MergeSeconds"/> are one step (a slider's drag, typing a
/// colour), a canvas drag is one step, and a profile switch or a 편집 중 pick is a step of its own.</summary>
public partial class SettingsWindow
{
    const char Separator = '\u0001';   // between 편집 중's pick and the settings JSON in a snapshot

    readonly SettingsHistory _history = new();
    ChangeRecorder? _recorder;
    ChangeRecorder Recorder => _recorder ??= new(_history, Snapshot);

    string Snapshot() => (_classShown ?? "") + Separator + JsonSerializer.Serialize(_s, SettingsStore.Json);

    void BuildUndo()
    {
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is TextBox || UiBuild.KeyFieldCapturing()) return;   // a text box keeps its own undo
            var mods = Keyboard.Modifiers;
            bool redo = e.Key == Key.Y && mods == ModifierKeys.Control || e.Key == Key.Z && mods == (ModifierKeys.Control | ModifierKeys.Shift);
            if (e.Key == Key.Z && mods == ModifierKeys.Control)
            {
                if (!PlaceBack()) Step(undo: true);   // placing on the 감지 page: its own steps, not 설정's
                e.Handled = true;
            }
            else if (redo)
            {
                if (!_placeWanted) Step(undo: false);
                e.Handled = true;
            }
        };
        // A slider's press to release is one step, however long the hand rests (sliders are built in UiBuild and the game boxes).
        AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && InSlider(d)) Recorder.BeginGesture();
        }), handledEventsToo: true);
        AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => Recorder.EndGesture()), handledEventsToo: true);
        Deactivated += (_, _) => Recorder.EndGesture();
    }

    static bool InSlider(DependencyObject d)
    {
        for (; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is Slider) return true;
        return false;
    }

    /// <summary>After a change of a row: records the state before it, unless it belongs to the step in progress.</summary>
    void RecordChange() { Recorder.Change(); RefreshUndoBars(); }

    /// <summary>Before a gesture (a canvas drag, a profile switch, a 편집 중 pick): the state now is a step.</summary>
    void RecordStep() { Recorder.Step(); RefreshUndoBars(); }

    /// <summary>The state now becomes what the next change records (after a rebuild, a gesture's moves, a pick).</summary>
    void ResetBaseline() => Recorder.Reset();

    void Step(bool undo)
    {
        var current = Snapshot();
        if ((undo ? _history.Undo(current) : _history.Redo(current)) is not { } target) return;
        int cut = target.IndexOf(Separator);
        if (JsonSerializer.Deserialize<Settings>(target[(cut + 1)..], SettingsStore.Json) is not { } snapshot) return;
        _s.CopyFrom(snapshot);
        Recorder.Separate();   // the next edit is a step of its own
        _lastCopy = null;   // its snapshot no longer matches what the step put back
        string pick = target[..cut];
        (_classShown, _lastActive) = (pick == "" ? null : pick, ClassProfiles.ActiveKey(_s));   // BuildAll keeps the pick
        BuildAll();
        RefreshUndoBars();
        SettingsChanged?.Invoke();
        LayoutChanged?.Invoke();   // the profile, spots and sizes as they were
    }
}
