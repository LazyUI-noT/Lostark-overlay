using System.Windows;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The 기력·충격 창 (breaker-aden.plan.md phase 5): 브레이커's ring as one bar, 기력 filling from the left edge and
/// 충격 from the right edge toward the middle (user, 2026-11-14). Nothing while the snapshot has no ring.</summary>
public sealed partial class OverlayRenderer
{
    const double RingSplit = 3;   // DIPs of the dark midline between the halves (user, 2026-11-15: thicker)
    static readonly Color RingMidline = Color.FromRgb(0x10, 0x10, 0x10);   // as the gauge's default 구분선
    // 수라의길's cue (RingCue): an outline round the half to press next, in its colour (the whole bar before the first skill);
    // after Z thicker and blinking fast. RingSettings.Glow picks when it shows.
    const double CueLine = 2, CueLineHard = 3, CueBlinkHz = 4, CueBlinkLow = 0.15;

    /// <summary>The 기력·충격 창 alone.</summary>
    public bool RingOnly { get; set; }

    Rect RingCell()
    {
        var (w, h) = RingSettings.Size(Settings.Ring);
        return new(Bleed, Bleed, w, h);
    }

    Size RingPanelSize()
    {
        var c = RingCell();
        return new(c.Right + Bleed, c.Bottom + Bleed);
    }

    void DrawRing(DrawingContext dc, TimeSpan now, double ms, bool reduced)
    {
        if (_player?.Snapshot.Ring is not { } ring) return;
        var r = Settings.Ring;
        if (!ShowHidden && r.OutOfRaid(_player.Snapshot.AutoShows(now))) return;   // 자동 (or the tray's 자동 → 항상 test)
        var cell = RingCell();
        double half = (cell.Width - RingSplit) / 2;
        var (left, right) = RingSettings.Fills(ring, half);
        var leftFill = UiColor.Parse(r.LeftFill, Color.FromRgb(0xE6, 0xB4, 0x22));
        var rightFill = UiColor.Parse(r.RightFill, Color.FromRgb(0x34, 0xB0, 0x4A));
        dc.PushOpacity(Math.Clamp(r.Opacity, 0.1, 1));
        dc.DrawRectangle(Solid(UiColor.Parse(r.Empty, Color.FromRgb(0x3A, 0x3A, 0x3A))), null, cell);
        if (left > 0) dc.DrawRectangle(Solid(leftFill), null, new Rect(cell.X, cell.Y, left, cell.Height));
        if (right > 0) dc.DrawRectangle(Solid(rightFill), null, new Rect(cell.Right - right, cell.Y, right, cell.Height));
        dc.DrawRectangle(Solid(RingMidline), null, new Rect(cell.X + half, cell.Y, RingSplit, cell.Height));
        if (_player.Snapshot.RingCue is { } cue && cue.Drawn(r.Glow))
            DrawRingCue(dc, cell, half, cue, cue.Show == RingSide.충격 ? rightFill : leftFill, ms, reduced);
        dc.Pop();
    }

    /// <summary>The outline round the cue's half, outside the bar (in the window's bleed) so the fill stays readable.</summary>
    void DrawRingCue(DrawingContext dc, Rect cell, double half, RingCue cue, Color colour, double ms, bool reduced)
    {
        double line = cue.AfterZ ? CueLineHard : CueLine;
        var box = cue.Show switch
        {
            RingSide.기력 => new Rect(cell.X, cell.Y, half, cell.Height),
            RingSide.충격 => new Rect(cell.X + half + RingSplit, cell.Y, half, cell.Height),
            _ => cell,
        };
        box.Inflate(line / 2, line / 2);
        if (cue.AfterZ) { dc.PushOpacity(Blink(ms, reduced, CueBlinkHz, CueBlinkLow)); _animating |= _frozenAt is null && !reduced; }
        dc.DrawRectangle(null, new Pen(Solid(colour), line), box);
        if (cue.AfterZ) dc.Pop();
    }
}
