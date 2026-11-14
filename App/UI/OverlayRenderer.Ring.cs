using System.Windows;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The 기력·충격 창 (breaker-aden.plan.md phase 5): 브레이커's ring as one bar, 기력 filling from the left edge and
/// 충격 from the right edge toward the middle (user, 2026-11-14). Nothing while the snapshot has no ring.</summary>
public sealed partial class OverlayRenderer
{
    const double RingSplit = 1;   // DIPs left empty between the halves

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

    void DrawRing(DrawingContext dc)
    {
        if (_player?.Snapshot.Ring is not { } ring) return;
        var r = Settings.Ring;
        var cell = RingCell();
        double half = (cell.Width - RingSplit) / 2;
        var (left, right) = RingSettings.Fills(ring, half);
        dc.PushOpacity(Math.Clamp(r.Opacity, 0.1, 1));
        dc.DrawRectangle(Solid(UiColor.Parse(r.Empty, Color.FromRgb(0x3A, 0x3A, 0x3A))), null, cell);
        if (left > 0) dc.DrawRectangle(Solid(UiColor.Parse(r.LeftFill, Color.FromRgb(0xE6, 0xB4, 0x22))), null, new Rect(cell.X, cell.Y, left, cell.Height));
        if (right > 0) dc.DrawRectangle(Solid(UiColor.Parse(r.RightFill, Color.FromRgb(0x34, 0xB0, 0x4A))), null, new Rect(cell.Right - right, cell.Y, right, cell.Height));
        dc.Pop();
    }
}
