using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>The 단심 window (user, 2026-10-13). 막대: one 30 s bar, full at the trigger and draining right to left, its last
/// sixth (심판 on) in <see cref="JudgmentSettings.Color"/> and the rest (the cooldown) in <see cref="JudgmentSettings.CooldownColor"/>
/// or a dim of the colour, a fixed line where 심판 ends, the seconds left on it; empty when ready. 준비 아이콘: the game's
/// 심판 icon while it is ready, nothing otherwise.</summary>
public sealed partial class OverlayRenderer
{
    const double JudgmentBarLength = 4, JudgmentBarThickness = 0.45;   // × a skill box, × the window's 너비 / 높이
    const double JudgmentDim = 0.45;                                    // 한 색으로: the cooldown part at this lightness
    const double JudgmentLine = 2;
    static readonly ImageSource JudgmentIcon = JudgmentImage();

    static ImageSource JudgmentImage()
    {
        var icon = JudgmentReader.Icon;
        var bmp = BitmapSource.Create(icon.Width, icon.Height, 96, 96, PixelFormats.Bgr32, null, icon.Bgra, icon.Stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>The 단심 window: 심판's cycle alone.</summary>
    public bool JudgmentOnly { get; set; }

    Rect JudgmentCell()
    {
        var j = Settings.Judgment;
        return j.Display == JudgmentDisplay.ReadyIcon
            ? new Rect(Bleed, Bleed, BoxSize(j.Width), BoxSize(j.Width))
            : new Rect(Bleed, Bleed, BoxSize(j.Width, JudgmentBarLength), BoxSize(j.Height, JudgmentBarThickness));
    }

    Size JudgmentPanelSize()
    {
        var c = JudgmentCell();
        return new(c.Right + Bleed, c.Bottom + Bleed);
    }

    void DrawJudgment(DrawingContext dc, TimeSpan now)
    {
        if (_player?.Snapshot.Judgment is not { } state) return;
        var j = Settings.Judgment;
        var cell = JudgmentCell();
        dc.PushOpacity(Math.Clamp(j.Opacity, JudgmentSettings.MinOpacity, 1));
        if (j.Display == JudgmentDisplay.ReadyIcon)
        {
            if (state.Phase(now) == JudgmentPhase.Ready) dc.DrawImage(JudgmentIcon, cell);
        }
        else DrawJudgmentBar(dc, cell, state, now, j);
        dc.Pop();
        if (_frozenAt is null) _moving |= state.Phase(now) != JudgmentPhase.Ready;   // the bar drains, and the icon comes back, on the clock
    }

    void DrawJudgmentBar(DrawingContext dc, Rect cell, JudgmentState state, TimeSpan now, JudgmentSettings j)
    {
        var colour = Parse(j.Color);
        dc.DrawRectangle(Solid(DebuffEmpty), null, cell);
        var (active, cooldown) = state.Fill(now);
        double w = cell.Width;
        if (cooldown > 0) dc.DrawRectangle(Solid(j.OneColor ? Level(colour, JudgmentDim) : Parse(j.CooldownColor)), null, new Rect(cell.X, cell.Y, w * cooldown, cell.Height));
        if (active > 0) dc.DrawRectangle(Solid(colour), null, new Rect(cell.X + w * cooldown, cell.Y, w * active, cell.Height));
        // Where 심판 ends: 24 of the bar's 30 s from the left.
        double x = cell.X + Math.Round(w * (1 - JudgmentState.Duration / JudgmentState.Cycle));
        dc.DrawLine(new Pen(TextBrush, JudgmentLine), new Point(x, cell.Y), new Point(x, cell.Bottom));
        double font = Math.Round(cell.Height * 0.75 / TimerFontStep) * TimerFontStep;
        if (j.ShowLabel && font >= MinTimerFont && state.LabelSeconds(now) is { } s)
            DrawOutlined(dc, cell, s.ToString(CultureInfo.InvariantCulture), font);
    }
}
