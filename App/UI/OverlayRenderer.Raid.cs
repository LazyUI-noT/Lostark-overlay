using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The 줄 수 창 (설정 → 레이드, spec § B): the boss bar count as a number on its background colour, glowing gold while
/// an alert is on. Nothing while no gate is recognised.</summary>
public sealed partial class OverlayRenderer
{
    const double RaidLength = 2.2, RaidThickness = 0.8;   // × a skill box, × the window's 너비 / 높이
    const double RaidText = 0.62;                          // the number, share of the cell's height
    const double RaidGlowPeriod = 0.8;                     // s per pulse
    static readonly Color RaidGlow = Color.FromRgb(0xFF, 0xC8, 0x3C);

    /// <summary>The 줄 수 창 alone.</summary>
    public bool RaidOnly { get; set; }

    Rect RaidCell() => new(Bleed, Bleed, BoxSize(Settings.Raids.Width, RaidLength), BoxSize(Settings.Raids.Height, RaidThickness));

    Size RaidPanelSize()
    {
        var c = RaidCell();
        return new(c.Right + Bleed, c.Bottom + Bleed);
    }

    void DrawRaid(DrawingContext dc, double ms, bool reduced)
    {
        if (_player?.Snapshot.Raid is not { } status) return;
        var r = Settings.Raids;
        var cell = RaidCell();
        double corner = cell.Height * 0.15;
        dc.PushOpacity(Math.Clamp(r.Opacity, 0.1, 1));
        dc.DrawRoundedRectangle(Solid(UiColor.Parse(r.Background, Color.FromArgb(0xCC, 0x10, 0x14, 0x18))), null, cell, corner, corner);
        if (status.Text is { } text) DrawOutlined(dc, cell, text, Math.Round(cell.Height * RaidText * 0.55 / TimerFontStep) * TimerFontStep);
        else
        {
            double font = Math.Round(cell.Height * RaidText / TimerFontStep) * TimerFontStep;
            DrawOutlined(dc, cell, status.Count?.ToString(CultureInfo.InvariantCulture) ?? "–", font);
        }
        if (status.Glow)
        {
            // Under reduced motion a steady ring; else it pulses, and the render loop runs for it.
            double alpha = reduced ? 1 : 0.55 + 0.45 * Math.Sin(ms / 1000 * 2 * Math.PI / RaidGlowPeriod);
            var pen = new Pen(Solid(Color.FromArgb((byte)(255 * alpha), RaidGlow.R, RaidGlow.G, RaidGlow.B)), Math.Max(2, cell.Height * 0.08));
            var ring = new Rect(cell.X + pen.Thickness / 2, cell.Y + pen.Thickness / 2, cell.Width - pen.Thickness, cell.Height - pen.Thickness);
            dc.DrawRoundedRectangle(null, pen, ring, corner, corner);
            _animating |= !reduced && _frozenAt is null;
        }
        dc.Pop();
    }
}
