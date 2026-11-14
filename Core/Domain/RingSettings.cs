namespace GaugeOverlay.Core.Domain;

/// <summary>기력·충격 창 (breaker-aden.plan.md phase 5): 브레이커's ring round the orb as one bar in its own window, the left half
/// 기력 (the ring's yellow half), the right half 충격 (its green half), each filling from the middle outward as the ring's
/// halves fill from the bottom up. Yours only, never sent. Shown for a class with a ring (<see cref="Data.RingDefs"/>) while
/// <see cref="On"/>, whatever the 아덴 게이지's switch. <see cref="Left"/>/<see cref="Top"/> NaN = never placed,
/// <see cref="Width"/> the length scale, <see cref="Height"/> the thickness scale (as the gauge window).</summary>
public sealed class RingSettings : IWindowPlace
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;
    public bool On { get; set; } = true;
    public string LeftFill { get; set; } = "#E6B422";
    public string RightFill { get; set; } = "#34B04A";
    public string Empty { get; set; } = "#3A3A3A";
    public double Opacity { get; set; } = 1.0;

    /// <summary>The bar's thickness at <see cref="Height"/> 1, in DIPs (the gauge's default 두께).</summary>
    public const double Thickness = 6;

    /// <summary>The bar's length × thickness in DIPs: as long as the gauge window at the same 길이, <see cref="Thickness"/> × 두께.</summary>
    public static (double W, double H) Size(RingSettings r) =>
        (SplitLayout.GaugeLength(r.Width), Math.Max(2, Math.Round(Thickness * Math.Clamp(r.Height, ClassProfiles.MinScale, ClassProfiles.MaxScale))));

    /// <summary>Each half's filled length out of <paramref name="half"/> DIPs, fills clamped to 0–1.</summary>
    public static (double Left, double Right) Fills(RingState s, double half) =>
        (Math.Round(half * Math.Clamp(s.Left, 0, 1)), Math.Round(half * Math.Clamp(s.Right, 0, 1)));
}

/// <summary>The ring bar's fill per half, 0–1 (<see cref="Snapshot.Ring"/>).</summary>
public sealed record RingState(double Left, double Right);
