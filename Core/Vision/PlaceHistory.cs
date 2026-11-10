using System.Collections.Immutable;

namespace GaugeOverlay.Core.Vision;

/// <summary>The 감지 page's 되돌리기: the edits a placing session can go back over, one per finished drag or 처음으로, the oldest
/// forgotten past <see cref="MaxSteps"/>. It lives as long as the session. Immutable.</summary>
public sealed record PlaceHistory
{
    public const int MaxSteps = 20;
    public static readonly PlaceHistory Empty = new();

    ImmutableList<FitAdjust> Steps { get; init; } = [];

    public bool CanGoBack => !Steps.IsEmpty;

    /// <summary>With the edit <paramref name="before"/> → <paramref name="now"/> as a step; one that changed nothing (a plain click) is none.</summary>
    public PlaceHistory After(FitAdjust before, FitAdjust now)
    {
        if (before == now) return this;
        var steps = Steps.Add(before);
        return this with { Steps = steps.Count > MaxSteps ? steps.RemoveAt(0) : steps };
    }

    /// <summary>The edit to go back to and the history without it; null with nothing to go back to.</summary>
    public (FitAdjust Step, PlaceHistory History)? Back() =>
        Steps.IsEmpty ? null : (Steps[^1], this with { Steps = Steps.RemoveAt(Steps.Count - 1) });
}
