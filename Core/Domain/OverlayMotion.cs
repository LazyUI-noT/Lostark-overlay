namespace GaugeOverlay.Core.Domain;

/// <summary>What a read-driven window's change of shown does (overlay-motion.plan.md): <see cref="None"/> nothing changed.</summary>
public enum WindowChange { None, Snap, FadeIn, FadeOut }

/// <summary>A read-driven window's inputs: <paramref name="Gate"/> everything but its read lets it show (오버레이 표시, the game
/// seen, the setting on), <paramref name="InUse"/> its read is on (or lingers), <paramref name="Placing"/> 편집 모드 shows it
/// regardless.</summary>
public readonly record struct ReadShown(bool Gate, bool InUse, bool Placing = false)
{
    public bool Shown => Gate && (InUse || Placing);
}

/// <summary>The in-game overlay's new motion (overlay-motion.plan.md; <c>.interface-design/overlay-motion.html</c>): the
/// game moves it, the player doesn't. Only its own read starting or a linger ending animates a window; a hotkey, Alt+Tab,
/// 편집 모드 or 오버레이 표시 never does, and a source that's gone snaps away. Whether it runs at all is
/// <see cref="Motion.Runs"/> (the App asks UiMotion.On). Pure.</summary>
public static class OverlayMotion
{
    /// <summary>Nothing arrives from transparent: readable from the first frame.</summary>
    public const double FadeFrom = 0.5;
    /// <summary>A window rises this many DIPs into place as it fades in.</summary>
    public const double Rise = 4;
    public const double InMs = 160, OutMs = 120, BarInMs = 120, MeterBarMs = 240;

    /// <summary>Hidden both times but the gate changed (Alt+Tab, a hotkey): <see cref="WindowChange.Snap"/>, so a fade-out
    /// under way ends at once.</summary>
    public static WindowChange Change(ReadShown was, ReadShown now, bool lingerEnded)
    {
        if (was.Shown == now.Shown) return !now.Shown && was.Gate != now.Gate ? WindowChange.Snap : WindowChange.None;
        bool byRead = was.Gate && now.Gate && !was.Placing && !now.Placing && was.InUse != now.InUse;
        if (!byRead) return WindowChange.Snap;
        return now.InUse ? WindowChange.FadeIn : lingerEnded ? WindowChange.FadeOut : WindowChange.Snap;
    }
}

/// <summary>A buff bar's arrival: when its buff last came on, to fade it in from <see cref="OverlayMotion.FadeFrom"/>. A bar
/// first seen on (<see cref="Unseen"/>: a window shown again, a new layout) is whole at once, as is one refreshed while on.</summary>
public readonly record struct Arrival(bool? WasOn, double StartMs)
{
    public static readonly Arrival Unseen = new(null, double.NaN);

    public Arrival Next(bool on, double ms, bool motion) =>
        on && WasOn == false && motion ? new(true, ms) : new(on, on ? StartMs : double.NaN);

    public double Opacity(double ms, double durationMs) =>
        Moving(ms, durationMs) ? OverlayMotion.FadeFrom + (1 - OverlayMotion.FadeFrom) * Motion.Out((ms - StartMs) / durationMs) : 1;

    public bool Moving(double ms, double durationMs) => !double.IsNaN(StartMs) && ms - StartMs < durationMs;
}

/// <summary>A value easing to its latest target along <see cref="Motion.Out"/> (the 미터기's bars); at rest it is exactly the
/// target, so a still bar draws as it did before easing.</summary>
public readonly record struct Glide(double From, double To, double StartMs)
{
    public static Glide At(double value) => new(value, value, double.NaN);

    public double Value(double ms, double durationMs) =>
        Moving(ms, durationMs) ? From + (To - From) * Motion.Out((ms - StartMs) / durationMs) : To;

    public bool Moving(double ms, double durationMs) => !double.IsNaN(StartMs) && ms - StartMs < durationMs;

    /// <summary>On to <paramref name="target"/> from where it is now; the same target keeps going, motion off is there at once.</summary>
    public Glide Toward(double target, double ms, double durationMs, bool motion) =>
        target == To ? this : motion ? new(Value(ms, durationMs), target, ms) : At(target);
}
