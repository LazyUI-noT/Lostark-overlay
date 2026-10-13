using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>The 단심 window (user, 2026-10-13). 막대: full at the trigger and draining (가로: right to left, 세로: top to
/// bottom) over 심판's 6 s in <see cref="JudgmentSettings.Color"/>, then full again over the 24 s cooldown in
/// <see cref="JudgmentSettings.CooldownColor"/> or a dim of the colour, with the seconds on it (시간 표시); empty when ready,
/// or gone with 비었을 때 숨기기. 아이콘: the game's 심판 icon with its seconds while it is on,
/// optionally dim with the seconds to ready through the cooldown, and a flash and fade when it is ready (JudgmentState.Icon).</summary>
public sealed partial class OverlayRenderer
{
    const double JudgmentBarLength = 4, JudgmentBarThickness = 0.45;   // × a skill box, × the window's 너비 / 높이
    const double JudgmentDim = 0.45;                                    // 한 색으로: the cooldown part at this lightness
    const double JudgmentIconText = 0.55;                               // the seconds, share of the icon's height
    static readonly Color JudgmentVeil = Color.FromArgb(0xA0, 0, 0, 0), JudgmentFlash = Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF);
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
        if (j.Display == JudgmentDisplay.ReadyIcon) return new Rect(Bleed, Bleed, BoxSize(j.Width), BoxSize(j.Width));
        return j.Side
            ? new Rect(Bleed, Bleed, BoxSize(j.Width, JudgmentBarThickness), BoxSize(j.Height, JudgmentBarLength))
            : new Rect(Bleed, Bleed, BoxSize(j.Width, JudgmentBarLength), BoxSize(j.Height, JudgmentBarThickness));
    }

    Size JudgmentPanelSize()
    {
        var c = JudgmentCell();
        return new(c.Right + Bleed, c.Bottom + Bleed);
    }

    void DrawJudgment(DrawingContext dc, TimeSpan now, bool reduced)
    {
        if (_player?.Snapshot.Judgment is not { } state) return;
        var j = Settings.Judgment;
        var cell = JudgmentCell();
        dc.PushOpacity(Math.Clamp(j.Opacity, JudgmentSettings.MinOpacity, 1));
        bool icon = j.Display == JudgmentDisplay.ReadyIcon;
        if (icon) DrawJudgmentIcon(dc, cell, state.Icon(now, j.IconCooldown), reduced);
        else Faint(dc, j.HideWhenEmpty && state.Phase(now) == JudgmentPhase.Ready, () => DrawJudgmentBar(dc, cell, state, now, j));
        dc.Pop();
        // On the clock: the bar drains, the countdown ticks, and the icon comes back with its flash.
        if (_frozenAt is null) _moving |= state.Phase(now) != JudgmentPhase.Ready || icon && state.Icon(now, j.IconCooldown) is not null;
    }

    /// <summary>Under reduced motion there is no white flash, and the fade steps at the slow frame rate.</summary>
    void DrawJudgmentIcon(DrawingContext dc, Rect cell, JudgmentIcon? look, bool reduced)
    {
        if (look is not { } i) return;
        _animating |= _frozenAt is null && !reduced && (i.Flash || i.Opacity < 1);   // the flash and fade want smooth frames
        dc.PushOpacity(i.Opacity);
        dc.DrawImage(JudgmentIcon, cell);
        if (i.Dim) dc.DrawRectangle(Solid(JudgmentVeil), null, cell);
        if (i.Flash && !reduced) dc.DrawRectangle(Solid(JudgmentFlash), null, cell);
        double font = Math.Round(cell.Height * JudgmentIconText / TimerFontStep) * TimerFontStep;
        if (i.Seconds is { } s && font >= MinTimerFont) DrawOutlined(dc, cell, s.ToString(CultureInfo.InvariantCulture), font);
        dc.Pop();
    }

    void DrawJudgmentBar(DrawingContext dc, Rect cell, JudgmentState state, TimeSpan now, JudgmentSettings j)
    {
        var colour = Parse(j.Color);
        var wait = j.OneColor ? Level(colour, JudgmentDim) : Parse(j.CooldownColor);
        dc.DrawRectangle(Solid(DebuffEmpty), null, cell);
        var (active, cooldown) = state.Fill(now);
        var (fill, brush) = active > 0 ? (active, colour) : (cooldown, wait);
        if (fill > 0)
            dc.DrawRectangle(Solid(brush), null, j.Side
                ? new Rect(cell.X, cell.Bottom - cell.Height * fill, cell.Width, cell.Height * fill)   // from the bottom: drains from the top
                : new Rect(cell.X, cell.Y, cell.Width * fill, cell.Height));                          // from the left: drains from the right
        double font = Math.Round(Math.Min(cell.Width, cell.Height) * 0.75 / TimerFontStep) * TimerFontStep;
        if (j.ShowLabel && font >= MinTimerFont && state.LabelSeconds(now) is { } s)
            DrawOutlined(dc, cell, s.ToString(CultureInfo.InvariantCulture), font);
    }
}
